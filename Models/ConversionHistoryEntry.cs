using System;

namespace ImvixPro.Models
{
    public sealed class ConversionHistoryEntry
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

        public ConversionTriggerSource Source { get; set; } = ConversionTriggerSource.Manual;

        public OutputImageFormat OutputFormat { get; set; } = OutputImageFormat.Png;

        public int TotalCount { get; set; }

        public int ProcessedCount { get; set; }

        public int SuccessCount { get; set; }

        public int FailureCount { get; set; }

        public long OriginalTotalBytes { get; set; }

        public long EstimatedMinBytes { get; set; }

        public long EstimatedMaxBytes { get; set; }

        public double DurationMilliseconds { get; set; }

        public bool WasCanceled { get; set; }

        public string FailureLogPath { get; set; } = string.Empty;
    }
}
