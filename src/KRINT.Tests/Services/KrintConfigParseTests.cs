using KRINT.API.Extensions;

namespace KRINT.Tests.Services
{
    /// <summary>krint.yaml keys left without a value (every entry commented out) used to
    /// deserialize to null and crash the hosted services on boot.</summary>
    public class KrintConfigParseTests
    {
        [Test]
        public async Task ParseOptions_EmptyListKeys_ReturnEmptyCollections()
        {
            var options = KrintConfigExtensions.ParseOptions(new StringReader("""
krint:
  port_ranges:
  #  postgres: "5432-5500"
  nodes:
  #  - name: node-eu-1
  #    secret: x
  storage:
"""));

            await Assert.That(options.PortRanges).IsNotNull();
            await Assert.That(options.PortRanges).IsEmpty();
            await Assert.That(options.Nodes).IsNotNull();
            await Assert.That(options.Nodes).IsEmpty();
            await Assert.That(options.Storage).IsNotNull();
        }

        [Test]
        public async Task ParseOptions_EmptyKrintKey_ReturnsDefaults()
        {
            var options = KrintConfigExtensions.ParseOptions(new StringReader("krint:\n"));
            await Assert.That(options.PortRanges).IsEmpty();
            await Assert.That(options.Nodes).IsEmpty();
        }

        [Test]
        public async Task ParseOptions_ValuesAreKept()
        {
            var options = KrintConfigExtensions.ParseOptions(new StringReader("""
krint:
  port_ranges:
    postgres: "5432-5500"
  nodes:
    - name: node-eu-1
      secret: abc
"""));
            await Assert.That(options.PortRanges["postgres"]).IsEqualTo("5432-5500");
            await Assert.That(options.Nodes).Count().IsEqualTo(1);
            await Assert.That(options.Nodes[0].Name).IsEqualTo("node-eu-1");
        }
    }
}
