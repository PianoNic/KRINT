using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    public class SecretGeneratorServiceTests
    {
        [Test]
        public async Task Generate_Default_ReturnsThirtyTwoCharacters()
        {
            var generator = new SecretGeneratorService();

            var secret = generator.Generate();

            await Assert.That(secret).Length().IsEqualTo(32);
        }

        [Test]
        public async Task Generate_Default_ContainsOnlyAlphanumericCharacters()
        {
            var generator = new SecretGeneratorService();

            var secret = generator.Generate();

            await Assert.That(secret.All(char.IsLetterOrDigit)).IsTrue();
        }

        [Test]
        public async Task Generate_Always_ContainsUpperLowerAndDigit()
        {
            var generator = new SecretGeneratorService();

            for (var i = 0; i < 500; i++)
            {
                var secret = generator.Generate();
                await Assert.That(secret.Any(char.IsAsciiLetterUpper) && secret.Any(char.IsAsciiLetterLower) && secret.Any(char.IsAsciiDigit)).IsTrue();
            }
        }

        [Test]
        public async Task Generate_ConsecutiveCalls_ProducesDifferentSecrets()
        {
            var generator = new SecretGeneratorService();

            var first = generator.Generate();
            var second = generator.Generate();

            await Assert.That(first).IsNotEqualTo(second);
        }
    }
}
