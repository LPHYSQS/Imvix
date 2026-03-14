
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imvix.Models;
using Imvix.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imvix.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService = new();
        private readonly LocalizationService _localizationService = new();
        private readonly ImageConversionService _imageConversionService = new();

        private string _statusKey = "StatusReady";
        private bool _isLoadingSettings;
        private bool _isSyncingSvgColorInputs;
        private bool _isVersionBadgeHovered;
        private readonly DispatcherTimer _gifPreviewTimer = new();
        private ImageConversionService.GifPreviewHandle? _gifPreviewHandle;
        private IReadOnlyList<Bitmap>? _gifPreviewFrames;
        private IReadOnlyList<TimeSpan>? _gifPreviewDurations;
        private int _gifPreviewIndex;
        private long _gifPreviewRequestId;

        public event EventHandler<ConversionSummary>? ConversionCompleted;

        public MainWindowViewModel()
        {
            Images.CollectionChanged += OnImagesCollectionChanged;
            FailedConversions.CollectionChanged += OnFailedConversionsCollectionChanged;
            Presets.CollectionChanged += OnPresetsCollectionChanged;
            _gifPreviewTimer.Tick += OnGifPreviewTick;

            _isLoadingSettings = true;

            var settings = _settingsService.Load();
            _localizationService.SetLanguage(settings.LanguageCode);
            UiFlowDirection = ResolveFlowDirection(settings.LanguageCode);

            SelectedLanguage = Languages.FirstOrDefault(x => x.Code.Equals(settings.LanguageCode, StringComparison.OrdinalIgnoreCase))
                               ?? Languages[0];
            SelectedTheme = Themes.FirstOrDefault(x => x.Code.Equals(settings.ThemeCode, StringComparison.OrdinalIgnoreCase))
                            ?? Themes[0];
            ApplyTheme(SelectedTheme.Code);

            SelectedOutputFormat = settings.DefaultOutputFormat;
            SelectedCompressionMode = settings.DefaultCompressionMode;
            Quality = Math.Clamp(settings.DefaultQuality, 1, 100);

            SelectedResizeMode = settings.DefaultResizeMode;
            ResizeWidth = Math.Max(1, settings.DefaultResizeWidth);
            ResizeHeight = Math.Max(1, settings.DefaultResizeHeight);
            ResizePercent = Math.Clamp(settings.DefaultResizePercent, 1, 1000);

            SelectedRenameMode = settings.DefaultRenameMode;
            RenamePrefix = settings.DefaultRenamePrefix;
            RenameSuffix = settings.DefaultRenameSuffix;
            RenameStartNumber = Math.Max(0, settings.DefaultRenameStartNumber);
            RenameNumberDigits = Math.Clamp(settings.DefaultRenameNumberDigits, 1, 8);

            OutputDirectory = settings.DefaultOutputDirectory;
            UseSourceFolder = ResolveUseSourceFolder(settings);
            IncludeSubfoldersOnFolderImport = settings.IncludeSubfoldersOnFolderImport;

            AutoOpenOutputDirectory = settings.AutoOpenOutputDirectory;
            AllowOverwrite = settings.AllowOverwrite;
            SvgUseBackground = settings.SvgUseBackground;
            SvgBackgroundColor = string.IsNullOrWhiteSpace(settings.SvgBackgroundColor) ? "#FFFFFFFF" : settings.SvgBackgroundColor;
            SelectedGifHandlingMode = settings.DefaultGifHandlingMode;

            Presets.Clear();
            foreach (var preset in settings.Presets.Where(static p => !string.IsNullOrWhiteSpace(p.Name)))
            {
                Presets.Add(ClonePreset(preset));
            }

            InitializeVersion3Features(settings);
            RefreshEnumOptions();

            RightPanelTabIndex = 0;
            CurrentFile = T("NoCurrentFile");
            RemainingCount = 0;
            ProgressPercent = 0;
            StatusText = T(_statusKey);

            _isLoadingSettings = false;
            RefreshLocalizedProperties();
            _ = CompleteVersion3InitializationAsync();

            OnPropertyChanged(nameof(IsSvgBackgroundColorVisible));
            OnPropertyChanged(nameof(IsQualityEditable));
            OnPropertyChanged(nameof(IsResizeWidthVisible));
            OnPropertyChanged(nameof(IsResizeHeightVisible));
            OnPropertyChanged(nameof(IsResizePercentVisible));
            OnPropertyChanged(nameof(IsRenamePrefixVisible));
            OnPropertyChanged(nameof(IsRenameSuffixVisible));
            OnPropertyChanged(nameof(IsRenameNumberVisible));

            SavePresetCommand.NotifyCanExecuteChanged();
            ApplyPresetCommand.NotifyCanExecuteChanged();
            DeletePresetCommand.NotifyCanExecuteChanged();
        }

        public ObservableCollection<ImageItemViewModel> Images { get; } = [];

        public ObservableCollection<ConversionFailure> FailedConversions { get; } = [];

        public ObservableCollection<ConversionPreset> Presets { get; } = [];

        public IReadOnlyList<OutputImageFormat> OutputFormats { get; } = Enum.GetValues<OutputImageFormat>();

        public ObservableCollection<EnumOption<CompressionMode>> CompressionModes { get; } = [];

        public ObservableCollection<EnumOption<ResizeMode>> ResizeModes { get; } = [];

        public ObservableCollection<EnumOption<RenameMode>> RenameModes { get; } = [];

        public ObservableCollection<EnumOption<GifHandlingMode>> GifHandlingModes { get; } = [];

        public IReadOnlyList<LanguageOption> Languages { get; } =
        [
            new LanguageOption("zh-CN", "\u7B80\u4F53\u4E2D\u6587"),
            new LanguageOption("zh-TW", "\u7E41\u9AD4\u4E2D\u6587"),
            new LanguageOption("en-US", "English"),
            new LanguageOption("ja-JP", "\u65E5\u672C\u8A9E"),
            new LanguageOption("ko-KR", "\uD55C\uAD6D\uC5B4"),
            new LanguageOption("fr-FR", "Fran\u00E7ais"),
            new LanguageOption("de-DE", "Deutsch"),
            new LanguageOption("it-IT", "Italiano"),
            new LanguageOption("ru-RU", "\u0420\u0443\u0441\u0441\u043A\u0438\u0439"),
            new LanguageOption("ar-SA", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629")
        ];

        public IReadOnlyList<ThemeOption> Themes { get; } =
        [
            new ThemeOption("Dark", "Dark / \u6697\u9ED1"),
            new ThemeOption("Light", "Light / \u660E\u4EAE")
        ];

        public bool HasImages => Images.Count > 0;

        public bool IsEmpty => Images.Count == 0;

        public bool HasFailures => FailedConversions.Count > 0;

        public bool HasPresets => Presets.Count > 0;

        public bool IsSvgBackgroundColorVisible => SvgUseBackground;

        public bool IsQualityEditable => SelectedCompressionMode == CompressionMode.Custom;

        public bool IsResizeWidthVisible => SelectedResizeMode is ResizeMode.FixedWidth or ResizeMode.CustomSize;

        public bool IsResizeHeightVisible => SelectedResizeMode is ResizeMode.FixedHeight or ResizeMode.CustomSize;

        public bool IsResizePercentVisible => SelectedResizeMode == ResizeMode.ScalePercent;

        public bool IsRenamePrefixVisible => SelectedRenameMode == RenameMode.Prefix;

        public bool IsRenameSuffixVisible => SelectedRenameMode == RenameMode.Suffix;

        public bool IsRenameNumberVisible => SelectedRenameMode == RenameMode.AutoNumber;

        public bool IsGifPreviewVisible => SelectedImage is not null &&
                                           SelectedImage.Extension.Equals("GIF", StringComparison.OrdinalIgnoreCase) &&
                                           SelectedOutputFormat != OutputImageFormat.Gif;

        public bool IsSvgPreviewVisible => SelectedImage is not null &&
                                           SelectedImage.Extension.Equals("SVG", StringComparison.OrdinalIgnoreCase);

        public string ProgressPercentText => $"{ProgressPercent:0}%";

        public string WindowTitle => "Imvix";

        public string ImportButtonText => T("ImportImages");

        public string ImportFolderButtonText => T("ImportFolder");

        public string ClearButtonText => T("ClearList");

        public string OutputFormatText => T("OutputFormat");

        public string OutputFolderText => T("OutputFolder");

        public string OutputDirectoryHintText => T("OutputDirectoryHint");

        public string ChooseFolderButtonText => T("ChooseFolder");

        public string StartConversionButtonText => T("StartConversion");

        public string SettingsButtonText => T("Settings");

        public string ImageListText => T("ImageList");

        public string DropHintTitleText => T("DropHintTitle");

        public string DropHintDescriptionText => T("DropHintDescription");

        public string PreviewTabText => T("PreviewTab");

        public string SettingsTabText => T("SettingsTab");

        public string NoPreviewText => T("NoPreview");

        public string StatusLabelText => T("StatusLabel");

        public string CurrentFileLabelText => T("CurrentFile");

        public string RemainingLabelText => T("Remaining");

        public string ProgressLabelText => T("Progress");

        public string LanguageLabelText => T("Language");

        public string ThemeLabelText => T("Theme");

        public string DefaultOutputFolderLabelText => T("DefaultOutputFolder");

        public string UseSourceFolderText => T("UseSourceFolder");

        public string IncludeSubfoldersOnImportText => T("IncludeSubfoldersOnImport");

        public string AutoOpenOutputFolderText => T("AutoOpenOutputFolder");

        public string AllowOverwriteText => T("AllowOverwrite");

        public string DefaultOutputFormatText => T("DefaultOutputFormat");

        public string CompressionModeText => T("CompressionMode");

        public string CompressionModeHintText => T("CompressionModeHint");

        public string QualityText => T("Quality");

        public string ResizeSettingsText => T("ResizeSettings");

        public string ResizeModeText => T("ResizeMode");

        public string ResizeWidthText => T("ResizeWidth");

        public string ResizeHeightText => T("ResizeHeight");

        public string ResizePercentText => T("ResizePercent");

        public string RenameSettingsText => T("RenameSettings");

        public string RenameModeText => T("RenameMode");

        public string RenamePrefixText => T("RenamePrefix");

        public string RenameSuffixText => T("RenameSuffix");

        public string RenameStartNumberText => T("RenameStartNumber");

        public string RenameDigitsText => T("RenameDigits");

        public string RenameHintText => T("RenameHint");

        public string GifHandlingText => T("GifHandling");

        public string GifAnimatedLabelText => T("GifAnimatedLabel");

        public string GifFrameCountTemplateText => T("GifFrameCountTemplate");

        public string PresetSettingsText => T("PresetSettings");

        public string PresetNameText => T("PresetName");

        public string PresetNameHintText => T("PresetNameHint");

        public string SavePresetText => T("SavePreset");

        public string ApplyPresetText => T("ApplyPreset");

        public string DeletePresetText => T("DeletePreset");

        public string SvgSettingsText => T("SvgSettings");

        public string SvgUseBackgroundText => T("SvgUseBackground");

        public string SvgBackgroundColorText => T("SvgBackgroundColor");

        public string SvgBackgroundColorRgbText => T("SvgBackgroundColorRgb");

        public string SvgColorPickerText => T("SvgColorPicker");

        public string SvgBackgroundColorHintText => T("SvgBackgroundColorHint");

        public string FailedFilesText => T("FailedFiles");

        public string RemoveText => T("Remove");

        public string ImportDialogTitle => T("ImportDialogTitle");

        public string ImportFolderDialogTitle => T("ImportFolderDialogTitle");

        public string SelectFolderDialogTitle => T("SelectFolderDialogTitle");

        public string ConversionSummaryTitleText => T("ConversionSummaryTitle");

        public string SummaryTotalText => T("SummaryTotal");

        public string SummarySuccessText => T("SummarySuccess");

        public string SummaryFailedText => T("SummaryFailed");

        public string SummaryDurationText => T("SummaryDuration");

        public string CloseText => T("Close");
        public string AboutButtonText => T("AboutButton");
        public string AboutWindowTitleText => T("AboutWindowTitle");
        public string AboutVersionLabelText => T("AboutVersionLabel");
        public string AboutTaglineText => T("AboutTagline");
        public string AboutSummaryText => T("AboutSummary");
        public string AboutFeatureSectionTitleText => T("AboutFeatureSectionTitle");
        public string AboutFeatureSectionBodyText => T("AboutFeatureSectionBody");
        public string AboutTechSectionTitleText => T("AboutTechSectionTitle");
        public string AboutTechSectionBodyText => T("AboutTechSectionBody");
        public string AboutIdeasSectionTitleText => T("AboutIdeasSectionTitle");
        public string AboutIdeasSectionBodyText => T("AboutIdeasSectionBody");
        public string AboutAuthorLabelText => T("AboutAuthorLabel");
        public string AboutLinksLabelText => T("AboutLinksLabel");
        public string AboutWebsiteLabelText => T("AboutWebsiteLabel");
        public string AboutWebsiteButtonText => T("AboutWebsiteButton");
        public string AboutRepositoryLabelText => T("AboutRepositoryLabel");
        public string AboutRepositoryButtonText => T("AboutRepositoryButton");
        public string AboutAuthorNameText => "\u5DF2\u901D\u60C5\u6B87";
        public string AppVersionText => $"v{typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.3.2"}";
        public string VersionBadgeHoverText => T("VersionBadgeHover");
        public string VersionBadgeToolTipText => FormatT("VersionBadgeToolTipFormat", AppVersionText);
        public string FooterVersionBadgeText => _isVersionBadgeHovered ? VersionBadgeHoverText : AppVersionText;
        public double FooterVersionBadgeFontSize => _isVersionBadgeHovered ? 14d : 15d;
        public string VersionNotesWindowTitleText => FormatT("VersionNotesWindowTitleFormat", AppVersionText);
        public string VersionNotesHeaderText => FormatT("VersionNotesHeaderFormat", AppVersionText);
        public string VersionNotesSummaryText => T("VersionNotesSummary");
        public string VersionNotesFixesTitleText => T("VersionNotesFixesTitle");
        public string VersionNotesFixesBodyText => T("VersionNotesFixesBody");
        public string VersionNotesFeaturesTitleText => T("VersionNotesFeaturesTitle");
        public string VersionNotesFeaturesBodyText => T("VersionNotesFeaturesBody");
        public bool IsRightToLeftLayout => UiFlowDirection == FlowDirection.RightToLeft;
        public bool IsLeftToRightLayout => !IsRightToLeftLayout;

        [ObservableProperty]
        private ImageItemViewModel? selectedImage;

        [ObservableProperty]
        private Bitmap? selectedPreview;

        [ObservableProperty]
        private OutputImageFormat selectedOutputFormat;

        [ObservableProperty]
        private CompressionMode selectedCompressionMode = CompressionMode.Custom;

        [ObservableProperty]
        private ResizeMode selectedResizeMode = ResizeMode.None;

        [ObservableProperty]
        private int resizeWidth = 1280;

        [ObservableProperty]
        private int resizeHeight = 720;

        [ObservableProperty]
        private int resizePercent = 100;

        [ObservableProperty]
        private RenameMode selectedRenameMode = RenameMode.KeepOriginal;

        [ObservableProperty]
        private GifHandlingMode selectedGifHandlingMode = GifHandlingMode.FirstFrame;

        [ObservableProperty]
        private EnumOption<CompressionMode>? selectedCompressionModeOption;

        [ObservableProperty]
        private EnumOption<ResizeMode>? selectedResizeModeOption;

        [ObservableProperty]
        private EnumOption<RenameMode>? selectedRenameModeOption;

        [ObservableProperty]
        private EnumOption<GifHandlingMode>? selectedGifHandlingModeOption;

        [ObservableProperty]
        private string renamePrefix = string.Empty;

        [ObservableProperty]
        private string renameSuffix = string.Empty;

        [ObservableProperty]
        private int renameStartNumber = 1;

        [ObservableProperty]
        private int renameNumberDigits = 4;

        [ObservableProperty]
        private string outputDirectory = string.Empty;

        [ObservableProperty]
        private bool useSourceFolder;

        [ObservableProperty]
        private bool includeSubfoldersOnFolderImport = true;

        [ObservableProperty]
        private bool autoOpenOutputDirectory;

        [ObservableProperty]
        private bool allowOverwrite;

        [ObservableProperty]
        private int quality = 90;

        [ObservableProperty]
        private bool svgUseBackground;

        [ObservableProperty]
        private string svgBackgroundColor = "#FFFFFFFF";

        [ObservableProperty]
        private string svgBackgroundColorRgb = "255,255,255";

        [ObservableProperty]
        private Color svgBackgroundColorValue = Color.FromArgb(255, 255, 255, 255);

        [ObservableProperty]
        private LanguageOption? selectedLanguage;

        [ObservableProperty]
        private ThemeOption? selectedTheme;

        [ObservableProperty]
        private FlowDirection uiFlowDirection = FlowDirection.LeftToRight;

        partial void OnUiFlowDirectionChanged(FlowDirection value)
        {
            OnPropertyChanged(nameof(IsRightToLeftLayout));
            OnPropertyChanged(nameof(IsLeftToRightLayout));
        }

        [ObservableProperty]
        private double progressPercent;

        [ObservableProperty]
        private string currentFile = string.Empty;

        [ObservableProperty]
        private int remainingCount;

        [ObservableProperty]
        private string statusText = string.Empty;

        [ObservableProperty]
        private bool isConverting;

        [ObservableProperty]
        private int rightPanelTabIndex;

        [ObservableProperty]
        private string presetNameInput = string.Empty;

        [ObservableProperty]
        private ConversionPreset? selectedPreset;

        public void AddFiles(IEnumerable<string> paths)
        {
            var candidates = ExpandInputPaths(paths, IncludeSubfoldersOnFolderImport)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var path in candidates)
            {
                var extension = Path.GetExtension(path);
                if (!ImageConversionService.SupportedInputExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Images.Any(x => x.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (ImageItemViewModel.TryCreate(path, out var item, out var error) && item is not null)
                {
                    item.Thumbnail ??= ImageConversionService.TryCreatePreview(item.FilePath, 140, svgUseBackground: false, svgBackgroundColor: null);
                    UpdateGifLabels(item);
                    Images.Add(item);
                    WarmGifPreviewIfNeeded(item);
                }
                else
                {
                    FailedConversions.Add(new ConversionFailure(Path.GetFileName(path), error ?? "Unknown error"));
                }
            }

            if (SelectedImage is null && Images.Count > 0)
            {
                SelectedImage = Images[0];
            }

            SetStatus("StatusReady");
            RefreshConversionInsights();
        }

        private static IEnumerable<string> ExpandInputPaths(IEnumerable<string> paths, bool includeSubfolders)
        {
            foreach (var rawPath in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(rawPath);
                }
                catch
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    yield return fullPath;
                    continue;
                }

                if (!Directory.Exists(fullPath))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(
                        fullPath,
                        "*.*",
                        includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file);
                    if (ImageConversionService.SupportedInputExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        yield return file;
                    }
                }
            }
        }

        public void SetOutputDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            OutputDirectory = path;
            UseSourceFolder = false;
        }

        public string BuildConversionSummaryText(ConversionSummary summary)
        {
            var processedLine = summary.WasCanceled
                ? $"\n{T("SummaryProcessed")}: {summary.ProcessedCount}"
                : string.Empty;
            var canceledLine = summary.WasCanceled
                ? $"\n{T("SummaryCanceled")}: {T("YesText")}"
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{SummaryTotalText}: {summary.TotalCount}{processedLine}\n{SummarySuccessText}: {summary.SuccessCount}\n{SummaryFailedText}: {summary.FailureCount}{canceledLine}\n{SummaryDurationText}: {FormatDuration(summary.Duration)}");
        }

        [RelayCommand(CanExecute = nameof(CanClearImages))]
        private void ClearImages()
        {
            SelectedImage = null;

            foreach (var image in Images)
            {
                image.Dispose();
            }

            Images.Clear();
            FailedConversions.Clear();
            LastFailureLogPath = string.Empty;
            SetStatus("StatusReady");
            RefreshConversionInsights();
        }

        [RelayCommand]
        private void RemoveImage(ImageItemViewModel? image)
        {
            if (image is null)
            {
                return;
            }

            var wasSelected = SelectedImage == image;
            if (!Images.Remove(image))
            {
                return;
            }

            image.Dispose();

            if (wasSelected)
            {
                SelectedImage = Images.FirstOrDefault();
            }

            RefreshConversionInsights();
        }

        [RelayCommand(CanExecute = nameof(CanSavePreset))]
        private void SavePreset()
        {
            var name = (PresetNameInput ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var preset = BuildPreset(name);
            var existing = Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Presets.Add(preset);
                SelectedPreset = preset;
            }
            else
            {
                var index = Presets.IndexOf(existing);
                if (index >= 0)
                {
                    Presets[index] = preset;
                    SelectedPreset = preset;
                }
            }

            PersistSettings();
        }

        [RelayCommand(CanExecute = nameof(CanApplyPreset))]
        private void ApplyPreset()
        {
            if (SelectedPreset is null)
            {
                return;
            }

            _isLoadingSettings = true;

            SelectedOutputFormat = SelectedPreset.OutputFormat;
            SelectedCompressionMode = SelectedPreset.CompressionMode;
            Quality = Math.Clamp(SelectedPreset.Quality, 1, 100);
            SelectedResizeMode = SelectedPreset.ResizeMode;
            ResizeWidth = Math.Max(1, SelectedPreset.ResizeWidth);
            ResizeHeight = Math.Max(1, SelectedPreset.ResizeHeight);
            ResizePercent = Math.Clamp(SelectedPreset.ResizePercent, 1, 1000);
            SelectedRenameMode = SelectedPreset.RenameMode;
            RenamePrefix = SelectedPreset.RenamePrefix;
            RenameSuffix = SelectedPreset.RenameSuffix;
            RenameStartNumber = Math.Max(0, SelectedPreset.RenameStartNumber);
            RenameNumberDigits = Math.Clamp(SelectedPreset.RenameNumberDigits, 1, 8);
            SelectedGifHandlingMode = SelectedPreset.GifHandlingMode;
            OutputDirectory = SelectedPreset.OutputDirectory;
            UseSourceFolder = SelectedPreset.OutputDirectoryRule == OutputDirectoryRule.SourceFolder;
            AllowOverwrite = SelectedPreset.AllowOverwrite;
            SvgUseBackground = SelectedPreset.SvgUseBackground;
            SvgBackgroundColor = string.IsNullOrWhiteSpace(SelectedPreset.SvgBackgroundColor)
                ? "#FFFFFFFF"
                : SelectedPreset.SvgBackgroundColor;

            _isLoadingSettings = false;

            OnPropertyChanged(nameof(IsQualityEditable));
            OnPropertyChanged(nameof(IsResizeWidthVisible));
            OnPropertyChanged(nameof(IsResizeHeightVisible));
            OnPropertyChanged(nameof(IsResizePercentVisible));
            OnPropertyChanged(nameof(IsRenamePrefixVisible));
            OnPropertyChanged(nameof(IsRenameSuffixVisible));
            OnPropertyChanged(nameof(IsRenameNumberVisible));

            PersistSettings();
        }

        [RelayCommand(CanExecute = nameof(CanDeletePreset))]
        private void DeletePreset()
        {
            if (SelectedPreset is null)
            {
                return;
            }

            var target = SelectedPreset;
            SelectedPreset = null;
            Presets.Remove(target);
            PersistSettings();
        }

                [RelayCommand(CanExecute = nameof(CanStartConversion))]
        private async Task StartConversionAsync()
        {
            await StartManualConversionCoreAsync();
        }

        [RelayCommand]
        private void OpenSettingsPanel()
        {
            RightPanelTabIndex = 1;
        }

        partial void OnSelectedImageChanged(ImageItemViewModel? value)
        {
            ClearSelectedPreview();

            if (value is not null)
            {
                SelectedPreview = ImageConversionService.TryCreatePreview(value.FilePath, 760, SvgUseBackground, EffectiveSvgBackgroundColor);
                if (value.IsAnimatedGif)
                {
                    if (ShouldAnimateGifPreview())
                    {
                        _ = LoadGifPreviewAsync(value.FilePath);
                    }
                    else
                    {
                        Interlocked.Increment(ref _gifPreviewRequestId);
                    }
                }
            }

            OnPropertyChanged(nameof(IsGifPreviewVisible));
            OnPropertyChanged(nameof(IsSvgPreviewVisible));

            RefreshConversionInsights();
        }

        partial void OnSelectedLanguageChanged(LanguageOption? value)
        {
            if (value is null)
            {
                return;
            }

            _localizationService.SetLanguage(value.Code);
            UiFlowDirection = ResolveFlowDirection(value.Code);
            RefreshLocalizedProperties();
            RefreshEnumOptions();
            StatusText = T(_statusKey);
            CurrentFile = T("NoCurrentFile");
            PersistSettings();
        }

        partial void OnSelectedThemeChanged(ThemeOption? value)
        {
            if (value is null)
            {
                return;
            }

            ApplyTheme(value.Code);
            PersistSettings();
        }

        partial void OnSelectedOutputFormatChanged(OutputImageFormat value)
        {
            PersistSettings();
            OnPropertyChanged(nameof(IsGifPreviewVisible));
            RefreshSelectedAnimatedGifPreview();
        }

        partial void OnSelectedCompressionModeChanged(CompressionMode value)
        {
            var option = CompressionModes.FirstOrDefault(x => EqualityComparer<CompressionMode>.Default.Equals(x.Value, value));
            if (SelectedCompressionModeOption != option)
            {
                SelectedCompressionModeOption = option;
            }

            OnPropertyChanged(nameof(IsQualityEditable));
            PersistSettings();
        }

        partial void OnSelectedCompressionModeOptionChanged(EnumOption<CompressionMode>? value)
        {
            if (value is null)
            {
                return;
            }

            if (!EqualityComparer<CompressionMode>.Default.Equals(SelectedCompressionMode, value.Value))
            {
                SelectedCompressionMode = value.Value;
            }
        }

        partial void OnSelectedResizeModeChanged(ResizeMode value)
        {
            var option = ResizeModes.FirstOrDefault(x => EqualityComparer<ResizeMode>.Default.Equals(x.Value, value));
            if (SelectedResizeModeOption != option)
            {
                SelectedResizeModeOption = option;
            }

            OnPropertyChanged(nameof(IsResizeWidthVisible));
            OnPropertyChanged(nameof(IsResizeHeightVisible));
            OnPropertyChanged(nameof(IsResizePercentVisible));
            PersistSettings();
        }

        partial void OnSelectedResizeModeOptionChanged(EnumOption<ResizeMode>? value)
        {
            if (value is null)
            {
                return;
            }

            if (!EqualityComparer<ResizeMode>.Default.Equals(SelectedResizeMode, value.Value))
            {
                SelectedResizeMode = value.Value;
            }
        }

        partial void OnResizeWidthChanged(int value)
        {
            if (value < 1)
            {
                ResizeWidth = 1;
                return;
            }

            PersistSettings();
        }

        partial void OnResizeHeightChanged(int value)
        {
            if (value < 1)
            {
                ResizeHeight = 1;
                return;
            }

            PersistSettings();
        }

        partial void OnResizePercentChanged(int value)
        {
            if (value < 1)
            {
                ResizePercent = 1;
                return;
            }

            if (value > 1000)
            {
                ResizePercent = 1000;
                return;
            }

            PersistSettings();
        }

        partial void OnSelectedRenameModeChanged(RenameMode value)
        {
            var option = RenameModes.FirstOrDefault(x => EqualityComparer<RenameMode>.Default.Equals(x.Value, value));
            if (SelectedRenameModeOption != option)
            {
                SelectedRenameModeOption = option;
            }

            OnPropertyChanged(nameof(IsRenamePrefixVisible));
            OnPropertyChanged(nameof(IsRenameSuffixVisible));
            OnPropertyChanged(nameof(IsRenameNumberVisible));
            PersistSettings();
        }

        partial void OnSelectedRenameModeOptionChanged(EnumOption<RenameMode>? value)
        {
            if (value is null)
            {
                return;
            }

            if (!EqualityComparer<RenameMode>.Default.Equals(SelectedRenameMode, value.Value))
            {
                SelectedRenameMode = value.Value;
            }
        }

        partial void OnSelectedGifHandlingModeChanged(GifHandlingMode value)
        {
            var option = GifHandlingModes.FirstOrDefault(x => EqualityComparer<GifHandlingMode>.Default.Equals(x.Value, value));
            if (SelectedGifHandlingModeOption != option)
            {
                SelectedGifHandlingModeOption = option;
            }

            PersistSettings();

            if (SelectedImage is null || !SelectedImage.IsAnimatedGif)
            {
                return;
            }

            if (value == GifHandlingMode.AllFrames)
            {
                WarmAllGifPreviewsIfNeeded();
            }

            RefreshSelectedAnimatedGifPreview();
        }

        partial void OnSelectedGifHandlingModeOptionChanged(EnumOption<GifHandlingMode>? value)
        {
            if (value is null)
            {
                return;
            }

            if (!EqualityComparer<GifHandlingMode>.Default.Equals(SelectedGifHandlingMode, value.Value))
            {
                SelectedGifHandlingMode = value.Value;
            }
        }

        partial void OnRenamePrefixChanged(string value)
        {
            PersistSettings();
        }

        partial void OnRenameSuffixChanged(string value)
        {
            PersistSettings();
        }

        partial void OnRenameStartNumberChanged(int value)
        {
            if (value < 0)
            {
                RenameStartNumber = 0;
                return;
            }

            PersistSettings();
        }

        partial void OnRenameNumberDigitsChanged(int value)
        {
            if (value < 1)
            {
                RenameNumberDigits = 1;
                return;
            }

            if (value > 8)
            {
                RenameNumberDigits = 8;
                return;
            }

            PersistSettings();
        }

        partial void OnOutputDirectoryChanged(string value)
        {
            PersistSettings();
        }

        partial void OnUseSourceFolderChanged(bool value)
        {
            PersistSettings();
        }

        partial void OnIncludeSubfoldersOnFolderImportChanged(bool value)
        {
            PersistSettings();
        }

        partial void OnAutoOpenOutputDirectoryChanged(bool value)
        {
            PersistSettings();
        }

        partial void OnAllowOverwriteChanged(bool value)
        {
            PersistSettings();
        }

        partial void OnQualityChanged(int value)
        {
            if (value < 1)
            {
                Quality = 1;
                return;
            }

            if (value > 100)
            {
                Quality = 100;
                return;
            }

            PersistSettings();
        }

        partial void OnSvgUseBackgroundChanged(bool value)
        {
            OnPropertyChanged(nameof(IsSvgBackgroundColorVisible));
            RefreshSelectedPreviewIfSvg();
            PersistSettings();
        }

        partial void OnSvgBackgroundColorChanged(string value)
        {
            if (_isSyncingSvgColorInputs)
            {
                return;
            }

            if (!TryParseSvgColor(value, out var parsed))
            {
                UpdateSvgColorInputs(SvgBackgroundColorValue);
                return;
            }

            UpdateSvgColorInputs(parsed);
            RefreshSelectedPreviewIfSvg();
            PersistSettings();
        }

        partial void OnSvgBackgroundColorRgbChanged(string value)
        {
            if (_isSyncingSvgColorInputs)
            {
                return;
            }

            if (!TryParseRgbColor(value, out var parsed))
            {
                UpdateSvgColorInputs(SvgBackgroundColorValue);
                return;
            }

            UpdateSvgColorInputs(parsed);
            RefreshSelectedPreviewIfSvg();
            PersistSettings();
        }

        partial void OnSvgBackgroundColorValueChanged(Color value)
        {
            if (_isSyncingSvgColorInputs)
            {
                return;
            }

            UpdateSvgColorInputs(value);
            RefreshSelectedPreviewIfSvg();
            PersistSettings();
        }

        partial void OnProgressPercentChanged(double value)
        {
            OnPropertyChanged(nameof(ProgressPercentText));
        }

        partial void OnIsConvertingChanged(bool value)
        {
            StartConversionCommand.NotifyCanExecuteChanged();
            ClearImagesCommand.NotifyCanExecuteChanged();
            PauseConversionCommand.NotifyCanExecuteChanged();
            ResumeConversionCommand.NotifyCanExecuteChanged();
            CancelConversionCommand.NotifyCanExecuteChanged();

            if (!value)
            {
                RefreshConversionInsights();
            }
        }

        partial void OnPresetNameInputChanged(string value)
        {
            SavePresetCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedPresetChanged(ConversionPreset? value)
        {
            ApplyPresetCommand.NotifyCanExecuteChanged();
            DeletePresetCommand.NotifyCanExecuteChanged();

            if (value is not null)
            {
                PresetNameInput = value.Name;
            }
        }

        private bool CanClearImages()
        {
            return Images.Count > 0 && !IsConverting;
        }

        private bool CanStartConversion()
        {
            return Images.Count > 0 && !IsConverting;
        }

        private bool CanSavePreset()
        {
            return !string.IsNullOrWhiteSpace(PresetNameInput);
        }

        private bool CanApplyPreset()
        {
            return SelectedPreset is not null;
        }

        private bool CanDeletePreset()
        {
            return SelectedPreset is not null;
        }

        private void OnImagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasImages));
            OnPropertyChanged(nameof(IsEmpty));
            StartConversionCommand.NotifyCanExecuteChanged();
            ClearImagesCommand.NotifyCanExecuteChanged();
        }

        private void OnFailedConversionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasFailures));
        }

        private void OnPresetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasPresets));
            ApplyPresetCommand.NotifyCanExecuteChanged();
            DeletePresetCommand.NotifyCanExecuteChanged();
        }

        private void PersistSettings()
        {
            if (_isLoadingSettings)
            {
                return;
            }

            var existing = _settingsService.Load();

            _settingsService.Save(new AppSettings
            {
                LanguageCode = SelectedLanguage?.Code ?? "zh-CN",
                ThemeCode = SelectedTheme?.Code ?? "Dark",
                DefaultOutputFormat = SelectedOutputFormat,
                DefaultCompressionMode = SelectedCompressionMode,
                DefaultQuality = Quality,
                DefaultResizeMode = SelectedResizeMode,
                DefaultResizeWidth = ResizeWidth,
                DefaultResizeHeight = ResizeHeight,
                DefaultResizePercent = ResizePercent,
                DefaultRenameMode = SelectedRenameMode,
                DefaultRenamePrefix = RenamePrefix,
                DefaultRenameSuffix = RenameSuffix,
                DefaultRenameStartNumber = RenameStartNumber,
                DefaultRenameNumberDigits = RenameNumberDigits,
                DefaultGifHandlingMode = SelectedGifHandlingMode,
                DefaultOutputDirectory = OutputDirectory,
                UseSourceFolderByDefault = UseSourceFolder,
                HasOutputDirectoryRule = true,
                OutputDirectoryRule = UseSourceFolder ? OutputDirectoryRule.SourceFolder : OutputDirectoryRule.SpecificFolder,
                IncludeSubfoldersOnFolderImport = IncludeSubfoldersOnFolderImport,
                AutoOpenOutputDirectory = AutoOpenOutputDirectory,
                AllowOverwrite = AllowOverwrite,
                SvgUseBackground = SvgUseBackground,
                SvgBackgroundColor = EffectiveSvgBackgroundColor,
                MaxParallelism = _maxParallelism,
                Presets = Presets.Select(ClonePreset).ToList(),
                WatchModeEnabled = WatchModeEnabled,
                WatchInputDirectory = WatchInputDirectory,
                WatchOutputDirectory = WatchOutputDirectory,
                WatchIncludeSubfolders = WatchIncludeSubfolders,
                KeepRunningInTray = KeepRunningInTray,
                HasWindowPlacement = existing.HasWindowPlacement,
                WindowPositionX = existing.WindowPositionX,
                WindowPositionY = existing.WindowPositionY,
                WindowWidth = existing.WindowWidth,
                WindowHeight = existing.WindowHeight
            });

            RefreshConversionInsights();
        }

        private static bool ResolveUseSourceFolder(AppSettings settings)
        {
            if (settings.HasOutputDirectoryRule)
            {
                return settings.OutputDirectoryRule == OutputDirectoryRule.SourceFolder;
            }

            return settings.UseSourceFolderByDefault;
        }

        private ConversionPreset BuildPreset(string name)
        {
            return new ConversionPreset
            {
                Name = name,
                OutputFormat = SelectedOutputFormat,
                CompressionMode = SelectedCompressionMode,
                Quality = Quality,
                ResizeMode = SelectedResizeMode,
                ResizeWidth = ResizeWidth,
                ResizeHeight = ResizeHeight,
                ResizePercent = ResizePercent,
                RenameMode = SelectedRenameMode,
                RenamePrefix = RenamePrefix,
                RenameSuffix = RenameSuffix,
                RenameStartNumber = RenameStartNumber,
                RenameNumberDigits = RenameNumberDigits,
                GifHandlingMode = SelectedGifHandlingMode,
                OutputDirectoryRule = UseSourceFolder ? OutputDirectoryRule.SourceFolder : OutputDirectoryRule.SpecificFolder,
                OutputDirectory = OutputDirectory,
                AllowOverwrite = AllowOverwrite,
                SvgUseBackground = SvgUseBackground,
                SvgBackgroundColor = EffectiveSvgBackgroundColor
            };
        }

        private static ConversionPreset ClonePreset(ConversionPreset source)
        {
            return new ConversionPreset
            {
                Name = source.Name,
                OutputFormat = source.OutputFormat,
                CompressionMode = source.CompressionMode,
                Quality = source.Quality,
                ResizeMode = source.ResizeMode,
                ResizeWidth = source.ResizeWidth,
                ResizeHeight = source.ResizeHeight,
                ResizePercent = source.ResizePercent,
                RenameMode = source.RenameMode,
                RenamePrefix = source.RenamePrefix,
                RenameSuffix = source.RenameSuffix,
                RenameStartNumber = source.RenameStartNumber,
                RenameNumberDigits = source.RenameNumberDigits,
                GifHandlingMode = source.GifHandlingMode,
                OutputDirectoryRule = source.OutputDirectoryRule,
                OutputDirectory = source.OutputDirectory,
                AllowOverwrite = source.AllowOverwrite,
                SvgUseBackground = source.SvgUseBackground,
                SvgBackgroundColor = source.SvgBackgroundColor
            };
        }

        private void RefreshEnumOptions()
        {
            RebuildEnumOptions(
                CompressionModes,
                Enum.GetValues<CompressionMode>(),
                mode => T($"CompressionMode_{mode}"));

            RebuildEnumOptions(
                ResizeModes,
                Enum.GetValues<ResizeMode>(),
                mode => T($"ResizeMode_{mode}"));

            RebuildEnumOptions(
                RenameModes,
                Enum.GetValues<RenameMode>(),
                mode => T($"RenameMode_{mode}"));

            RebuildEnumOptions(
                GifHandlingModes,
                Enum.GetValues<GifHandlingMode>(),
                mode => T($"GifHandling_{mode}"));

            SelectedCompressionModeOption = CompressionModes.FirstOrDefault(x => EqualityComparer<CompressionMode>.Default.Equals(x.Value, SelectedCompressionMode));
            SelectedResizeModeOption = ResizeModes.FirstOrDefault(x => EqualityComparer<ResizeMode>.Default.Equals(x.Value, SelectedResizeMode));
            SelectedRenameModeOption = RenameModes.FirstOrDefault(x => EqualityComparer<RenameMode>.Default.Equals(x.Value, SelectedRenameMode));
            SelectedGifHandlingModeOption = GifHandlingModes.FirstOrDefault(x => EqualityComparer<GifHandlingMode>.Default.Equals(x.Value, SelectedGifHandlingMode));
        }

        private static void RebuildEnumOptions<T>(
            ObservableCollection<EnumOption<T>> options,
            IReadOnlyList<T> values,
            Func<T, string> textFactory)
            where T : struct
        {
            options.Clear();
            foreach (var value in values)
            {
                options.Add(new EnumOption<T>(value, textFactory(value)));
            }
        }
        private void SetStatus(string key)
        {
            _statusKey = key;
            StatusText = T(key);
        }

        public void SetVersionBadgeHover(bool isHovered)
        {
            if (_isVersionBadgeHovered == isHovered)
            {
                return;
            }

            _isVersionBadgeHovered = isHovered;
            OnPropertyChanged(nameof(FooterVersionBadgeText));
            OnPropertyChanged(nameof(FooterVersionBadgeFontSize));
        }

        private string T(string key)
        {
            return _localizationService.Translate(key);
        }

        private string FormatT(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }

        private static FlowDirection ResolveFlowDirection(string? languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return FlowDirection.LeftToRight;
            }

            return languageCode.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }

        private void ApplyTheme(string themeCode)
        {
            if (Application.Current is null)
            {
                return;
            }

            Application.Current.RequestedThemeVariant = themeCode.Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
        }

        private void RefreshGifLabels()
        {
            foreach (var image in Images)
            {
                UpdateGifLabels(image);
            }
        }

        private void UpdateGifLabels(ImageItemViewModel image)
        {
            if (!image.IsAnimatedGif)
            {
                image.GifBadgeText = string.Empty;
                image.GifFrameCountText = string.Empty;
                return;
            }

            image.GifBadgeText = GifAnimatedLabelText;
            image.GifFrameCountText = string.Format(CultureInfo.CurrentCulture, GifFrameCountTemplateText, image.GifFrameCount);
        }

        private void WarmGifPreviewIfNeeded(ImageItemViewModel image)
        {
            if (!image.IsAnimatedGif || SelectedGifHandlingMode != GifHandlingMode.AllFrames)
            {
                return;
            }

            ImageConversionService.WarmGifPreview(image.FilePath, 760);
        }

        private void WarmAllGifPreviewsIfNeeded()
        {
            if (SelectedGifHandlingMode != GifHandlingMode.AllFrames)
            {
                return;
            }

            foreach (var image in Images)
            {
                if (image.IsAnimatedGif)
                {
                    ImageConversionService.WarmGifPreview(image.FilePath, 760);
                }
            }
        }

        private bool ShouldAnimateGifPreview()
        {
            return SelectedOutputFormat == OutputImageFormat.Gif || SelectedGifHandlingMode == GifHandlingMode.AllFrames;
        }

        private void RefreshSelectedAnimatedGifPreview()
        {
            if (SelectedImage is null || !SelectedImage.IsAnimatedGif)
            {
                return;
            }

            ClearSelectedPreview();
            SelectedPreview = ImageConversionService.TryCreatePreview(SelectedImage.FilePath, 760, SvgUseBackground, EffectiveSvgBackgroundColor);

            if (ShouldAnimateGifPreview())
            {
                _ = LoadGifPreviewAsync(SelectedImage.FilePath);
            }
            else
            {
                Interlocked.Increment(ref _gifPreviewRequestId);
            }
        }

        private async Task LoadGifPreviewAsync(string filePath)
        {
            var requestId = Interlocked.Increment(ref _gifPreviewRequestId);

            var handle = await ImageConversionService.GetOrLoadGifPreviewAsync(filePath, 760);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestId != _gifPreviewRequestId ||
                    SelectedImage is null ||
                    !SelectedImage.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    handle?.Dispose();
                    return;
                }

                if (handle is null || handle.Frames.Count == 0 || handle.Frames.Count != handle.Durations.Count)
                {
                    handle?.Dispose();
                    return;
                }

                StartGifPreview(handle);
            });
        }

        private void StartGifPreview(ImageConversionService.GifPreviewHandle handle)
        {
            StopGifPreview();

            var frames = handle.Frames;
            var durations = handle.Durations;
            if (frames.Count == 0 || frames.Count != durations.Count)
            {
                handle.Dispose();
                return;
            }

            _gifPreviewHandle = handle;
            _gifPreviewFrames = frames;
            _gifPreviewDurations = durations;
            _gifPreviewIndex = 0;
            SelectedPreview?.Dispose();
            SelectedPreview = frames[0];
            _gifPreviewTimer.Interval = ClampGifDuration(durations[0]);
            _gifPreviewTimer.Start();
        }

        private void StopGifPreview()
        {
            if (_gifPreviewTimer.IsEnabled)
            {
                _gifPreviewTimer.Stop();
            }

            _gifPreviewHandle?.Dispose();
            _gifPreviewHandle = null;
            _gifPreviewFrames = null;
            _gifPreviewDurations = null;
            _gifPreviewIndex = 0;
        }

        private void ClearSelectedPreview()
        {
            if (_gifPreviewFrames is not null)
            {
                StopGifPreview();
                SelectedPreview = null;
                return;
            }

            SelectedPreview?.Dispose();
            SelectedPreview = null;
        }

        private void OnGifPreviewTick(object? sender, EventArgs e)
        {
            if (_gifPreviewFrames is null || _gifPreviewDurations is null || _gifPreviewFrames.Count == 0)
            {
                StopGifPreview();
                SelectedPreview = null;
                return;
            }

            _gifPreviewIndex = (_gifPreviewIndex + 1) % _gifPreviewFrames.Count;
            SelectedPreview = _gifPreviewFrames[_gifPreviewIndex];

            if (_gifPreviewIndex < _gifPreviewDurations.Count)
            {
                _gifPreviewTimer.Interval = ClampGifDuration(_gifPreviewDurations[_gifPreviewIndex]);
            }
        }

        private static TimeSpan ClampGifDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero || duration.TotalMilliseconds <= 20)
            {
                return TimeSpan.FromMilliseconds(100);
            }

            return duration;
        }

        private void RefreshSelectedPreviewIfSvg()
        {
            if (SelectedImage is null)
            {
                return;
            }

            if (!Path.GetExtension(SelectedImage.FilePath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedPreview?.Dispose();
            SelectedPreview = ImageConversionService.TryCreatePreview(SelectedImage.FilePath, 760, SvgUseBackground, EffectiveSvgBackgroundColor);
        }

        private void RefreshLocalizedProperties()
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(ImportButtonText));
            OnPropertyChanged(nameof(ImportFolderButtonText));
            OnPropertyChanged(nameof(ClearButtonText));
            OnPropertyChanged(nameof(OutputFormatText));
            OnPropertyChanged(nameof(OutputFolderText));
            OnPropertyChanged(nameof(OutputDirectoryHintText));
            OnPropertyChanged(nameof(ChooseFolderButtonText));
            OnPropertyChanged(nameof(StartConversionButtonText));
            OnPropertyChanged(nameof(SettingsButtonText));
            OnPropertyChanged(nameof(ImageListText));
            OnPropertyChanged(nameof(DropHintTitleText));
            OnPropertyChanged(nameof(DropHintDescriptionText));
            OnPropertyChanged(nameof(PreviewTabText));
            OnPropertyChanged(nameof(SettingsTabText));
            OnPropertyChanged(nameof(NoPreviewText));
            OnPropertyChanged(nameof(StatusLabelText));
            OnPropertyChanged(nameof(CurrentFileLabelText));
            OnPropertyChanged(nameof(RemainingLabelText));
            OnPropertyChanged(nameof(ProgressLabelText));
            OnPropertyChanged(nameof(LanguageLabelText));
            OnPropertyChanged(nameof(ThemeLabelText));
            OnPropertyChanged(nameof(DefaultOutputFolderLabelText));
            OnPropertyChanged(nameof(UseSourceFolderText));
            OnPropertyChanged(nameof(IncludeSubfoldersOnImportText));
            OnPropertyChanged(nameof(AutoOpenOutputFolderText));
            OnPropertyChanged(nameof(AllowOverwriteText));
            OnPropertyChanged(nameof(DefaultOutputFormatText));
            OnPropertyChanged(nameof(CompressionModeText));
            OnPropertyChanged(nameof(CompressionModeHintText));
            OnPropertyChanged(nameof(QualityText));
            OnPropertyChanged(nameof(ResizeSettingsText));
            OnPropertyChanged(nameof(ResizeModeText));
            OnPropertyChanged(nameof(ResizeWidthText));
            OnPropertyChanged(nameof(ResizeHeightText));
            OnPropertyChanged(nameof(ResizePercentText));
            OnPropertyChanged(nameof(RenameSettingsText));
            OnPropertyChanged(nameof(RenameModeText));
            OnPropertyChanged(nameof(RenamePrefixText));
            OnPropertyChanged(nameof(RenameSuffixText));
            OnPropertyChanged(nameof(RenameStartNumberText));
            OnPropertyChanged(nameof(RenameDigitsText));
            OnPropertyChanged(nameof(RenameHintText));
            OnPropertyChanged(nameof(GifHandlingText));
            OnPropertyChanged(nameof(GifAnimatedLabelText));
            OnPropertyChanged(nameof(GifFrameCountTemplateText));
            OnPropertyChanged(nameof(PresetSettingsText));
            OnPropertyChanged(nameof(PresetNameText));
            OnPropertyChanged(nameof(PresetNameHintText));
            OnPropertyChanged(nameof(SavePresetText));
            OnPropertyChanged(nameof(ApplyPresetText));
            OnPropertyChanged(nameof(DeletePresetText));
            OnPropertyChanged(nameof(SvgSettingsText));
            OnPropertyChanged(nameof(SvgUseBackgroundText));
            OnPropertyChanged(nameof(SvgBackgroundColorText));
            OnPropertyChanged(nameof(SvgBackgroundColorRgbText));
            OnPropertyChanged(nameof(SvgColorPickerText));
            OnPropertyChanged(nameof(SvgBackgroundColorHintText));
            OnPropertyChanged(nameof(FailedFilesText));
            OnPropertyChanged(nameof(RemoveText));
            OnPropertyChanged(nameof(ImportDialogTitle));
            OnPropertyChanged(nameof(ImportFolderDialogTitle));
            OnPropertyChanged(nameof(SelectFolderDialogTitle));
            OnPropertyChanged(nameof(ConversionSummaryTitleText));
            OnPropertyChanged(nameof(SummaryTotalText));
            OnPropertyChanged(nameof(SummarySuccessText));
            OnPropertyChanged(nameof(SummaryFailedText));
            OnPropertyChanged(nameof(SummaryDurationText));
            OnPropertyChanged(nameof(CloseText));
            OnPropertyChanged(nameof(AboutButtonText));
            OnPropertyChanged(nameof(AboutWindowTitleText));
            OnPropertyChanged(nameof(AboutVersionLabelText));
            OnPropertyChanged(nameof(AboutTaglineText));
            OnPropertyChanged(nameof(AboutSummaryText));
            OnPropertyChanged(nameof(AboutFeatureSectionTitleText));
            OnPropertyChanged(nameof(AboutFeatureSectionBodyText));
            OnPropertyChanged(nameof(AboutTechSectionTitleText));
            OnPropertyChanged(nameof(AboutTechSectionBodyText));
            OnPropertyChanged(nameof(AboutIdeasSectionTitleText));
            OnPropertyChanged(nameof(AboutIdeasSectionBodyText));
            OnPropertyChanged(nameof(AboutAuthorLabelText));
            OnPropertyChanged(nameof(AboutLinksLabelText));
            OnPropertyChanged(nameof(AboutWebsiteLabelText));
            OnPropertyChanged(nameof(AboutWebsiteButtonText));
            OnPropertyChanged(nameof(AboutRepositoryLabelText));
            OnPropertyChanged(nameof(AboutRepositoryButtonText));
            OnPropertyChanged(nameof(AboutAuthorNameText));
            OnPropertyChanged(nameof(AppVersionText));
            OnPropertyChanged(nameof(VersionBadgeHoverText));
            OnPropertyChanged(nameof(VersionBadgeToolTipText));
            OnPropertyChanged(nameof(FooterVersionBadgeText));
            OnPropertyChanged(nameof(VersionNotesWindowTitleText));
            OnPropertyChanged(nameof(VersionNotesHeaderText));
            OnPropertyChanged(nameof(VersionNotesSummaryText));
            OnPropertyChanged(nameof(VersionNotesFixesTitleText));
            OnPropertyChanged(nameof(VersionNotesFixesBodyText));
            OnPropertyChanged(nameof(VersionNotesFeaturesTitleText));
            OnPropertyChanged(nameof(VersionNotesFeaturesBodyText));
            RefreshLocalizedPropertiesV3();
            RefreshGifLabels();
        }

        private string EffectiveSvgBackgroundColor => ToHexColor(SvgBackgroundColorValue);

        private void UpdateSvgColorInputs(Color color)
        {
            _isSyncingSvgColorInputs = true;
            SvgBackgroundColor = ToHexColor(color);
            SvgBackgroundColorRgb = ToRgbColorText(color);
            SvgBackgroundColorValue = color;
            _isSyncingSvgColorInputs = false;
        }

        private static bool TryParseSvgColor(string value, out Color color)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                color = default;
                return false;
            }

            return Color.TryParse(value.Trim(), out color);
        }

        private static bool TryParseRgbColor(string value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
            {
                text = text[4..^1];
            }

            var parts = text.Split([',', '\uFF0C'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                return false;
            }

            if (!byte.TryParse(parts[0], out var r) ||
                !byte.TryParse(parts[1], out var g) ||
                !byte.TryParse(parts[2], out var b))
            {
                return false;
            }

            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        private static string ToHexColor(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static string ToRgbColorText(Color color)
        {
            return $"{color.R},{color.G},{color.B}";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds / 10:00}");
        }

        private void TryOpenOutputFolder(IReadOnlyList<string> outputFolders, bool hasOutput)
        {
            if (!hasOutput || !AutoOpenOutputDirectory)
            {
                return;
            }

            var folders = outputFolders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (folders.Count == 0)
            {
                return;
            }

            var folderToOpen = folders[0];
            if (!Directory.Exists(folderToOpen))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderToOpen,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore shell-open failures.
            }
        }
    }
}

















