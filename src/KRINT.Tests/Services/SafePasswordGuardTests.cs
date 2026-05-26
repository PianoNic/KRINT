using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    public class SafePasswordGuardTests
    {
        [Test]
        [Arguments("Alphanumeric123")]
        [Arguments("with-dashes")]
        [Arguments("under_scores")]
        [Arguments("dots.and.tildes~ok")]
        [Arguments("aA1-_.~")]
        public async Task Require_AllowedAlphabet_DoesNotThrow(string password)
        {
            SafePasswordGuard.Require(password);
            await Assert.That(password).IsNotEmpty();
        }

        [Test]
        [Arguments("has space")]
        [Arguments("with'quote")]
        [Arguments("semi;colon")]
        [Arguments("backtick`bad")]
        [Arguments("dollar$sign")]
        [Arguments("ampersand&here")]
        [Arguments("at@sign")]
        [Arguments("hashtag#here")]
        public async Task Require_DisallowedCharacters_Throws(string password)
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                Task.Run(() => SafePasswordGuard.Require(password)));
            await Assert.That(ex!.Message).Contains("unsupported character");
        }

        [Test]
        public async Task Require_Empty_Throws()
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() => SafePasswordGuard.Require("")));
            await Assert.That(ex!.Message).Contains("must not be empty");
        }
    }
}
