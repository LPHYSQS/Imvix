using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace ImvixPro.Models
{
    public sealed partial class ImageItemViewModel : ObservableObject, IDisposable
    {
        private ImageItemViewModel(string filePath, long fileSize, int pixelWidth, int pixelHeight, Bitmap? thumbnail, int gifFrameCount, int pdfPageCount)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            Extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            FileSizeBytes = Math.Max(0, fileSize);
            SizeText = FormatSize(FileSizeBytes);
            PixelWidth = Math.Max(0, pixelWidth);
            PixelHeight = Math.Max(0, pixelHeight);
            GifFrameCount = Math.Max(1, gifFrameCount);
            IsAnimatedGif = GifFrameCount > 1;
            PdfPageCount = Math.Max(0, pdfPageCount);
            IsPdfDocument = PdfPageCount > 0;
            ResolutionText = PixelWidth > 0 && PixelHeight > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{PixelWidth} x {PixelHeight}")
                : "-";
            Thumbnail = thumbnail;
        }

        public string FilePath { get; }

        public string FileName { get; }

        public string Extension { get; }

        public long FileSizeBytes { get; }

        public string SizeText { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public long PixelCount => (long)PixelWidth * PixelHeight;

        public int GifFrameCount { get; }

        public bool IsAnimatedGif { get; }

        public int PdfPageCount { get; }

        public bool IsPdfDocument { get; }

        public string ResolutionText { get; }

        [ObservableProperty]
        private bool isMarked;

        [ObservableProperty]
        private Bitmap? thumbnail;

        [ObservableProperty]
        private string gifBadgeText = string.Empty;

        [ObservableProperty]
        private string gifFrameCountText = string.Empty;

        public static bool TryCreate(string filePath, out ImageItemViewModel? item, out string? error, bool generateThumbnail = true)
        {
            item = null;
            error = null;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    error = "File not found.";
                    return false;
                }

                Bitmap? thumbnail = null;
                if (generateThumbnail)
                {
                    try
                    {
                        using var stream = File.OpenRead(filePath);
                        thumbnail = Bitmap.DecodeToWidth(stream, 140);
                    }
                    catch
                    {
                        try
                        {
                            using var fallback = TryDecodeWithSystemDrawing(filePath);
                            if (fallback is not null)
                            {
                                using var image = SKImage.FromBitmap(fallback);
                                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                if (data is not null)
                                {
                                    using var memory = new MemoryStream(data.ToArray());
                                    thumbnail = Bitmap.DecodeToWidth(memory, 140);
                                }
                            }
                        }
                        catch
                        {
                            // Keep import usable when thumbnail generation fails.
                        }
                    }
                }

                _ = TryReadImageInfo(filePath, out var width, out var height, out var frameCount);
                var gifFrameCount = Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase)
                    ? frameCount
                    : 1;

                item = CreateImported(filePath, fileInfo.Length, width, height, thumbnail, gifFrameCount);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            Thumbnail?.Dispose();
        }

        internal static ImageItemViewModel CreateImported(
            string filePath,
            long fileSize,
            int pixelWidth,
            int pixelHeight,
            Bitmap? thumbnail,
            int gifFrameCount,
            int pdfPageCount = 0)
        {
            return new ImageItemViewModel(filePath, fileSize, pixelWidth, pixelHeight, thumbnail, gifFrameCount, pdfPageCount);
        }

        private static bool TryReadImageInfo(string filePath, out int width, out int height, out int frameCount)
        {
            width = 0;
            height = 0;
            frameCount = 1;

            try
            {
                if (Path.GetExtension(filePath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svg = new SKSvg();
                    var picture = svg.Load(filePath);
                    if (picture is null)
                    {
                        return false;
                    }

                    var bounds = picture.CullRect;
                    width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
                    height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
                    frameCount = 1;
                    return true;
                }

                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);
                if (codec is null)
                {
                    return TryReadImageInfoWithSystemDrawing(filePath, out width, out height, out frameCount);
                }

                width = Math.Max(0, codec.Info.Width);
                height = Math.Max(0, codec.Info.Height);
                frameCount = Math.Max(1, codec.FrameCount);
                return width > 0 && height > 0;
            }
            catch
            {
                return TryReadImageInfoWithSystemDrawing(filePath, out width, out height, out frameCount);
            }
        }

        private static bool TryReadImageInfoWithSystemDrawing(string filePath, out int width, out int height, out int frameCount)
        {
            width = 0;
            height = 0;
            frameCount = 1;

            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);

                width = Math.Max(1, image.Width);
                height = Math.Max(1, image.Height);

                if (Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
                    image.FrameDimensionsList.Length > 0)
                {
                    var dimension = new FrameDimension(image.FrameDimensionsList[0]);
                    frameCount = Math.Max(1, image.GetFrameCount(dimension));
                }

                return true;
            }
            catch
            {
                width = 0;
                height = 0;
                frameCount = 1;
                return false;
            }
        }

        private static SKBitmap? TryDecodeWithSystemDrawing(string filePath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                using var memory = new MemoryStream();
                image.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                return SKBitmap.Decode(memory);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatSize(long size)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;

            if (size < kb)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{size} B");
            }

            if (size < mb)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{size / kb:0.0} KB");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{size / mb:0.0} MB");
        }
    }
}
