using System.Text.RegularExpressions;

namespace KRINT.Infrastructure.Services
{
    public static class InnerDatabaseNameValidator
    {
        // Conservative cross-engine identifier rule: letter/underscore, then [A-Za-z0-9_-], 1-63 chars.
        // Identifiers can't be parameterised in DDL, so we validate strictly before interpolating.
        private static readonly Regex Pattern = new("^[A-Za-z_][A-Za-z0-9_-]{0,62}$", RegexOptions.Compiled);

        public static void Require(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !Pattern.IsMatch(name))
            {
                throw new ArgumentException(
                    $"Invalid database name '{name}'. Must start with a letter/underscore and contain only [A-Za-z0-9_-], max 63 chars.",
                    nameof(name));
            }
        }
    }
}
