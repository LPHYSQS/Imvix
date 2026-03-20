namespace ImvixPro.Models
{
    public sealed class ImageAnalysisResult
    {
        public ImageAnalysisResult(
            bool hasTransparency,
            ImageContentKind contentKind,
            OutputImageFormat primaryRecommendation,
            OutputImageFormat? secondaryRecommendation)
        {
            HasTransparency = hasTransparency;
            ContentKind = contentKind;
            PrimaryRecommendation = primaryRecommendation;
            SecondaryRecommendation = secondaryRecommendation;
        }

        public bool HasTransparency { get; }

        public ImageContentKind ContentKind { get; }

        public OutputImageFormat PrimaryRecommendation { get; }

        public OutputImageFormat? SecondaryRecommendation { get; }
    }
}
