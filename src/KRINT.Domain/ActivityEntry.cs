namespace KRINT.Domain
{
    public class ActivityEntry : BaseEntity
    {
        public required string Action { get; init; }
        public required string Target { get; init; }
        public Guid? InstanceId { get; init; }
        public string? Engine { get; init; }
        public string? Details { get; init; }
        /// <summary>The preferred_username (or name / email) from the bearer token of the user who triggered the
        /// action. Null for background jobs (scheduler / hosted services).</summary>
        public string? ActorName { get; init; }
    }
}
