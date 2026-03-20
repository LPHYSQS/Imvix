namespace Imvix.Models
{
    public sealed class ConversionProgress
    {
        public ConversionProgress(int processedCount, int totalCount, string fileName, bool succeeded, string? error)
        {
            ProcessedCount = processedCount;
            TotalCount = totalCount;
            FileName = fileName;
            Succeeded = succeeded;
            Error = error;
        }

        public int ProcessedCount { get; }

        public int TotalCount { get; }

        public string FileName { get; }

        public bool Succeeded { get; }

        public string? Error { get; }
    }
}
