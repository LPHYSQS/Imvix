using Avalonia.Media.Imaging;
using ImvixPro.Models;
using System;
using System.IO;

namespace ImvixPro.Services.PdfModule
{
    public sealed class PdfImportService
    {
        private readonly PdfRenderService _pdfRenderService = new();

        public static bool IsPdfFile(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryCreate(string filePath, out ImageItemViewModel? item, out string? error, bool generateThumbnail = true)
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

                if (!_pdfRenderService.TryReadDocumentInfo(filePath, out var info, out error))
                {
                    return false;
                }

                Bitmap? thumbnail = null;
                if (generateThumbnail)
                {
                    try
                    {
                        thumbnail = _pdfRenderService.TryCreatePreview(filePath, 0, 140);
                    }
                    catch
                    {
                        // Keep import usable when thumbnail generation fails.
                    }
                }

                item = ImageItemViewModel.CreateImported(
                    filePath,
                    fileInfo.Length,
                    info.FirstPageWidth,
                    info.FirstPageHeight,
                    thumbnail,
                    gifFrameCount: 1,
                    pdfPageCount: info.PageCount);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
