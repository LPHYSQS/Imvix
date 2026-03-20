using CommunityToolkit.Mvvm.ComponentModel;
using ImvixPro.Models;
using System;
using System.Collections.Generic;

namespace ImvixPro.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly Dictionary<string, GifFrameRangeSelection> _gifTrimSelections = new(StringComparer.OrdinalIgnoreCase);
        private bool _isUpdatingGifTrimSelection;

        public int GifTrimRangeMinimum => 0;

        public int GifTrimRangeMaximum => Math.Max(0, GetGifTrimFrameCount() - 1);

        [ObservableProperty]
        private int selectedGifTrimStartIndex;

        [ObservableProperty]
        private int selectedGifTrimEndIndex;

        partial void OnSelectedGifTrimStartIndexChanged(int value)
        {
            if (_isUpdatingGifTrimSelection)
            {
                return;
            }

            ApplyGifTrimSelection(value, SelectedGifTrimEndIndex, updatePreview: true, cacheSelection: true);
        }

        partial void OnSelectedGifTrimEndIndexChanged(int value)
        {
            if (_isUpdatingGifTrimSelection)
            {
                return;
            }

            ApplyGifTrimSelection(SelectedGifTrimStartIndex, value, updatePreview: true, cacheSelection: true);
        }

        public void HandleGifTrimRangeChanged(int startIndex, int endIndex)
        {
            if (!IsGifTrimRangeVisible || SelectedImage is null || !SelectedImage.IsAnimatedGif)
            {
                return;
            }

            ApplyGifTrimSelection(startIndex, endIndex, updatePreview: true, cacheSelection: true);
        }

        private void RefreshGifTrimUiState()
        {
            OnPropertyChanged(nameof(IsGifTrimRangeVisible));
            OnPropertyChanged(nameof(GifTrimRangeText));
            OnPropertyChanged(nameof(GifTrimRangeMinimum));
            OnPropertyChanged(nameof(GifTrimRangeMaximum));
        }

        private void RestoreGifTrimSelection(ImageItemViewModel? image)
        {
            RefreshGifTrimUiState();

            if (image is null || !image.IsAnimatedGif || SelectedOutputFormat != OutputImageFormat.Gif)
            {
                ApplyGifTrimSelection(0, 0, updatePreview: false, cacheSelection: false, imageOverride: image);
                return;
            }

            var selection = _gifTrimSelections.TryGetValue(image.FilePath, out var cached)
                ? ClampGifTrimSelection(image, cached)
                : new GifFrameRangeSelection(0, Math.Max(0, image.GifFrameCount - 1));

            ApplyGifTrimSelection(selection.StartIndex, selection.EndIndex, updatePreview: false, cacheSelection: false, imageOverride: image);
        }

        private void ApplyGifTrimSelection(
            int startIndex,
            int endIndex,
            bool updatePreview,
            bool cacheSelection,
            ImageItemViewModel? imageOverride = null)
        {
            var image = imageOverride ?? SelectedImage;
            var selection = image is null
                ? new GifFrameRangeSelection(0, 0)
                : ClampGifTrimSelection(image, new GifFrameRangeSelection(startIndex, endIndex));

            _isUpdatingGifTrimSelection = true;
            try
            {
                SelectedGifTrimStartIndex = selection.StartIndex;
                SelectedGifTrimEndIndex = selection.EndIndex;
            }
            finally
            {
                _isUpdatingGifTrimSelection = false;
            }

            if (cacheSelection && image is not null && image.IsAnimatedGif)
            {
                _gifTrimSelections[image.FilePath] = selection;
            }

            if (updatePreview)
            {
                ApplyGifTrimPreviewRangeIfNeeded();
            }
        }

        private void ApplyGifTrimPreviewRangeIfNeeded()
        {
            if (!IsGifTrimRangeVisible ||
                SelectedImage is null ||
                !SelectedImage.IsAnimatedGif)
            {
                return;
            }

            if (_gifPreviewFrames is null || _gifPreviewDurations is null || _gifPreviewFrames.Count == 0)
            {
                _ = LoadGifPreviewAsync(SelectedImage.FilePath);
                return;
            }

            var selection = GetCurrentGifTrimSelection();
            var nextIndex = _gifPreviewIndex;
            if (nextIndex < selection.StartIndex || nextIndex > selection.EndIndex)
            {
                nextIndex = selection.StartIndex;
            }

            _gifPreviewIndex = nextIndex;
            SelectedPreview = _gifPreviewFrames[_gifPreviewIndex];

            if (_gifPreviewIndex < _gifPreviewDurations.Count)
            {
                _gifPreviewTimer.Interval = ClampGifDuration(_gifPreviewDurations[_gifPreviewIndex]);
            }
        }

        private GifFrameRangeSelection GetCurrentGifTrimSelection()
        {
            if (SelectedImage is null || !SelectedImage.IsAnimatedGif)
            {
                return new GifFrameRangeSelection(0, 0);
            }

            if (_gifTrimSelections.TryGetValue(SelectedImage.FilePath, out var cached))
            {
                return ClampGifTrimSelection(SelectedImage, cached);
            }

            return ClampGifTrimSelection(
                SelectedImage,
                new GifFrameRangeSelection(SelectedGifTrimStartIndex, SelectedGifTrimEndIndex));
        }

        private int GetGifTrimFrameCount()
        {
            return SelectedImage?.IsAnimatedGif == true
                ? Math.Max(1, SelectedImage.GifFrameCount)
                : 0;
        }

        private static GifFrameRangeSelection ClampGifTrimSelection(ImageItemViewModel image, GifFrameRangeSelection selection)
        {
            var maxIndex = Math.Max(0, image.GifFrameCount - 1);
            var start = Math.Clamp(selection.StartIndex, 0, maxIndex);
            var end = Math.Clamp(selection.EndIndex, start, maxIndex);
            return new GifFrameRangeSelection(start, end);
        }
    }
}
