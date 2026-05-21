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
            return RandomNumberGenerator.GetString(Alphabet, Length);
        }
    }
}
