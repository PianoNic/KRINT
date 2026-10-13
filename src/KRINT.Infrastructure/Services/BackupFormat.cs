namespace KRINT.Infrastructure.Services
{
    /// <summary>Tells the multi-database tar backups apart from the older single-database files
    /// (pg_dump custom archive, raw SQL Server .bak) so both keep restoring.</summary>
    internal static class BackupFormat
    {
        private const int HeaderSize = 512;

        /// <summary>POSIX/GNU tar headers carry "ustar" at offset 257.</summary>
        public static bool IsTar(ReadOnlySpan<byte> header)
            => header.Length >= 262 && header.Slice(257, 5).SequenceEqual("ustar"u8);

        /// <summary>Peeks at the head of <paramref name="dump"/> and rewinds it. A non-seekable stream
        /// is spooled to a temp file first; the returned stream must then be disposed by the caller
        /// (it differs from <paramref name="dump"/>).</summary>
        public static async Task<(bool IsTar, Stream Stream)> SniffAsync(Stream dump, CancellationToken cancellationToken)
        {
            var stream = dump;
            if (!dump.CanSeek)
            {
                stream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                await dump.CopyToAsync(stream, 81920, cancellationToken);
                stream.Position = 0;
            }

            var start = stream.Position;
            var header = new byte[HeaderSize];
            var read = 0;
            while (read < header.Length)
            {
                var n = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
                if (n == 0) break;
                read += n;
            }
            stream.Position = start;
            return (IsTar(header.AsSpan(0, read)), stream);
        }
    }
}
