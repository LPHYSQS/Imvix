namespace Imvix.Models
{
    public sealed class ConversionPreset
    {
        public string Name { get; set; } = string.Empty;

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
    }
}