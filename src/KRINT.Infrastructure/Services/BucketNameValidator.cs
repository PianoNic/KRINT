using System.Text.RegularExpressions;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Object-store "databases" are S3 buckets or Azure blob containers, and both APIs reject
    /// names the generic identifier rule allows (underscores, uppercase, a leading hyphen). Check
    /// the intersection of the two grammars up front so the user gets a sentence instead of a
    /// stack trace from the storage SDK.
    /// </summary>
    public static class BucketNameValidator
    {
        private static readonly Regex Pattern = new("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.Compiled);

        public static void Require(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !Pattern.IsMatch(name) || name.Contains("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Invalid bucket name '{name}'. Use 3-63 lowercase letters, digits or single hyphens, starting and ending with a letter or digit.",
                    nameof(name));
            }
        }
    }
}
