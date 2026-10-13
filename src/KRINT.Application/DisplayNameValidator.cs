namespace KRINT.Application
{
    /// <summary>One rule for an instance's display name, shared by create, provision, rename and
    /// the instances-file reconcile so they cannot drift apart.</summary>
    public static class DisplayNameValidator
    {
        public const int MaxLength = 64;

        /// <summary>Returns the trimmed name, or throws <see cref="ArgumentException"/> with a
        /// user-facing message when it is empty or too long.</summary>
        public static string Normalize(string? displayName)
        {
            var name = displayName?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Display name must not be empty.", nameof(displayName));
            if (name.Length > MaxLength)
                throw new ArgumentException($"Display name must be {MaxLength} characters or fewer.", nameof(displayName));
            return name;
        }
    }
}
