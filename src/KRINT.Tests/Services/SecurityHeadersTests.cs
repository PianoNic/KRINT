namespace KRINT.Tests.Services
{
    public class SecurityHeadersTests
    {
        [Test]
        public async Task Allows_the_identity_provider_origin_for_fetches_and_form_posts()
        {
            var csp = KRINT.API.SecurityHeaders.BuildContentSecurityPolicy("https://auth.example.com/realms/krint");
            await Assert.That(csp).Contains("connect-src 'self' ws: wss: https://auth.example.com");
            await Assert.That(csp).Contains("form-action 'self' https://auth.example.com");
            await Assert.That(csp).Contains("frame-ancestors 'none'");
        }

        [Test]
        public async Task Stays_self_only_without_an_authority()
        {
            var csp = KRINT.API.SecurityHeaders.BuildContentSecurityPolicy(null);
            await Assert.That(csp).Contains("connect-src 'self' ws: wss:;");
            await Assert.That(csp).DoesNotContain("https://");
        }
    }
}
