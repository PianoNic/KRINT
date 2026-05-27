using KRINT.Application.Dtos.Database;

namespace KRINT.Application.Dtos.Migration
{
    /// <summary>
    /// Single event in the migration stream. The wizard renders these as steps in a progress
    /// panel - one event per state transition (probe -&gt; provision -&gt; dump -&gt; restore -&gt;
    /// done | failed). On the terminal events (`Done` or `Failed`) Result / Error / Cleanup are
    /// populated; otherwise they are null.
    /// </summary>
    public record MigrationProgressDto
    {
        public required string Step { get; init; }
        public required string Status { get; init; }
        public required string Message { get; init; }
        public required int CurrentStep { get; init; }
        public required int TotalSteps { get; init; }
        public ProvisionedDatabaseDto? Result { get; init; }
        public IReadOnlyList<CleanupStepDto>? Cleanup { get; init; }
        public string? Error { get; init; }
    }

    public record CleanupStepDto
    {
        public required string Title { get; init; }
        public required string Detail { get; init; }
    }
}
