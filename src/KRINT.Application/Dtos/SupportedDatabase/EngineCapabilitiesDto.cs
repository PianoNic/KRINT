namespace KRINT.Application.Dtos.SupportedDatabase
{
    /// <summary>
    /// What an engine supports + how its objects should be labelled in the UI. The frontend
    /// reads this to hide actions that don't apply (e.g. Redis has no tables, Mongo has no
    /// row-edit) and to relabel terms (Mongo says "collection / document", Redis says "DB
    /// number / key", SQL engines stay with "database / table / row").
    /// </summary>
    public record EngineCapabilitiesDto
    {
        public required string DatabaseTerm { get; init; }
        public required string TableTerm { get; init; }
        public required string RowTerm { get; init; }

        public required bool SupportsListDatabases { get; init; }
        public required bool SupportsCreateDatabase { get; init; }
        public required bool SupportsDropDatabase { get; init; }

        public required bool SupportsListTables { get; init; }
        public required bool SupportsDropTable { get; init; }

        public required bool SupportsRowRead { get; init; }
        public required bool SupportsRowInsert { get; init; }
        public required bool SupportsRowEdit { get; init; }
        public required bool SupportsRowDelete { get; init; }

        public required bool SupportsUsers { get; init; }
        public required bool SupportsBackup { get; init; }

        /// <summary>Object/blob stores (SeaweedFS, Azurite): "rows" are uploaded files, so the UI
        /// offers a file-upload dialog (key + file, replace re-uploads) instead of the row-insert form.</summary>
        public bool SupportsObjectUpload { get; init; }
    }
}
