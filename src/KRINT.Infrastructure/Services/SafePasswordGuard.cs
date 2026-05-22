namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Guards against SQL injection in passwords inlined into DDL (CREATE ROLE / IDENTIFIED BY).
    /// DDL doesn't accept parameter placeholders, so the password is interpolated; this enforces
    /// that the password contains nothing that could break out of the string literal.
    /// Today the generator only emits [A-Za-z0-9], so this is a belt-and-suspenders check.
    /// </summary>
    internal static class SafePasswordGuard
    {
        public static void Require(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password must not be empty.", nameof(password));

            foreach (var c in password)
            {
                // Allow alphanumerics + a small printable safe set; reject anything that can
                // terminate a SQL string literal or escape it.
                var safe = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '-' || c == '_' || c == '.' || c == '~';
                if (!safe)
                    throw new ArgumentException("Password contains an unsupported character. KRINT generates and accepts only [A-Za-z0-9-_.~] passwords for inlineable DDL safety.", nameof(password));
            }
        }
    }
}
