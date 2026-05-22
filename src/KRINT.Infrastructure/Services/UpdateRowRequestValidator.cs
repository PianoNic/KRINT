using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public static class UpdateRowRequestValidator
    {
        public static void Require(UpdateRowRequest request)
        {
            if (request.Columns.Count == 0)
                throw new ArgumentException("At least one column is required.", nameof(request));
            if (request.Columns.Count != request.OriginalValues.Count || request.Columns.Count != request.NewValues.Count)
                throw new ArgumentException("Columns, OriginalValues, and NewValues must all have the same length.", nameof(request));
            foreach (var col in request.Columns) InnerDatabaseNameValidator.Require(col);
        }
    }
}
