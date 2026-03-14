﻿using System;
using System.Collections.Generic;

namespace Imvix.Models
{
    public sealed class AppSettings
    {
        public string LanguageCode { get; set; } = "zh-CN";

        public string ThemeCode { get; set; } = "Dark";

        public OutputImageFormat DefaultOutputFormat { get; set; } = OutputImageFormat.Png;

        public CompressionMode DefaultCompressionMode { get; set; } = CompressionMode.Custom;

        public int DefaultQuality { get; set; } = 90;

        public ResizeMode DefaultResizeMode { get; set; } = ResizeMode.None;

        public int DefaultResizeWidth { get; set; } = 1280;

        public int DefaultResizeHeight { get; set; } = 720;

        public int DefaultResizePercent { get; set; } = 100;

        public RenameMode DefaultRenameMode { get; set; } = RenameMode.KeepOriginal;

        public string DefaultRenamePrefix { get; set; } = string.Empty;

        public string DefaultRenameSuffix { get; set; } = string.Empty;

        public int DefaultRenameStartNumber { get; set; } = 1;

        public int DefaultRenameNumberDigits { get; set; } = 4;

        public string DefaultOutputDirectory { get; set; } = string.Empty;

        public bool UseSourceFolderByDefault { get; set; } = true;

        public bool HasOutputDirectoryRule { get; set; }

        public OutputDirectoryRule OutputDirectoryRule { get; set; } = OutputDirectoryRule.SourceFolder;

        public string AutoResultFolderName { get; set; } = "Imvix_Output";

        public bool IncludeSubfoldersOnFolderImport { get; set; } = true;

        public ListSortMode DefaultListSortMode { get; set; } = ListSortMode.NameAscending;

        public int MaxParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);

        public bool AutoOpenOutputDirectory { get; set; } = true;

        public bool AllowOverwrite { get; set; }

        public bool SvgUseBackground { get; set; }

        public string SvgBackgroundColor { get; set; } = "#FFFFFFFF";

        public GifHandlingMode DefaultGifHandlingMode { get; set; } = GifHandlingMode.FirstFrame;

        public List<ConversionPreset> Presets { get; set; } = [];

        public bool WatchModeEnabled { get; set; }

        public string WatchInputDirectory { get; set; } = string.Empty;

        public string WatchOutputDirectory { get; set; } = string.Empty;

        public bool WatchIncludeSubfolders { get; set; } = true;

        public bool KeepRunningInTray { get; set; }

        public bool HasWindowPlacement { get; set; }

        public int WindowPositionX { get; set; }

        public int WindowPositionY { get; set; }

        public double WindowWidth { get; set; }

        public double WindowHeight { get; set; }
    }
}
