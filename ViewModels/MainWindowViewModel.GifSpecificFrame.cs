using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImvixPro.Models;
using ImvixPro.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ImvixPro.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly Dictionary<string, int> _gifSpecificFrameSelections = new(StringComparer.OrdinalIgnoreCase);
        private bool _persistGifSpecificFrameSelection = true;
        private bool _refreshGifSpecificFramePreview = true;
        private bool _cacheGifSpecificFrameSelection = true;
        private bool _isRestoringGifSpecificFrameSelection;
        private bool _isUpdatingGifSpecificFrameSliderValue;
        private long _gifSpecificFrameRestoreRequestId;

        public bool IsGifSpecificFrameControlsVisible =>
            IsGifPreviewVisible &&
            SelectedImage?.IsAnimatedGif == true &&
            SelectedGifHandlingMode == GifHandlingMode.SpecificFrame;

        public int GifSpecificFrameMaximum => Math.Max(0, GetGifSpecificFrameCount() - 1);

        public string GifSpecificFrameCountdownText => GetGifSpecificFrameCount() <= 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                T("GifFrameIndicatorTemplate"),
                ClampGifSpecificFrameIndex(SelectedGifSpecificFrameIndex) + 1,
                GetGifSpecificFrameCount());

        public bool CanAdjustGifSpecificFrame => IsGifSpecificFrameControlsVisible && GetGifSpecificFrameCount() > 1;

        public bool IsGifSpecificFramePlaybackPaused => !IsGifSpecificFramePlaying;

        public string GifSpecificFramePlaybackButtonText => IsGifSpecificFramePlaying ? PauseText : ResumeText;

        [ObservableProperty]
        private int selectedGifSpecificFrameIndex;

        [ObservableProperty]
        private bool isGifSpecificFramePlaying;

        [ObservableProperty]
        private double gifSpecificFrameSliderValue;

        partial void OnSelectedGifSpecificFrameIndexChanged(int value)
        {
            var clamped = ClampGifSpecificFrameIndex(value);
            if (value != clamped)
            {
                SetGifSpecificFrameIndex(clamped, _persistGifSpecificFrameSelection, _refreshGifSpecificFramePreview);
                return;
            }

            _gifPreviewIndex = clamped;
            if (!_isRestoringGifSpecificFrameSelection &&
                _cacheGifSpecificFrameSelection &&
                SelectedImage is not null &&
                SelectedImage.IsAnimatedGif)
            {
                _gifSpecificFrameSelections[SelectedImage.FilePath] = ClampGifSpecificFrameIndex(SelectedImage, clamped);
            }

            OnPropertyChanged(nameof(GifSpecificFrameCountdownText));

            if (!_isRestoringGifSpecificFrameSelection &&
                _refreshGifSpecificFramePreview &&
                SelectedGifHandlingMode == GifHandlingMode.SpecificFrame)
            {
                if (_gifPreviewFrames is not null && _gifPreviewFrames.Count > 0)
                {
                    ApplyGifSpecificFramePreview(disposeExistingPreview: false);
                }
                else if (SelectedImage?.IsAnimatedGif == true)
                {
                    _ = LoadGifPreviewAsync(SelectedImage.FilePath);
                }
            }

            if (!_isRestoringGifSpecificFrameSelection && _persistGifSpecificFrameSelection)
            {
                PersistSettings();
            }
        }

        partial void OnIsGifSpecificFramePlayingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsGifSpecificFramePlaybackPaused));
            OnPropertyChanged(nameof(GifSpecificFramePlaybackButtonText));
        }

        public void HandleGifSpecificFrameSliderChanged(double value)
        {
            if (_isUpdatingGifSpecificFrameSliderValue ||
                SelectedImage is null ||
                !SelectedImage.IsAnimatedGif ||
                SelectedGifHandlingMode != GifHandlingMode.SpecificFrame)
            {
                return;
            }

            _gifSpecificFrameRestoreRequestId++;
            _isRestoringGifSpecificFrameSelection = false;
            SetGifSpecificFrameIndex((int)Math.Round(value, MidpointRounding.AwayFromZero), persist: true, refreshPreview: true);
        }

        [RelayCommand]
        private async Task ToggleGifSpecificFramePlaybackAsync()
        {
            if (IsGifSpecificFramePlaying)
            {
                PauseGifSpecificFramePlayback();
                return;
            }

            if (!CanAdjustGifSpecificFrame || SelectedImage is null)
            {
                return;
            }

            IsGifSpecificFramePlaying = true;

            if (_gifPreviewFrames is not null &&
                _gifPreviewDurations is not null &&
                _gifPreviewFrames.Count > 0 &&
                _gifPreviewFrames.Count == _gifPreviewDurations.Count)
            {
                StartGifSpecificFramePlayback();
                return;
            }

            await LoadGifPreviewAsync(SelectedImage.FilePath);

            if (_gifPreviewFrames is null || !IsGifSpecificFrameControlsVisible)
            {
                IsGifSpecificFramePlaying = false;
            }
        }

        private void RefreshGifHandlingModeOptions()
        {
            RebuildEnumOptions(
                GifHandlingModes,
                GetAvailableGifHandlingModes(),
                mode => T($"GifHandling_{mode}"));

            var selectedOption = GifHandlingModes.FirstOrDefault(x => EqualityComparer<GifHandlingMode>.Default.Equals(x.Value, SelectedGifHandlingMode));
            if (selectedOption is null && IsGifPreviewVisible)
            {
                var fallbackMode = IsGifPdfModeSelectorVisible
                    ? GifHandlingMode.AllFrames
                    : GifHandlingMode.FirstFrame;

                if (SelectedGifHandlingMode != fallbackMode)
                {
                    SelectedGifHandlingMode = fallbackMode;
                    return;
                }

                selectedOption = GifHandlingModes.FirstOrDefault(x => EqualityComparer<GifHandlingMode>.Default.Equals(x.Value, SelectedGifHandlingMode));
            }

            if (!ReferenceEquals(SelectedGifHandlingModeOption, selectedOption))
            {
                SelectedGifHandlingModeOption = selectedOption;
            }

            RefreshGifSpecificFrameUiState();
            RefreshGifPdfUiState();
        }

        private void RefreshGifSpecificFrameUiState()
        {
            if (!IsGifSpecificFrameControlsVisible && IsGifSpecificFramePlaying)
            {
                PauseGifSpecificFramePlayback();
            }

            OnPropertyChanged(nameof(IsGifSpecificFrameControlsVisible));
            OnPropertyChanged(nameof(GifSpecificFrameMaximum));
            OnPropertyChanged(nameof(CanAdjustGifSpecificFrame));
            SetGifSpecificFrameIndex(ClampGifSpecificFrameIndex(SelectedGifSpecificFrameIndex), persist: false, refreshPreview: false, cacheSelection: false);
            OnPropertyChanged(nameof(GifSpecificFrameCountdownText));
        }

        private void RestoreGifSpecificFrameSelection(ImageItemViewModel? image)
        {
            var requestId = ++_gifSpecificFrameRestoreRequestId;
            _isRestoringGifSpecificFrameSelection = true;

            OnPropertyChanged(nameof(IsGifSpecificFrameControlsVisible));
            OnPropertyChanged(nameof(GifSpecificFrameMaximum));
            OnPropertyChanged(nameof(CanAdjustGifSpecificFrame));

            if (image is null || !image.IsAnimatedGif)
            {
                SetGifSpecificFrameIndex(0, persist: false, refreshPreview: false, cacheSelection: false);
                _isRestoringGifSpecificFrameSelection = false;
                return;
            }

            var storedIndex = 0;
            if (_gifSpecificFrameSelections.TryGetValue(image.FilePath, out var cachedIndex))
            {
                storedIndex = ClampGifSpecificFrameIndex(image, cachedIndex);
            }

            SetGifSpecificFrameIndex(storedIndex, persist: false, refreshPreview: false, cacheSelection: false);

            Dispatcher.UIThread.Post(() =>
            {
                if (requestId != _gifSpecificFrameRestoreRequestId ||
                    SelectedImage is null ||
                    !SelectedImage.FilePath.Equals(image.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var stableIndex = _gifSpecificFrameSelections.TryGetValue(image.FilePath, out var restoredIndex)
                    ? ClampGifSpecificFrameIndex(image, restoredIndex)
                    : 0;

                SetGifSpecificFrameIndex(stableIndex, persist: false, refreshPreview: false, cacheSelection: false);
                if (_gifPreviewFrames is not null && SelectedGifHandlingMode == GifHandlingMode.SpecificFrame)
                {
                    ApplyGifSpecificFramePreview(disposeExistingPreview: false);
                }

                OnPropertyChanged(nameof(GifSpecificFrameCountdownText));
                _isRestoringGifSpecificFrameSelection = false;
            }, DispatcherPriority.Render);
        }

        private bool ShouldLoadGifPreviewFrames()
        {
            return SelectedImage?.IsAnimatedGif == true &&
                   (ShouldAnimateGifPreview() || SelectedGifHandlingMode == GifHandlingMode.SpecificFrame);
        }

        private void ApplyLoadedGifPreviewHandle(ImageConversionService.GifPreviewHandle handle)
        {
            if (SelectedGifHandlingMode == GifHandlingMode.SpecificFrame && !ShouldAnimateGifPreview())
            {
                ApplyGifSpecificFrameHandle(handle);
                return;
            }

            StartGifPreview(handle);
        }

        private void ApplyGifSpecificFrameHandle(ImageConversionService.GifPreviewHandle handle)
        {
            ResetGifPreviewState(resetSpecificFramePlaybackState: false);

            var frames = handle.Frames;
            var durations = handle.Durations;
            if (frames.Count == 0 || frames.Count != durations.Count)
            {
                handle.Dispose();
                IsGifSpecificFramePlaying = false;
                return;
            }

            _gifPreviewHandle = handle;
            _gifPreviewFrames = frames;
            _gifPreviewDurations = durations;
            SelectedPreview?.Dispose();

            SetGifSpecificFrameIndex(ClampGifSpecificFrameIndex(SelectedGifSpecificFrameIndex), persist: false, refreshPreview: false, cacheSelection: false);
            SelectedPreview = frames[_gifPreviewIndex];

            if (IsGifSpecificFramePlaying)
            {
                StartGifSpecificFramePlayback();
            }
            else
            {
                PauseGifSpecificFramePlayback();
            }
        }

        private void StartGifSpecificFramePlayback()
        {
            if (!IsGifSpecificFrameControlsVisible ||
                _gifPreviewFrames is null ||
                _gifPreviewDurations is null ||
                _gifPreviewFrames.Count == 0 ||
                _gifPreviewFrames.Count != _gifPreviewDurations.Count)
            {
                IsGifSpecificFramePlaying = false;
                return;
            }

            _gifPreviewIndex = ClampGifSpecificFrameIndex(SelectedGifSpecificFrameIndex);
            SelectedPreview = _gifPreviewFrames[_gifPreviewIndex];

            var durationIndex = Math.Min(_gifPreviewIndex, _gifPreviewDurations.Count - 1);
            _gifPreviewTimer.Interval = ClampGifDuration(_gifPreviewDurations[durationIndex]);
            _gifPreviewTimer.Start();
        }

        private void PauseGifSpecificFramePlayback()
        {
            if (_gifPreviewTimer.IsEnabled)
            {
                _gifPreviewTimer.Stop();
            }

            if (IsGifSpecificFramePlaying)
            {
                IsGifSpecificFramePlaying = false;
            }
        }

        private void ApplyGifSpecificFramePreview(bool disposeExistingPreview)
        {
            if (_gifPreviewFrames is null || _gifPreviewFrames.Count == 0)
            {
                return;
            }

            _gifPreviewIndex = ClampGifSpecificFrameIndex(SelectedGifSpecificFrameIndex);

            if (disposeExistingPreview)
            {
                SelectedPreview?.Dispose();
            }

            SelectedPreview = _gifPreviewFrames[_gifPreviewIndex];
            OnPropertyChanged(nameof(GifSpecificFrameCountdownText));
        }

        private void SetGifSpecificFrameIndex(int index, bool persist, bool refreshPreview, bool cacheSelection = true)
        {
            var clamped = ClampGifSpecificFrameIndex(index);
            if (SelectedGifSpecificFrameIndex == clamped)
            {
                _gifPreviewIndex = clamped;
                OnPropertyChanged(nameof(GifSpecificFrameCountdownText));
                return;
            }

            _persistGifSpecificFrameSelection = persist;
            _refreshGifSpecificFramePreview = refreshPreview;
            _cacheGifSpecificFrameSelection = cacheSelection;

            try
            {
                _isUpdatingGifSpecificFrameSliderValue = true;
                GifSpecificFrameSliderValue = clamped;
                SelectedGifSpecificFrameIndex = clamped;
            }
            finally
            {
                _isUpdatingGifSpecificFrameSliderValue = false;
                _persistGifSpecificFrameSelection = true;
                _refreshGifSpecificFramePreview = true;
                _cacheGifSpecificFrameSelection = true;
            }
        }

        private IReadOnlyList<GifHandlingMode> GetAvailableGifHandlingModes()
        {
            var modes = new List<GifHandlingMode>();

            if (!IsGifPdfModeSelectorVisible)
            {
                modes.Add(GifHandlingMode.FirstFrame);
            }

            if (SelectedOutputFormat != OutputImageFormat.Pdf || SelectedImage?.IsAnimatedGif == true)
            {
                modes.Add(GifHandlingMode.AllFrames);
            }

            if (CanUseSpecificGifFrameMode())
            {
                modes.Add(GifHandlingMode.SpecificFrame);
            }

            return modes;
        }

        private bool CanUseSpecificGifFrameMode()
        {
            return IsGifPreviewVisible && SelectedImage?.IsAnimatedGif == true;
        }

        private int GetGifSpecificFrameCount()
        {
            return SelectedImage?.IsAnimatedGif == true
                ? Math.Max(1, SelectedImage.GifFrameCount)
                : 0;
        }

        private int ClampGifSpecificFrameIndex(int index)
        {
            var frameCount = GetGifSpecificFrameCount();
            if (frameCount <= 0)
            {
                return 0;
            }

            return Math.Clamp(index, 0, frameCount - 1);
        }

        private static int ClampGifSpecificFrameIndex(ImageItemViewModel image, int index)
        {
            var frameCount = Math.Max(1, image.GifFrameCount);
            return Math.Clamp(index, 0, frameCount - 1);
        }
    }
}
