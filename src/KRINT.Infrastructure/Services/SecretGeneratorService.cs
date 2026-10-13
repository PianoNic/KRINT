using System.Security.Cryptography;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class SecretGeneratorService : ISecretGeneratorService
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        private const int Length = 32;

        public string Generate()
        {
            // Engines with a password policy (SQL Server wants three of upper/lower/digit/symbol)
            // refuse to start on a weak one, so always include an upper, a lower and a digit.
            // Redraw instead of patching characters in, so the output stays uniformly random.
            while (true)
            {
                var candidate = RandomNumberGenerator.GetString(Alphabet, Length);
                if (candidate.Any(char.IsAsciiLetterUpper) && candidate.Any(char.IsAsciiLetterLower) && candidate.Any(char.IsAsciiDigit))
                    return candidate;
            }
        }
    }
}
