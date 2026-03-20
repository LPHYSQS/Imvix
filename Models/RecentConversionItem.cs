namespace ImvixPro.Models
{
    public sealed class RecentConversionItem
    {
        public required string TimestampText { get; init; }

        public required string SummaryText { get; init; }

        public required string DetailText { get; init; }

        public string FailureLogPath { get; init; } = string.Empty;

        public bool HasFailureLog => !string.IsNullOrWhiteSpace(FailureLogPath);
    }
}
