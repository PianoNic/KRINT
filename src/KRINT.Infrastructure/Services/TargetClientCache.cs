using System.Security.Cryptography;
using System.Text;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Keeps one long-lived driver client (Cassandra cluster, Neo4j driver, Redis multiplexer) per
    /// target instead of building and tearing one down for every call; those clients own their own
    /// connection pools and are meant to be shared. A client is handed out as a lease, and one
    /// nobody has leased for <see cref="IdleTimeout"/> is disposed. A client whose creation fails
    /// is dropped right away so the next call tries again.
    /// </summary>
    internal sealed class TargetClientCache<T> where T : class
    {
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

        private readonly Func<InnerDatabaseTarget, Task<T>> _create;
        private readonly Func<T, ValueTask> _dispose;
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Timer _sweeper;

        public TargetClientCache(Func<InnerDatabaseTarget, Task<T>> create, Func<T, ValueTask> dispose)
        {
            _create = create;
            _dispose = dispose;
            _sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public async Task<Lease> AcquireAsync(InnerDatabaseTarget target)
        {
            var key = KeyOf(target);
            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new Entry(new Lazy<Task<T>>(() => _create(target)));
                    _entries[key] = entry;
                }
                entry.InUse++;
                entry.LastUsed = DateTime.UtcNow;
            }

            try
            {
                return new Lease(this, key, entry, await entry.Client.Value);
            }
            catch
            {
                Release(entry);
                Invalidate(key, entry);
                throw;
            }
        }

        /// <summary>Drops a client that turned out to be unusable; it is disposed once no lease holds it.</summary>
        private void Invalidate(string key, Entry entry)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(key);
                entry.Evicted = true;
                if (entry.InUse > 0) return;
            }
            _ = DisposeEntryAsync(entry);
        }

        private void Release(Entry entry)
        {
            bool disposeNow;
            lock (_gate)
            {
                entry.InUse--;
                entry.LastUsed = DateTime.UtcNow;
                disposeNow = entry.Evicted && entry.InUse == 0;
            }
            if (disposeNow) _ = DisposeEntryAsync(entry);
        }

        private void Sweep()
        {
            List<Entry> idle;
            lock (_gate)
            {
                var cutoff = DateTime.UtcNow - IdleTimeout;
                var keys = _entries.Where(e => e.Value.InUse == 0 && e.Value.LastUsed < cutoff).Select(e => e.Key).ToList();
                idle = new List<Entry>(keys.Count);
                foreach (var k in keys)
                {
                    idle.Add(_entries[k]);
                    _entries[k].Evicted = true;
                    _entries.Remove(k);
                }
            }
            foreach (var entry in idle) _ = DisposeEntryAsync(entry);
        }

        private async Task DisposeEntryAsync(Entry entry)
        {
            try
            {
                if (!entry.Client.IsValueCreated) return;
                var client = await entry.Client.Value;
                await _dispose(client);
            }
            catch
            {
                // Creation failed (nothing to dispose) or the client was already broken.
            }
        }

        // The password is part of the identity (a rotated password must not reuse the old client),
        // but only its hash is kept as a dictionary key.
        private static string KeyOf(InnerDatabaseTarget target)
        {
            var secret = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.Password ?? string.Empty)));
            return $"{target.Host}|{target.Port}|{target.Username}|{secret}";
        }

        internal sealed class Entry(Lazy<Task<T>> client)
        {
            public Lazy<Task<T>> Client { get; } = client;
            public int InUse;
            public DateTime LastUsed;
            public bool Evicted;
        }

        public sealed class Lease : IDisposable
        {
            private readonly TargetClientCache<T> _owner;
            private readonly string _key;
            private readonly Entry _entry;
            private int _released;

            internal Lease(TargetClientCache<T> owner, string key, Entry entry, T client)
            {
                _owner = owner;
                _key = key;
                _entry = entry;
                Client = client;
            }

            public T Client { get; }

            /// <summary>Marks the client as broken so the next call builds a fresh one.</summary>
            public void Invalidate() => _owner.Invalidate(_key, _entry);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) _owner.Release(_entry);
            }
        }
    }
}
