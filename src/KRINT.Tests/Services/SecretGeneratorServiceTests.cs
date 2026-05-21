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
        public async Task Generate_ConsecutiveCalls_ProducesDifferentSecrets()
        {
            var generator = new SecretGeneratorService();

            var first = generator.Generate();
            var second = generator.Generate();

            await Assert.That(first).IsNotEqualTo(second);
        }
    }
}
