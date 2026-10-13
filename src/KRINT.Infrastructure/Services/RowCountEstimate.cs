namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Table browser totals: an exact COUNT(*) scans the whole table, which on a large table costs
    /// more than the page itself. Each engine's planner/catalog statistics are tried first; when
    /// they say the table is large the estimate is used (and flagged as such), otherwise - small
    /// table, never-analyzed table, view, or no statistics at all - the exact count runs, since it
    /// is cheap there and keeps pagination exact.
    /// </summary>
    internal static class RowCountEstimate
    {
        public const long ExactBelow = 100_000;

        public static async Task<(long? Total, bool IsEstimate)> ResolveAsync(Func<Task<long?>> estimate, Func<Task<long?>> exact)
        {
            long? estimated = null;
            try { estimated = await estimate(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Statistics are a shortcut, never a requirement: fall through to the exact count.
            }

            if (estimated is >= ExactBelow) return (estimated, true);
            return (await exact(), false);
        }
    }
}
