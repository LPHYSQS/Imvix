using System;
using System.Collections.Generic;

namespace ImvixPro.Models
{
    public sealed class ConversionSummary
    {
        public ConversionSummary(
            int totalCount,
            int processedCount,
            int successCount,
            IReadOnlyList<ConversionFailure> failures,
            IReadOnlyList<string> outputDirectories,
            TimeSpan duration,
            bool wasCanceled = false)
        {
            TotalCount = totalCount;
            ProcessedCount = processedCount;
            SuccessCount = successCount;
            Failures = failures;
            OutputDirectories = outputDirectories;
            Duration = duration;
            WasCanceled = wasCanceled;
        }

        public int TotalCount { get; }

        public int ProcessedCount { get; }

        public int SuccessCount { get; }

        public int FailureCount => Failures.Count;

        public IReadOnlyList<ConversionFailure> Failures { get; }

        public IReadOnlyList<string> OutputDirectories { get; }

        public TimeSpan Duration { get; }

        public bool WasCanceled { get; }
    }
}
