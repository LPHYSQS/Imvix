namespace ImvixPro.Models
{
    public sealed class ConversionProgress
    {
        public ConversionProgress(
            int processedCount,
            int totalCount,
            int processedFileCount,
            int totalFileCount,
            string fileName,
            int currentFileProcessedCount,
            int currentFileTotalCount,
            bool isFileCompleted,
            bool succeeded,
            string? error)
        {
            ProcessedCount = processedCount;
            TotalCount = totalCount;
            ProcessedFileCount = processedFileCount;
            TotalFileCount = totalFileCount;
            FileName = fileName;
            CurrentFileProcessedCount = currentFileProcessedCount;
            CurrentFileTotalCount = currentFileTotalCount;
            IsFileCompleted = isFileCompleted;
            Succeeded = succeeded;
            Error = error;
        }

        public int ProcessedCount { get; }

        public int TotalCount { get; }

        public int ProcessedFileCount { get; }

        public int TotalFileCount { get; }

        public string FileName { get; }

        public int CurrentFileProcessedCount { get; }

        public int CurrentFileTotalCount { get; }

        public bool IsFileCompleted { get; }

        public bool Succeeded { get; }

        public string? Error { get; }
    }
}
