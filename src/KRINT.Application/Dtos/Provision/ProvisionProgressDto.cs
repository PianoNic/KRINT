namespace KRINT.Application.Dtos.Provision
{
    /// <summary>One event of a streamed provision: a running step, the finished result, or the
    /// failure. Mirrors the migration stream so the SPA renders both the same way.</summary>
    public record ProvisionProgressDto
    {
        /// <summary>"running", "done" or "failed".</summary>
        public required string Status { get; init; }
        public required string Message { get; init; }
        public ProvisionResultDto? Result { get; init; }
        public string? Error { get; init; }
    }
}
