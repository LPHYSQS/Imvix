using System;
using System.Collections.Generic;

namespace ImvixPro.Models
{
    public sealed class ConversionOptions
    {
        public OutputImageFormat OutputFormat { get; set; } = OutputImageFormat.Png;

        public CompressionMode CompressionMode { get; set; } = CompressionMode.Custom;

        public int Quality { get; set; } = 90;

        public ResizeMode ResizeMode { get; set; } = ResizeMode.None;

        public int ResizeWidth { get; set; } = 1280;

        public int ResizeHeight { get; set; } = 720;

        public int ResizePercent { get; set; } = 100;

        public RenameMode RenameMode { get; set; } = RenameMode.KeepOriginal;

        public string RenamePrefix { get; set; } = string.Empty;

        public string RenameSuffix { get; set; } = string.Empty;

        public int RenameStartNumber { get; set; } = 1;

        public int RenameNumberDigits { get; set; } = 4;

        public OutputDirectoryRule OutputDirectoryRule { get; set; } = OutputDirectoryRule.SourceFolder;

        public string OutputDirectory { get; set; } = string.Empty;

        public string AutoResultFolderName { get; set; } = "Imvix_Output";

        public bool AllowOverwrite { get; set; }

        public bool SvgUseBackground { get; set; }

        public string SvgBackgroundColor { get; set; } = "#FFFFFFFF";

        public GifHandlingMode GifHandlingMode { get; set; } = GifHandlingMode.FirstFrame;

        public int GifSpecificFrameIndex { get; set; }

        public Dictionary<string, int> GifSpecificFrameSelections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, GifFrameRangeSelection> GifFrameRanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public PdfImageExportMode PdfImageExportMode { get; set; } = PdfImageExportMode.AllPages;

        public PdfDocumentExportMode PdfDocumentExportMode { get; set; } = PdfDocumentExportMode.AllPages;

        public int PdfPageIndex { get; set; }

        public Dictionary<string, int> PdfPageSelections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PdfPageRangeSelection> PdfPageRanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);
    }
}
