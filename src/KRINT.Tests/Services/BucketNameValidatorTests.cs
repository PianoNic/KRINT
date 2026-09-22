using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    public class BucketNameValidatorTests
    {
        [Test]
        [Arguments("krt-test")]
        [Arguments("abc")]
        [Arguments("my-bucket-01")]
        public async Task Accepts_valid_bucket_names(string name)
        {
            await Assert.That(() => BucketNameValidator.Require(name)).ThrowsNothing();
        }

        [Test]
        [Arguments("krt_test")]
        [Arguments("Upper")]
        [Arguments("ab")]
        [Arguments("-lead")]
        [Arguments("trail-")]
        [Arguments("dou--ble")]
        [Arguments("")]
        public async Task Rejects_names_the_object_stores_refuse(string name)
        {
            await Assert.That(() => BucketNameValidator.Require(name)).Throws<ArgumentException>();
        }
    }
}
