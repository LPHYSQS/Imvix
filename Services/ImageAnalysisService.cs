using Imvix.Models;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Imvix.Services
{
    public sealed class ImageAnalysisService
    {
        private static readonly string[] IconKeywords = ["icon", "logo", "favicon", "appicon", "avatar"];

        private readonly ConcurrentDictionary<string, ImageAnalysisResult> _analysisCache = new(StringComparer.OrdinalIgnoreCase);

        public ImageAnalysisResult Analyze(ImageItemViewModel image)
        {
            return _analysisCache.GetOrAdd(
                image.FilePath,
                _ => AnalyzeCore(image.FilePath, image.FileName, image.PixelWidth, image.PixelHeight));
        }

        public bool HasTransparency(ImageItemViewModel image)
        {
            return Analyze(image).HasTransparency;
        }

        public bool ContainsTransparency(IReadOnlyList<ImageItemViewModel> images)
        {
            foreach (var image in images)
            {
                if (HasTransparency(image))
                {
                    return true;
                }
            }

            return false;
        }

        public SizeEstimateResult Estimate(IReadOnlyList<ImageItemViewModel> images, ConversionOptions options)
        {
            if (images.Count == 0)
            {
                return new SizeEstimateResult(false, 0, 0, 0);
            }

            long originalTotal = 0;
            double estimatedMin = 0;
            double estimatedMax = 0;

            foreach (var image in images)
            {
                originalTotal += image.FileSizeBytes;

                var analysis = Analyze(image);
                var (minBytes, maxBytes) = EstimateSingle(image, analysis, options);
                estimatedMin += minBytes;
                estimatedMax += maxBytes;
            }

            return new SizeEstimateResult(
                true,
                originalTotal,
                (long)Math.Max(0, Math.Round(estimatedMin)),
                (long)Math.Max(0, Math.Round(estimatedMax)));
        }

        private static ImageAnalysisResult AnalyzeCore(string path, string fileName, int width, int height)
        {
            var extension = Path.GetExtension(path);
            var hasTransparency = DetectTransparency(path, extension);
            var contentKind = ClassifyContent(extension, fileName, width, height, hasTransparency);

            var primary = OutputImageFormat.Webp;
            OutputImageFormat? secondary = OutputImageFormat.Png;

            switch (contentKind)
            {
                case ImageContentKind.Photo:
                    primary = OutputImageFormat.Webp;
                    secondary = OutputImageFormat.Jpeg;
                    break;
                case ImageContentKind.TransparentGraphic:
                    primary = OutputImageFormat.Png;
                    secondary = OutputImageFormat.Webp;
                    break;
                case ImageContentKind.Icon:
                    primary = OutputImageFormat.Png;
                    secondary = OutputImageFormat.Ico;
                    break;
                case ImageContentKind.Vector:
                    primary = OutputImageFormat.Svg;
                    secondary = OutputImageFormat.Png;
                    break;
            }

            return new ImageAnalysisResult(hasTransparency, contentKind, primary, secondary);
        }

        private static ImageContentKind ClassifyContent(string extension, string fileName, int width, int height, bool hasTransparency)
        {
            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return ImageContentKind.Vector;
            }

            var normalizedName = fileName.ToLowerInvariant();
            var pixelCount = Math.Max(1L, (long)width * Math.Max(1, height));
            var isSquareish = width > 0 && height > 0 && Math.Abs(width - height) <= Math.Max(width, height) * 0.18;
            var isSmallGraphic = width > 0 && height > 0 && width <= 512 && height <= 512;

            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
                IconKeywords.Any(keyword => normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (isSmallGraphic && isSquareish && hasTransparency))
            {
                return ImageContentKind.Icon;
            }

            if (hasTransparency)
            {
                return ImageContentKind.TransparentGraphic;
            }

            if (pixelCount >= 500_000 && width >= 800 && height >= 500)
            {
                return ImageContentKind.Photo;
            }

            return ImageContentKind.Unknown;
        }

        private static bool DetectTransparency(string path, string extension)
        {
            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var codec = SKCodec.Create(stream);
                if (codec is null)
                {
                    return false;
                }

                if (codec.Info.AlphaType == SKAlphaType.Opaque)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                using var bitmap = SKBitmap.Decode(path);
                if (bitmap is null)
                {
                    return false;
                }

                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        if (bitmap.GetPixel(x, y).Alpha < byte.MaxValue)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static (double MinBytes, double MaxBytes) EstimateSingle(
            ImageItemViewModel image,
            ImageAnalysisResult analysis,
            ConversionOptions options)
        {
            var originalBytes = Math.Max(1d, image.FileSizeBytes);
            var sourcePixels = Math.Max(1d, image.PixelCount);
            var (targetWidth, targetHeight) = CalculateTargetDimensions(image.PixelWidth, image.PixelHeight, options);
            var targetPixels = Math.Max(1d, targetWidth * (double)Math.Max(1, targetHeight));
            var scaleRatio = sourcePixels <= 0 ? 1d : targetPixels / sourcePixels;
            var quality = ResolveQuality(options) / 100d;

            double baseEstimate = options.OutputFormat switch
            {
                OutputImageFormat.Jpeg => originalBytes * scaleRatio * (0.36 + quality * 0.72),
                OutputImageFormat.Webp => originalBytes * scaleRatio * (0.26 + quality * 0.58),
                OutputImageFormat.Png => originalBytes * scaleRatio * (analysis.HasTransparency ? 0.9 : 1.35),
                OutputImageFormat.Bmp => targetPixels * 4d + 64d,
                OutputImageFormat.Gif => originalBytes * scaleRatio * 0.82,
                OutputImageFormat.Tiff => originalBytes * scaleRatio * 1.1,
                OutputImageFormat.Ico => Math.Clamp(targetPixels / 5d, 4096d, 262144d),
                OutputImageFormat.Svg => originalBytes * scaleRatio * 1.2,
                _ => originalBytes
            };

            var variance = options.OutputFormat switch
            {
                OutputImageFormat.Png or OutputImageFormat.Bmp or OutputImageFormat.Svg => 0.28,
                OutputImageFormat.Ico => 0.25,
                _ => 0.18
            };

            var minBytes = Math.Max(512d, baseEstimate * (1d - variance));
            var maxBytes = Math.Max(minBytes, baseEstimate * (1d + variance));
            return (minBytes, maxBytes);
        }

        private static int ResolveQuality(ConversionOptions options)
        {
            return options.CompressionMode switch
            {
                CompressionMode.HighQuality => 92,
                CompressionMode.Balanced => 80,
                CompressionMode.HighCompression => 60,
                _ => Math.Clamp(options.Quality, 1, 100)
            };
        }

        private static (int Width, int Height) CalculateTargetDimensions(int sourceWidth, int sourceHeight, ConversionOptions options)
        {
            if (sourceWidth < 1 || sourceHeight < 1)
            {
                return (1, 1);
            }

            return options.ResizeMode switch
            {
                ResizeMode.FixedWidth =>
                    (Math.Max(1, options.ResizeWidth), Math.Max(1, (int)Math.Round(sourceHeight * (options.ResizeWidth / (double)sourceWidth)))),
                ResizeMode.FixedHeight =>
                    (Math.Max(1, (int)Math.Round(sourceWidth * (options.ResizeHeight / (double)sourceHeight))), Math.Max(1, options.ResizeHeight)),
                ResizeMode.ScalePercent =>
                    (
                        Math.Max(1, (int)Math.Round(sourceWidth * options.ResizePercent / 100d)),
                        Math.Max(1, (int)Math.Round(sourceHeight * options.ResizePercent / 100d))
                    ),
                ResizeMode.CustomSize => (Math.Max(1, options.ResizeWidth), Math.Max(1, options.ResizeHeight)),
                _ => (sourceWidth, sourceHeight)
            };
        }
    }
}



