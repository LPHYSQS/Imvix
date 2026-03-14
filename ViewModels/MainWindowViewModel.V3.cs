﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imvix.Models;
using Imvix.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imvix.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly ImageAnalysisService _imageAnalysisService = new();
        private readonly ConversionHistoryService _conversionHistoryService = new();
        private readonly ConversionLogService _conversionLogService = new();
        private readonly FolderWatchService _folderWatchService = new();
        private readonly ConversionPauseController _manualPauseController = new();
        private readonly SemaphoreSlim _watchConfigurationGate = new(1, 1);
        private readonly SemaphoreSlim _watchProcessingGate = new(1, 1);
        private readonly List<ConversionHistoryEntry> _historyCache = [];

        private CancellationTokenSource? _manualConversionCancellationSource;
        private CancellationTokenSource? _watchProcessingCancellationSource;
        private int _maxParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4);

        public Func<string, string, string, string, Task<bool>>? ConfirmDialogAsync { get; set; }

        public ObservableCollection<string> ActiveWarnings { get; } = [];

        public ObservableCollection<RecentConversionItem> RecentConversions { get; } = [];

        public bool HasActiveWarnings => ActiveWarnings.Count > 0;

        public bool HasRecentConversions => RecentConversions.Count > 0;

        public bool IsHistoryEmpty => !HasRecentConversions;

        public bool HasFormatRecommendation => !string.IsNullOrWhiteSpace(FormatRecommendationText);

        public bool HasSizeEstimate => !string.IsNullOrWhiteSpace(OriginalSizeSummaryText) || !string.IsNullOrWhiteSpace(EstimatedSizeSummaryText);

        public bool HasFailureLog => !string.IsNullOrWhiteSpace(LastFailureLogPath);

        public bool IsWatchModeRunning => _folderWatchService.IsRunning;

        public string FormatRecommendationTitleText => T("FormatRecommendationTitle");
        public string EstimatedSizeTitleText => T("EstimatedSizeTitle");
        public string WarningsTitleText => T("WarningsTitle");
        public string HistoryTitleText => T("HistoryTitle");
        public string HistoryEmptyText => T("HistoryEmpty");
        public string WatchModeTitleText => T("WatchModeTitle");
        public string WatchModeEnabledText => T("WatchModeEnabled");
        public string WatchInputFolderText => T("WatchInputFolder");
        public string WatchOutputFolderText => T("WatchOutputFolder");
        public string WatchStatusLabelText => T("WatchStatusLabel");
        public string WatchIncludeSubfoldersText => T("WatchIncludeSubfolders");
        public string WatchHintText => T("WatchHint");
        public string SelectWatchInputFolderDialogTitle => T("SelectWatchInputFolderDialogTitle");
        public string SelectWatchOutputFolderDialogTitle => T("SelectWatchOutputFolderDialogTitle");
        public string PauseText => T("Pause");
        public string ResumeText => T("Resume");
        public string CancelTaskText => T("CancelTask");
        public string FailureLogLabelText => T("FailureLogLabel");
        public string ContinueActionText => T("ContinueAction");
        public string CancelActionText => T("CancelAction");
        public string WatchMetricsText => string.Format(CultureInfo.CurrentCulture, T("WatchMetricsTemplate"), WatchProcessedCount, WatchFailureCount);
        public string ClearRecentConversionsText => T("ClearRecentConversions");
        public string TraySettingsTitleText => T("TraySettingsTitle");
        public string KeepRunningInTrayText => T("KeepRunningInTray");
        public string TrayHintText => T("TrayHint");
        public string TrayRestoreText => T("TrayRestore");
        public string TrayExitText => T("TrayExit");

        [ObservableProperty]
        private string formatRecommendationText = string.Empty;

        [ObservableProperty]
        private string formatRecommendationReasonText = string.Empty;

        [ObservableProperty]
        private string originalSizeSummaryText = string.Empty;

        [ObservableProperty]
        private string estimatedSizeSummaryText = string.Empty;

        [ObservableProperty]
        private bool isConversionPaused;

        [ObservableProperty]
        private string lastFailureLogPath = string.Empty;

        [ObservableProperty]
        private bool watchModeEnabled;

        [ObservableProperty]
        private string watchInputDirectory = string.Empty;

        [ObservableProperty]
        private string watchOutputDirectory = string.Empty;

        [ObservableProperty]
        private bool watchIncludeSubfolders = true;

        [ObservableProperty]
        private bool keepRunningInTray;

        [ObservableProperty]
        private string watchStatusText = string.Empty;

        [ObservableProperty]
        private int watchProcessedCount;

        [ObservableProperty]
        private int watchFailureCount;

        [ObservableProperty]
        private string watchCurrentFile = string.Empty;

        [ObservableProperty]
        private bool isWatchProcessing;

        private void InitializeVersion3Features(AppSettings settings)
        {
            ActiveWarnings.CollectionChanged += OnActiveWarningsCollectionChanged;
            RecentConversions.CollectionChanged += OnRecentConversionsCollectionChanged;
            _folderWatchService.FileReady += OnWatchedFileReady;

            _maxParallelism = Math.Clamp(Math.Max(1, settings.MaxParallelism), 1, 4);
            WatchModeEnabled = settings.WatchModeEnabled;
            WatchInputDirectory = settings.WatchInputDirectory;
            WatchOutputDirectory = settings.WatchOutputDirectory;
            WatchIncludeSubfolders = settings.WatchIncludeSubfolders;
            KeepRunningInTray = settings.KeepRunningInTray;
            WatchStatusText = T("WatchStatusStopped");

            LoadRecentConversionHistory();
            RefreshConversionInsights();
        }

        public void LoadRecentConversionHistory()
        {
            ReplaceHistory(_conversionHistoryService.Load());
        }

        private async Task CompleteVersion3InitializationAsync()
        {
            RefreshWatchStatus();
            RefreshConversionInsights();

            if (WatchModeEnabled)
            {
                await ReconfigureWatchModeAsync();
            }
        }

        private async Task StartManualConversionCoreAsync()
        {
            if (Images.Count == 0)
            {
                return;
            }

            var warnings = await BuildPreflightWarningsAsync();
            if (warnings.Count > 0 && ConfirmDialogAsync is not null)
            {
                var proceed = await ConfirmDialogAsync(
                    T("ConfirmConversionTitle"),
                    string.Join(Environment.NewLine + Environment.NewLine, warnings),
                    ContinueActionText,
                    CancelActionText);

                if (!proceed)
                {
                    SetStatus("StatusReady");
                    return;
                }
            }

            _manualConversionCancellationSource?.Cancel();
            _manualConversionCancellationSource?.Dispose();
            _manualConversionCancellationSource = new CancellationTokenSource();
            _manualPauseController.Resume();
            IsConversionPaused = false;
            IsConverting = true;
            LastFailureLogPath = string.Empty;
            FailedConversions.Clear();
            ProgressPercent = 0;
            RemainingCount = Images.Count;
            CurrentFile = T("NoCurrentFile");
            SetStatus("StatusConverting");

            var snapshot = Images.ToList();
            var options = BuildCurrentConversionOptions();
            var estimate = _imageAnalysisService.Estimate(snapshot, options);

            try
            {
                var progress = new Progress<ConversionProgress>(p =>
                {
                    CurrentFile = p.FileName;
                    RemainingCount = Math.Max(0, p.TotalCount - p.ProcessedCount);
                    ProgressPercent = p.TotalCount == 0 ? 0 : 100d * p.ProcessedCount / p.TotalCount;
                });

                var summary = await _imageConversionService.ConvertAsync(
                    snapshot,
                    options,
                    progress,
                    _manualPauseController,
                    _manualConversionCancellationSource.Token);

                foreach (var failure in summary.Failures)
                {
                    FailedConversions.Add(failure);
                }

                CurrentFile = summary.WasCanceled
                    ? string.Format(CultureInfo.InvariantCulture, T("TaskSummaryCanceledInlineTemplate"), summary.ProcessedCount, summary.SuccessCount, summary.FailureCount)
                    : string.Format(CultureInfo.InvariantCulture, T("TaskSummaryInlineTemplate"), summary.SuccessCount, summary.FailureCount, FormatDuration(summary.Duration));

                RemainingCount = Math.Max(0, summary.TotalCount - summary.ProcessedCount);

                if (summary.WasCanceled)
                {
                    SetStatus("StatusCanceled");
                }
                else if (summary.FailureCount > 0)
                {
                    SetStatus("StatusCompletedWithFailures");
                }
                else
                {
                    SetStatus("StatusCompleted");
                }

                var logPath = _conversionLogService.WriteFailureLog(summary, options, snapshot, ConversionTriggerSource.Manual);
                LastFailureLogPath = logPath ?? string.Empty;
                AppendHistory(summary, options, estimate, logPath, ConversionTriggerSource.Manual);

                ConversionCompleted?.Invoke(this, summary);
                TryOpenOutputFolder(summary.OutputDirectories, summary.SuccessCount > 0);
            }
            catch (OperationCanceledException)
            {
                SetStatus("StatusCanceled");
            }
            catch (Exception ex)
            {
                FailedConversions.Add(new ConversionFailure("System", ex.Message));
                SetStatus("StatusUnexpectedError");
            }
            finally
            {
                _manualPauseController.Resume();
                _manualConversionCancellationSource?.Dispose();
                _manualConversionCancellationSource = null;
                IsConversionPaused = false;
                IsConverting = false;
                RefreshConversionInsights();
            }
        }

        private ConversionOptions BuildCurrentConversionOptions(bool forWatch = false)
        {
            return new ConversionOptions
            {
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
                OutputDirectoryRule = forWatch
                    ? OutputDirectoryRule.SpecificFolder
                    : UseSourceFolder ? OutputDirectoryRule.SourceFolder : OutputDirectoryRule.SpecificFolder,
                OutputDirectory = forWatch ? WatchOutputDirectory : OutputDirectory,
                AllowOverwrite = AllowOverwrite,
                SvgUseBackground = SvgUseBackground,
                SvgBackgroundColor = EffectiveSvgBackgroundColor,
                GifHandlingMode = SelectedGifHandlingMode,
                MaxDegreeOfParallelism = _maxParallelism
            };
        }

        private async Task<List<string>> BuildPreflightWarningsAsync()
        {
            var warnings = new List<string>();

            if (SelectedOutputFormat == OutputImageFormat.Jpeg)
            {
                var hasTransparency = await Task.Run(() => _imageAnalysisService.ContainsTransparency(Images.ToList()));
                if (hasTransparency)
                {
                    warnings.Add(T("WarningTransparencyLoss"));
                }
            }

            if (IsHighCompressionSelection())
            {
                warnings.Add(T("WarningHighCompression"));
            }

            return warnings.Distinct(StringComparer.Ordinal).ToList();
        }

        private void RefreshConversionInsights()
        {
            if (SelectedImage is null)
            {
                FormatRecommendationText = string.Empty;
                FormatRecommendationReasonText = string.Empty;
            }
            else
            {
                var analysis = _imageAnalysisService.Analyze(SelectedImage);
                var recommendedFormats = BuildRecommendationFormatsText(analysis);
                FormatRecommendationText = string.Format(CultureInfo.CurrentCulture, T("RecommendationFormatsTemplate"), recommendedFormats);
                FormatRecommendationReasonText = T(GetRecommendationReasonKey(analysis.ContentKind));
            }

            var estimate = _imageAnalysisService.Estimate(Images.ToList(), BuildCurrentConversionOptions());
            if (!estimate.IsAvailable)
            {
                OriginalSizeSummaryText = string.Empty;
                EstimatedSizeSummaryText = string.Empty;
            }
            else
            {
                OriginalSizeSummaryText = string.Format(CultureInfo.CurrentCulture, T("OriginalSizeTemplate"), FormatBytes(estimate.OriginalTotalBytes));
                EstimatedSizeSummaryText = string.Format(CultureInfo.CurrentCulture, T("EstimatedSizeTemplate"), FormatBytesRange(estimate.EstimatedMinBytes, estimate.EstimatedMaxBytes));
            }

            RefreshActiveWarnings();
            OnPropertyChanged(nameof(HasFormatRecommendation));
            OnPropertyChanged(nameof(HasSizeEstimate));
        }

        private void RefreshActiveWarnings()
        {
            ActiveWarnings.Clear();

            if (SelectedImage is not null &&
                SelectedOutputFormat == OutputImageFormat.Jpeg &&
                _imageAnalysisService.HasTransparency(SelectedImage))
            {
                ActiveWarnings.Add(T("WarningTransparencyLoss"));
            }

            if (IsHighCompressionSelection())
            {
                ActiveWarnings.Add(T("WarningHighCompression"));
            }
        }

        private bool IsHighCompressionSelection()
        {
            return SelectedOutputFormat is OutputImageFormat.Jpeg or OutputImageFormat.Webp &&
                   (SelectedCompressionMode == CompressionMode.HighCompression ||
                    (SelectedCompressionMode == CompressionMode.Custom && Quality <= 45));
        }

        private string BuildRecommendationFormatsText(ImageAnalysisResult analysis)
        {
            var primary = FormatOutputFormat(analysis.PrimaryRecommendation);
            if (analysis.SecondaryRecommendation is null)
            {
                return primary;
            }

            return $"{primary} / {FormatOutputFormat(analysis.SecondaryRecommendation.Value)}";
        }

        private static string FormatOutputFormat(OutputImageFormat format)
        {
            return format switch
            {
                OutputImageFormat.Jpeg => "JPEG",
                OutputImageFormat.Webp => "WEBP",
                OutputImageFormat.Png => "PNG",
                OutputImageFormat.Bmp => "BMP",
                OutputImageFormat.Gif => "GIF",
                OutputImageFormat.Tiff => "TIFF",
                OutputImageFormat.Ico => "ICO",
                OutputImageFormat.Svg => "SVG",
                _ => format.ToString().ToUpperInvariant()
            };
        }

        private static string GetRecommendationReasonKey(ImageContentKind kind)
        {
            return kind switch
            {
                ImageContentKind.Photo => "RecommendationReasonPhoto",
                ImageContentKind.TransparentGraphic => "RecommendationReasonTransparent",
                ImageContentKind.Icon => "RecommendationReasonIcon",
                ImageContentKind.Vector => "RecommendationReasonVector",
                _ => "RecommendationReasonGeneric"
            };
        }

        private void ReplaceHistory(IReadOnlyList<ConversionHistoryEntry> entries)
        {
            _historyCache.Clear();
            _historyCache.AddRange(entries.OrderByDescending(entry => entry.Timestamp));

            RecentConversions.Clear();
            foreach (var entry in _historyCache)
            {
                RecentConversions.Add(BuildHistoryItem(entry));
            }
        }

        private RecentConversionItem BuildHistoryItem(ConversionHistoryEntry entry)
        {
            var timestampText = entry.Timestamp.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
            var sourceText = entry.Source == ConversionTriggerSource.Manual ? T("HistorySourceManual") : T("HistorySourceWatch");
            var formatText = FormatOutputFormat(entry.OutputFormat);
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, entry.DurationMilliseconds));
            var summaryText = entry.WasCanceled
                ? string.Format(CultureInfo.CurrentCulture, T("HistorySummaryCanceledTemplate"), sourceText, formatText, entry.ProcessedCount, entry.TotalCount)
                : string.Format(CultureInfo.CurrentCulture, T("HistorySummaryTemplate"), sourceText, formatText, entry.TotalCount, entry.SuccessCount, entry.FailureCount);
            var detailText = string.Format(CultureInfo.CurrentCulture, T("HistoryDetailTemplate"), FormatBytes(entry.OriginalTotalBytes), FormatBytesRange(entry.EstimatedMinBytes, entry.EstimatedMaxBytes), FormatDuration(duration));

            return new RecentConversionItem
            {
                TimestampText = timestampText,
                SummaryText = summaryText,
                DetailText = detailText,
                FailureLogPath = entry.FailureLogPath
            };
        }

        private void AppendHistory(
            ConversionSummary summary,
            ConversionOptions options,
            SizeEstimateResult estimate,
            string? failureLogPath,
            ConversionTriggerSource source)
        {
            var updated = _conversionHistoryService.Append(new ConversionHistoryEntry
            {
                Timestamp = DateTimeOffset.Now,
                Source = source,
                OutputFormat = options.OutputFormat,
                TotalCount = summary.TotalCount,
                ProcessedCount = summary.ProcessedCount,
                SuccessCount = summary.SuccessCount,
                FailureCount = summary.FailureCount,
                OriginalTotalBytes = estimate.OriginalTotalBytes,
                EstimatedMinBytes = estimate.EstimatedMinBytes,
                EstimatedMaxBytes = estimate.EstimatedMaxBytes,
                DurationMilliseconds = summary.Duration.TotalMilliseconds,
                WasCanceled = summary.WasCanceled,
                FailureLogPath = failureLogPath ?? string.Empty
            });

            ReplaceHistory(updated);
        }

        private void RefreshLocalizedPropertiesV3()
        {
            OnPropertyChanged(nameof(FormatRecommendationTitleText));
            OnPropertyChanged(nameof(EstimatedSizeTitleText));
            OnPropertyChanged(nameof(WarningsTitleText));
            OnPropertyChanged(nameof(HistoryTitleText));
            OnPropertyChanged(nameof(HistoryEmptyText));
            OnPropertyChanged(nameof(ClearRecentConversionsText));
            OnPropertyChanged(nameof(WatchModeTitleText));
            OnPropertyChanged(nameof(TraySettingsTitleText));
            OnPropertyChanged(nameof(KeepRunningInTrayText));
            OnPropertyChanged(nameof(TrayHintText));
            OnPropertyChanged(nameof(TrayRestoreText));
            OnPropertyChanged(nameof(TrayExitText));
            OnPropertyChanged(nameof(WatchModeEnabledText));
            OnPropertyChanged(nameof(WatchInputFolderText));
            OnPropertyChanged(nameof(WatchOutputFolderText));
            OnPropertyChanged(nameof(WatchStatusLabelText));
            OnPropertyChanged(nameof(WatchIncludeSubfoldersText));
            OnPropertyChanged(nameof(WatchHintText));
            OnPropertyChanged(nameof(SelectWatchInputFolderDialogTitle));
            OnPropertyChanged(nameof(SelectWatchOutputFolderDialogTitle));
            OnPropertyChanged(nameof(PauseText));
            OnPropertyChanged(nameof(ResumeText));
            OnPropertyChanged(nameof(CancelTaskText));
            OnPropertyChanged(nameof(FailureLogLabelText));
            OnPropertyChanged(nameof(ContinueActionText));
            OnPropertyChanged(nameof(CancelActionText));
            OnPropertyChanged(nameof(WatchMetricsText));
            RefreshConversionInsights();
            ReplaceHistory(_historyCache);
            RefreshWatchStatus();
        }

        private void RefreshWatchStatus()
        {
            if (!WatchModeEnabled)
            {
                WatchStatusText = T("WatchStatusStopped");
                OnPropertyChanged(nameof(IsWatchModeRunning));
                return;
            }

            if (IsWatchProcessing && !string.IsNullOrWhiteSpace(WatchCurrentFile))
            {
                WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusProcessing"), WatchCurrentFile);
                OnPropertyChanged(nameof(IsWatchModeRunning));
                return;
            }

            if (_folderWatchService.IsRunning)
            {
                WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusRunning"), WatchInputDirectory);
                OnPropertyChanged(nameof(IsWatchModeRunning));
                return;
            }

            if (!TryValidateWatchConfiguration(out var validationMessage))
            {
                WatchStatusText = validationMessage;
            }
            else
            {
                WatchStatusText = T("WatchStatusWaiting");
            }

            OnPropertyChanged(nameof(IsWatchModeRunning));
        }

        private async Task ReconfigureWatchModeAsync()
        {
            await _watchConfigurationGate.WaitAsync();
            try
            {
                _folderWatchService.Stop();
                _watchProcessingCancellationSource?.Cancel();
                _watchProcessingCancellationSource?.Dispose();
                _watchProcessingCancellationSource = null;

                if (!WatchModeEnabled)
                {
                    RefreshWatchStatus();
                    return;
                }

                if (!TryValidateWatchConfiguration(out var validationMessage))
                {
                    WatchStatusText = validationMessage;
                    OnPropertyChanged(nameof(IsWatchModeRunning));
                    return;
                }

                Directory.CreateDirectory(WatchOutputDirectory);
                _watchProcessingCancellationSource = new CancellationTokenSource();
                _folderWatchService.Start(WatchInputDirectory, WatchIncludeSubfolders);
                RefreshWatchStatus();
            }
            catch (Exception ex)
            {
                WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusErrorTemplate"), ex.Message);
                OnPropertyChanged(nameof(IsWatchModeRunning));
            }
            finally
            {
                _watchConfigurationGate.Release();
            }
        }

        private bool TryValidateWatchConfiguration(out string message)
        {
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(WatchInputDirectory) || !Directory.Exists(WatchInputDirectory))
            {
                message = T("WatchStatusInvalidInput");
                return false;
            }

            if (string.IsNullOrWhiteSpace(WatchOutputDirectory))
            {
                message = T("WatchStatusInvalidOutput");
                return false;
            }

            if (PathsOverlap(WatchInputDirectory, WatchOutputDirectory))
            {
                message = T("WatchStatusOverlap");
                return false;
            }

            return true;
        }

        private static bool PathsOverlap(string firstPath, string secondPath)
        {
            var first = EnsureTrailingSeparator(Path.GetFullPath(firstPath));
            var second = EnsureTrailingSeparator(Path.GetFullPath(secondPath));
            return first.StartsWith(second, StringComparison.OrdinalIgnoreCase) || second.StartsWith(first, StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private void OnWatchedFileReady(object? sender, string path)
        {
            _ = ProcessWatchedFileAsync(path);
        }

        private async Task ProcessWatchedFileAsync(string path)
        {
            var cancellation = _watchProcessingCancellationSource;
            if (cancellation is null || !WatchModeEnabled)
            {
                return;
            }

            await _watchProcessingGate.WaitAsync();
            ImageItemViewModel? item = null;

            try
            {
                cancellation.Token.ThrowIfCancellationRequested();

                while (IsConverting)
                {
                    await Task.Delay(350, cancellation.Token);
                }

                if (!ImageItemViewModel.TryCreate(path, out item, out var error, generateThumbnail: false) || item is null)
                {
                    WatchFailureCount++;
                    WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusSingleFailureTemplate"), Path.GetFileName(path), error ?? T("UnknownReason"));
                    return;
                }

                IsWatchProcessing = true;
                WatchCurrentFile = item.FileName;
                RefreshWatchStatus();

                var snapshot = new List<ImageItemViewModel> { item };
                var options = BuildCurrentConversionOptions(forWatch: true);
                var estimate = _imageAnalysisService.Estimate(snapshot, options);
                var progress = new Progress<ConversionProgress>(p =>
                {
                    WatchCurrentFile = p.FileName;
                    RefreshWatchStatus();
                });

                var summary = await _imageConversionService.ConvertAsync(snapshot, options, progress, pauseController: null, cancellationToken: cancellation.Token);

                var logPath = _conversionLogService.WriteFailureLog(summary, options, snapshot, ConversionTriggerSource.Watch);
                AppendHistory(summary, options, estimate, logPath, ConversionTriggerSource.Watch);

                if (summary.FailureCount > 0)
                {
                    WatchFailureCount += summary.FailureCount;
                    WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusSingleFailureTemplate"), item.FileName, summary.Failures[0].Reason);
                }
                else
                {
                    WatchProcessedCount += summary.SuccessCount;
                    WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusProcessedTemplate"), item.FileName);
                }
            }
            catch (OperationCanceledException)
            {
                RefreshWatchStatus();
            }
            catch (Exception ex)
            {
                WatchFailureCount++;
                WatchStatusText = string.Format(CultureInfo.CurrentCulture, T("WatchStatusErrorTemplate"), ex.Message);
            }
            finally
            {
                item?.Dispose();
                IsWatchProcessing = false;
                WatchCurrentFile = string.Empty;
                _watchProcessingGate.Release();
                OnPropertyChanged(nameof(WatchMetricsText));

                if (!WatchModeEnabled || cancellation.IsCancellationRequested)
                {
                    RefreshWatchStatus();
                }
                else
                {
                    OnPropertyChanged(nameof(IsWatchModeRunning));
                }
            }
        }

        public void SetWatchInputDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                WatchInputDirectory = path;
            }
        }

        public void SetWatchOutputDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                WatchOutputDirectory = path;
            }
        }

        [RelayCommand(CanExecute = nameof(CanClearRecentConversions))]
        private void ClearRecentConversions()
        {
            ReplaceHistory(_conversionHistoryService.Clear());
        }

        [RelayCommand(CanExecute = nameof(CanPauseConversion))]
        private void PauseConversion()
        {
            _manualPauseController.Pause();
            IsConversionPaused = true;
            SetStatus("StatusPaused");
        }

        [RelayCommand(CanExecute = nameof(CanResumeConversion))]
        private void ResumeConversion()
        {
            _manualPauseController.Resume();
            IsConversionPaused = false;
            SetStatus("StatusConverting");
        }

        [RelayCommand(CanExecute = nameof(CanCancelConversion))]
        private void CancelConversion()
        {
            _manualConversionCancellationSource?.Cancel();
        }

        private bool CanClearRecentConversions()
        {
            return RecentConversions.Count > 0;
        }

        private bool CanPauseConversion()
        {
            return IsConverting && !IsConversionPaused;
        }

        private bool CanResumeConversion()
        {
            return IsConverting && IsConversionPaused;
        }

        private bool CanCancelConversion()
        {
            return IsConverting;
        }

        partial void OnIsConversionPausedChanged(bool value)
        {
            PauseConversionCommand.NotifyCanExecuteChanged();
            ResumeConversionCommand.NotifyCanExecuteChanged();
        }

        partial void OnLastFailureLogPathChanged(string value)
        {
            OnPropertyChanged(nameof(HasFailureLog));
        }

        partial void OnWatchModeEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(IsWatchModeRunning));
            if (_isLoadingSettings)
            {
                return;
            }

            _ = ReconfigureWatchModeAsync();
            PersistSettings();
        }

        partial void OnWatchInputDirectoryChanged(string value)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            _ = ReconfigureWatchModeAsync();
            PersistSettings();
        }

        partial void OnWatchOutputDirectoryChanged(string value)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            _ = ReconfigureWatchModeAsync();
            PersistSettings();
        }

        partial void OnWatchIncludeSubfoldersChanged(bool value)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            _ = ReconfigureWatchModeAsync();
            PersistSettings();
        }

        partial void OnKeepRunningInTrayChanged(bool value)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            PersistSettings();
        }

        partial void OnWatchProcessedCountChanged(int value)
        {
            OnPropertyChanged(nameof(WatchMetricsText));
        }

        partial void OnWatchFailureCountChanged(int value)
        {
            OnPropertyChanged(nameof(WatchMetricsText));
        }

        private void OnActiveWarningsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasActiveWarnings));
        }

        private void OnRecentConversionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasRecentConversions));
            OnPropertyChanged(nameof(IsHistoryEmpty));
            ClearRecentConversionsCommand.NotifyCanExecuteChanged();
        }

        private static string FormatBytes(long bytes)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;

            if (bytes < kb)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, bytes)} B");
            }

            if (bytes < mb)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{bytes / kb:0.0} KB");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{bytes / mb:0.0} MB");
        }

        private static string FormatBytesRange(long minBytes, long maxBytes)
        {
            return $"{FormatBytes(minBytes)} - {FormatBytes(maxBytes)}";
        }
    }
}








