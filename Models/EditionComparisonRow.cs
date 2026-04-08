namespace Imvix.Models
{
    public sealed class EditionComparisonRow
    {
        public required string Title { get; init; }

        public required string Description { get; init; }

        public required bool IsStandardIncluded { get; init; }

        public required bool IsProIncluded { get; init; }

        public required string StandardStatus { get; init; }

        public required string ProStatus { get; init; }

        public bool IsShared => IsStandardIncluded && IsProIncluded;

        public bool IsStandardOnly => IsStandardIncluded && !IsProIncluded;

        public bool IsProOnly => !IsStandardIncluded && IsProIncluded;

        public bool IsStandardUnavailable => !IsStandardIncluded;

        public bool IsProUnavailable => !IsProIncluded;
    }
}
