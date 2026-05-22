namespace KRINT.Application.Dtos
{
    public record DashboardStatsDto
    {
        public required int TotalInstances { get; init; }
        public required int RunningInstances { get; init; }
        /// <summary>Aggregate memory usage across all running managed containers, in bytes.</summary>
        public required long TotalMemoryBytes { get; init; }
        /// <summary>Combined CPU% of all managed containers, 0..100 - the share of host CPU
        /// capacity they consume (excludes everything else running on the host).</summary>
        public required double TotalCpuPercent { get; init; }
        public required IReadOnlyList<EngineCountDto> PerEngine { get; init; }
        public required IReadOnlyList<ActivityEntryDto> RecentActivity { get; init; }
    }

    public record EngineCountDto
    {
        public required string Engine { get; init; }
        public required int Count { get; init; }
    }
}
