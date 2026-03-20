namespace ImvixPro.Models
{
    public sealed class SizeEstimateResult
    {
        public SizeEstimateResult(
            bool isAvailable,
            long originalTotalBytes,
            long estimatedMinBytes,
            long estimatedMaxBytes)
        {
            IsAvailable = isAvailable;
            OriginalTotalBytes = originalTotalBytes;
            EstimatedMinBytes = estimatedMinBytes;
            EstimatedMaxBytes = estimatedMaxBytes;
        }

        public bool IsAvailable { get; }

        public long OriginalTotalBytes { get; }

        public long EstimatedMinBytes { get; }

        public long EstimatedMaxBytes { get; }
    }
}
