using Docnet.Core;
using Docnet.Core.Editors;
using ImvixPro.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace ImvixPro.Services.PdfModule
{
    public sealed class PdfExportService
    {
        public static bool IsPdfFile(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        public byte[] ExportPdfDocument(
            string filePath,
            PdfDocumentExportMode exportMode,
            int selectedPageIndex,
            PdfPageRangeSelection rangeSelection,
            int pageCount)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PDF source file was not found.", filePath);
            }

            var maxIndex = Math.Max(0, pageCount - 1);
            var clampedPage = Math.Clamp(selectedPageIndex, 0, maxIndex);
            var clampedRange = ClampRange(rangeSelection, maxIndex);

            return exportMode switch
            {
                PdfDocumentExportMode.CurrentPage => DocLib.Instance.Split(filePath, clampedPage, clampedPage),
                PdfDocumentExportMode.PageRange => DocLib.Instance.Split(filePath, clampedRange.StartIndex, clampedRange.EndIndex),
                _ => File.ReadAllBytes(filePath)
            };
        }

        public byte[] CreatePdfFromJpegs(IReadOnlyList<RenderedJpegPage> pages)
        {
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("No rendered pages are available for PDF creation.");
            }

            var images = new List<JpegImage>(pages.Count);
            foreach (var page in pages)
            {
                images.Add(new JpegImage
                {
                    Bytes = page.Bytes,
                    Width = Math.Max(1, page.Width),
                    Height = Math.Max(1, page.Height)
                });
            }

            return DocLib.Instance.JpegToPdf(images);
        }

        public static PdfPageRangeSelection ClampRange(PdfPageRangeSelection selection, int maxIndex)
        {
            if (maxIndex <= 0)
            {
                return new PdfPageRangeSelection(0, 0);
            }

            var start = Math.Clamp(selection.StartIndex, 0, maxIndex);
            var end = Math.Clamp(selection.EndIndex, start, maxIndex);
            return new PdfPageRangeSelection(start, end);
        }

        public readonly record struct RenderedJpegPage(byte[] Bytes, int Width, int Height);
    }
}
