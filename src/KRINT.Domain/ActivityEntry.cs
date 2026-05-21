namespace KRINT.Domain
{
    public class ActivityEntry : BaseEntity
    {
        public required string Action { get; init; }
        public required string Target { get; init; }
        public Guid? InstanceId { get; init; }
        public string? Engine { get; init; }
        public string? Details { get; init; }
    }
}
