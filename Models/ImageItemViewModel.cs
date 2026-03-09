using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Globalization;
using System.IO;

namespace Imvix.Models
{
    public sealed partial class ImageItemViewModel : ObservableObject, IDisposable
    {
        private ImageItemViewModel(string filePath, long fileSize, int pixelWidth, int pixelHeight, Bitmap? thumbnail)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            Extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            FileSizeBytes = Math.Max(0, fileSize);
            SizeText = FormatSize(FileSizeBytes);
            PixelWidth = Math.Max(0, pixelWidth);
            PixelHeight = Math.Max(0, pixelHeight);
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

        public string ResolutionText { get; }

        [ObservableProperty]
        private bool isMarked;

        [ObservableProperty]
        private Bitmap? thumbnail;

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
                        // Keep import usable when thumbnail generation fails.
                    }
                }

                _ = TryReadImageSize(filePath, out var width, out var height);

                item = new ImageItemViewModel(filePath, fileInfo.Length, width, height, thumbnail);
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

        private static bool TryReadImageSize(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;

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
                    return true;
                }

                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);
                if (codec is null)
                {
                    return false;
                }

                width = Math.Max(0, codec.Info.Width);
                height = Math.Max(0, codec.Info.Height);
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
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
