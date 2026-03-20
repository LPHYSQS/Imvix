using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImvixPro.Models;
using ImvixPro.Services.PdfModule;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ImvixPro.ViewModels
{
    public partial class MainWindowViewModel
    {
        private const int PdfPreviewThrottleMilliseconds = 32;
        private const int PdfPreviewLowQualityWidth = 320;
        private const int PdfPreviewHighQualityWidth = 760;
        private readonly PdfImportService _pdfImportService = new();
        private readonly PdfRenderService _pdfRenderService = new();
        private readonly Dictionary<string, int> _pdfPageSelections = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PdfPageRangeSelection> _pdfPageRanges = new(StringComparer.OrdinalIgnoreCase);
        private bool _isUpdatingPdfPageSelection;
        private bool _isUpdatingPdfRangeSelection;
        private CancellationTokenSource? _selectedPdfPreviewCts;
        private long _selectedPdfPreviewRequestId;

        public bool IsPdfSelected => SelectedImage?.IsPdfDocument == true;

        public bool IsPdfNavigationVisible => SelectedImage?.IsPdfDocument == true && SelectedImage.PdfPageCount > 1;

        public bool IsPdfImageExportVisible => IsPdfSelected && SelectedOutputFormat != OutputImageFormat.Pdf;

        public bool IsPdfDocumentExportVisible => IsPdfSelected && SelectedOutputFormat == OutputImageFormat.Pdf;

        public bool IsPdfImageModeSelectorVisible => IsPdfImageExportVisible && SelectedImage?.PdfPageCount > 1;

        public bool IsPdfDocumentModeSelectorVisible => IsPdfDocumentExportVisible && SelectedImage?.PdfPageCount > 1;

        public bool IsPdfImagePageSliderVisible =>
            IsPdfImageExportVisible &&
            SelectedImage?.PdfPageCount > 1 &&
            SelectedPdfImageExportMode == PdfImageExportMode.CurrentPage;

        public bool IsPdfDocumentPageSliderVisible =>
            IsPdfDocumentExportVisible &&
            SelectedImage?.PdfPageCount > 1 &&
            SelectedPdfDocumentExportMode == PdfDocumentExportMode.CurrentPage;

        public bool IsPdfDocumentRangeSliderVisible =>
            IsPdfDocumentExportVisible &&
            SelectedImage?.PdfPageCount > 1 &&
            SelectedPdfDocumentExportMode == PdfDocumentExportMode.PageRange;

        public bool IsSinglePagePdf => SelectedImage?.IsPdfDocument == true && SelectedImage.PdfPageCount <= 1;

        public bool IsPdfImageAllPagesMode => SelectedPdfImageExportMode == PdfImageExportMode.AllPages;

        public bool IsPdfImageCurrentPageMode => SelectedPdfImageExportMode == PdfImageExportMode.CurrentPage;

        public bool IsPdfDocumentAllPagesMode => SelectedPdfDocumentExportMode == PdfDocumentExportMode.AllPages;

        public bool IsPdfDocumentCurrentPageMode => SelectedPdfDocumentExportMode == PdfDocumentExportMode.CurrentPage;

        public bool IsPdfDocumentPageRangeMode => SelectedPdfDocumentExportMode == PdfDocumentExportMode.PageRange;

        public bool IsPdfDocumentSplitSinglePagesMode => SelectedPdfDocumentExportMode == PdfDocumentExportMode.SplitSinglePages;

        public int PdfPageMinimum => 0;

        public int PdfPageMaximum => Math.Max(0, GetSelectedPdfPageCount() - 1);

        public int PdfRangeMinimum => 0;

        public int PdfRangeMaximum => Math.Max(0, GetSelectedPdfPageCount() - 1);

        public bool CanGoToPreviousPdfPage => IsPdfNavigationVisible && SelectedPdfPageIndex > 0;

        public bool CanGoToNextPdfPage => IsPdfNavigationVisible && SelectedPdfPageIndex < PdfPageMaximum;

        public string PdfPageIndicatorText => GetSelectedPdfPageCount() <= 0
            ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, T("PdfPageIndicatorTemplate"), SelectedPdfPageIndex + 1, GetSelectedPdfPageCount());

        public string PdfPageRangeIndicatorText => GetSelectedPdfPageCount() <= 0
            ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, T("PdfPageRangeIndicatorTemplate"), SelectedPdfRangeStartIndex + 1, SelectedPdfRangeEndIndex + 1);

        public string PdfPageText => T("PdfPageText");

        public string PdfPreviousPageText => T("PdfPreviousPage");

        public string PdfNextPageText => T("PdfNextPage");

        public string PdfImageExportTitleText => T("PdfImageExportTitle");

        public string PdfDocumentExportTitleText => T("PdfDocumentExportTitle");

        public string PdfExportAllPagesText => T("PdfExportAllPages");

        public string PdfExportCurrentPageText => T("PdfExportCurrentPage");

        public string PdfExportPageRangeText => T("PdfExportPageRange");

        public string PdfSplitSinglePagesText => T("PdfSplitSinglePages");

        public string PdfSinglePageHintText => T("PdfSinglePageHint");

        [ObservableProperty]
        private int selectedPdfPageIndex;

        [ObservableProperty]
        private int selectedPdfRangeStartIndex;

        [ObservableProperty]
        private int selectedPdfRangeEndIndex;

        [ObservableProperty]
        private PdfImageExportMode selectedPdfImageExportMode = PdfImageExportMode.AllPages;

        [ObservableProperty]
        private PdfDocumentExportMode selectedPdfDocumentExportMode = PdfDocumentExportMode.AllPages;

        partial void OnSelectedPdfPageIndexChanged(int value)
        {
            if (_isUpdatingPdfPageSelection)
            {
                return;
            }

            ApplyPdfPageSelection(value, updatePreview: true, cacheSelection: true);
        }

        partial void OnSelectedPdfRangeStartIndexChanged(int value)
        {
            if (_isUpdatingPdfRangeSelection)
            {
                return;
            }

            ApplyPdfRangeSelection(value, SelectedPdfRangeEndIndex, cacheSelection: true);
        }

        partial void OnSelectedPdfRangeEndIndexChanged(int value)
        {
            if (_isUpdatingPdfRangeSelection)
            {
                return;
            }

            ApplyPdfRangeSelection(SelectedPdfRangeStartIndex, value, cacheSelection: true);
        }

        partial void OnSelectedPdfImageExportModeChanged(PdfImageExportMode value)
        {
            RefreshPdfUiState();
        }

        partial void OnSelectedPdfDocumentExportModeChanged(PdfDocumentExportMode value)
        {
            RefreshPdfUiState();
        }

        public bool TryCreateInputItem(string path, out ImageItemViewModel? item, out string? error, bool generateThumbnail = true)
        {
            if (PdfImportService.IsPdfFile(path))
            {
                return _pdfImportService.TryCreate(path, out item, out error, generateThumbnail);
            }

            return ImageItemViewModel.TryCreate(path, out item, out error, generateThumbnail);
        }

        public void HandlePdfPageSliderChanged(double value)
        {
            if (!IsPdfSelected)
            {
                return;
            }

            ApplyPdfPageSelection(
                (int)Math.Round(value, MidpointRounding.AwayFromZero),
                updatePreview: true,
                cacheSelection: true,
                preferImmediatePreview: false);
        }

        public void HandlePdfRangeChanged(int startIndex, int endIndex)
        {
            if (!IsPdfSelected)
            {
                return;
            }

            ApplyPdfRangeSelection(startIndex, endIndex, cacheSelection: true);
        }

        public void SetPdfImageExportMode(PdfImageExportMode mode)
        {
            if (SelectedPdfImageExportMode != mode)
            {
                SelectedPdfImageExportMode = mode;
                return;
            }

            RefreshPdfUiState();
        }

        public void SetPdfDocumentExportMode(PdfDocumentExportMode mode)
        {
            if (SelectedPdfDocumentExportMode != mode)
            {
                SelectedPdfDocumentExportMode = mode;
                return;
            }

            RefreshPdfUiState();
        }

        [RelayCommand(CanExecute = nameof(CanGoToPreviousPdfPage))]
        private void GoToPreviousPdfPage()
        {
            ApplyPdfPageSelection(SelectedPdfPageIndex - 1, updatePreview: true, cacheSelection: true, preferImmediatePreview: true);
        }

        [RelayCommand(CanExecute = nameof(CanGoToNextPdfPage))]
        private void GoToNextPdfPage()
        {
            ApplyPdfPageSelection(SelectedPdfPageIndex + 1, updatePreview: true, cacheSelection: true, preferImmediatePreview: true);
        }

        private void RestorePdfSelection(ImageItemViewModel? image)
        {
            if (image is null || !image.IsPdfDocument)
            {
                ApplyPdfPageSelection(0, updatePreview: false, cacheSelection: false, imageOverride: image);
                ApplyPdfRangeSelection(0, 0, cacheSelection: false, imageOverride: image);
                return;
            }

            var pageIndex = _pdfPageSelections.TryGetValue(image.FilePath, out var cachedPageIndex)
                ? ClampPdfPageIndex(image, cachedPageIndex)
                : 0;

            var rangeSelection = _pdfPageRanges.TryGetValue(image.FilePath, out var cachedRange)
                ? ClampPdfRangeSelection(image, cachedRange)
                : new PdfPageRangeSelection(0, Math.Max(0, image.PdfPageCount - 1));

            ApplyPdfPageSelection(pageIndex, updatePreview: false, cacheSelection: false, imageOverride: image);
            ApplyPdfRangeSelection(rangeSelection.StartIndex, rangeSelection.EndIndex, cacheSelection: false, imageOverride: image);
        }

        private void RefreshSelectedPdfPreview(bool preferImmediatePreview = false)
        {
            CancelPendingPdfPreviewRender();

            if (SelectedImage is null || !SelectedImage.IsPdfDocument)
            {
                return;
            }

            var filePath = SelectedImage.FilePath;
            var pageIndex = ClampPdfPageIndex(SelectedImage, SelectedPdfPageIndex);
            var cancellationSource = new CancellationTokenSource();
            _selectedPdfPreviewCts = cancellationSource;
            var requestId = Interlocked.Increment(ref _selectedPdfPreviewRequestId);

            _ = LoadSelectedPdfPreviewAsync(filePath, pageIndex, requestId, cancellationSource, preferImmediatePreview);
        }

        private void RefreshPdfUiState()
        {
            if (SelectedImage is not { IsPdfDocument: true } || SelectedImage.PdfPageCount <= 1)
            {
                if (SelectedPdfImageExportMode != PdfImageExportMode.CurrentPage)
                {
                    _isUpdatingPdfPageSelection = true;
                    try
                    {
                        SelectedPdfImageExportMode = PdfImageExportMode.CurrentPage;
                    }
                    finally
                    {
                        _isUpdatingPdfPageSelection = false;
                    }
                }

                if (SelectedPdfDocumentExportMode != PdfDocumentExportMode.AllPages)
                {
                    _isUpdatingPdfRangeSelection = true;
                    try
                    {
                        SelectedPdfDocumentExportMode = PdfDocumentExportMode.AllPages;
                    }
                    finally
                    {
                        _isUpdatingPdfRangeSelection = false;
                    }
                }
            }

            OnPropertyChanged(nameof(IsPdfSelected));
            OnPropertyChanged(nameof(IsPdfNavigationVisible));
            OnPropertyChanged(nameof(IsPdfImageExportVisible));
            OnPropertyChanged(nameof(IsPdfDocumentExportVisible));
            OnPropertyChanged(nameof(IsPdfImageModeSelectorVisible));
            OnPropertyChanged(nameof(IsPdfDocumentModeSelectorVisible));
            OnPropertyChanged(nameof(IsPdfImagePageSliderVisible));
            OnPropertyChanged(nameof(IsPdfDocumentPageSliderVisible));
            OnPropertyChanged(nameof(IsPdfDocumentRangeSliderVisible));
            OnPropertyChanged(nameof(IsSinglePagePdf));
            OnPropertyChanged(nameof(IsPdfImageAllPagesMode));
            OnPropertyChanged(nameof(IsPdfImageCurrentPageMode));
            OnPropertyChanged(nameof(IsPdfDocumentAllPagesMode));
            OnPropertyChanged(nameof(IsPdfDocumentCurrentPageMode));
            OnPropertyChanged(nameof(IsPdfDocumentPageRangeMode));
            OnPropertyChanged(nameof(IsPdfDocumentSplitSinglePagesMode));
            OnPropertyChanged(nameof(PdfPageMinimum));
            OnPropertyChanged(nameof(PdfPageMaximum));
            OnPropertyChanged(nameof(PdfRangeMinimum));
            OnPropertyChanged(nameof(PdfRangeMaximum));
            RefreshPdfPageState();
            RefreshPdfRangeState();
        }

        private void RefreshPdfLocalizedProperties()
        {
            OnPropertyChanged(nameof(PdfPageText));
            OnPropertyChanged(nameof(PdfPreviousPageText));
            OnPropertyChanged(nameof(PdfNextPageText));
            OnPropertyChanged(nameof(PdfImageExportTitleText));
            OnPropertyChanged(nameof(PdfDocumentExportTitleText));
            OnPropertyChanged(nameof(PdfExportAllPagesText));
            OnPropertyChanged(nameof(PdfExportCurrentPageText));
            OnPropertyChanged(nameof(PdfExportPageRangeText));
            OnPropertyChanged(nameof(PdfSplitSinglePagesText));
            OnPropertyChanged(nameof(PdfSinglePageHintText));
            OnPropertyChanged(nameof(PdfPageIndicatorText));
            OnPropertyChanged(nameof(PdfPageRangeIndicatorText));
            RefreshPdfUiState();
        }

        private void ApplyPdfPageSelection(
            int pageIndex,
            bool updatePreview,
            bool cacheSelection,
            bool preferImmediatePreview = false,
            ImageItemViewModel? imageOverride = null)
        {
            var image = imageOverride ?? SelectedImage;
            var clampedPageIndex = ClampPdfPageIndex(image, pageIndex);
            var isCurrentImage = ReferenceEquals(image, SelectedImage);

            if (cacheSelection && image is { IsPdfDocument: true })
            {
                _pdfPageSelections[image.FilePath] = clampedPageIndex;
            }

            if (isCurrentImage && clampedPageIndex == SelectedPdfPageIndex)
            {
                return;
            }

            _isUpdatingPdfPageSelection = true;
            try
            {
                SelectedPdfPageIndex = clampedPageIndex;
            }
            finally
            {
                _isUpdatingPdfPageSelection = false;
            }

            if (updatePreview && image is { IsPdfDocument: true } && isCurrentImage)
            {
                RefreshSelectedPdfPreview(preferImmediatePreview);
            }

            if (isCurrentImage)
            {
                RefreshPdfPageState();
            }
        }

        private void ApplyPdfRangeSelection(int startIndex, int endIndex, bool cacheSelection, ImageItemViewModel? imageOverride = null)
        {
            var image = imageOverride ?? SelectedImage;
            var selection = ClampPdfRangeSelection(image, new PdfPageRangeSelection(startIndex, endIndex));
            var isCurrentImage = ReferenceEquals(image, SelectedImage);

            if (cacheSelection && image is { IsPdfDocument: true })
            {
                _pdfPageRanges[image.FilePath] = selection;
            }

            if (isCurrentImage &&
                selection.StartIndex == SelectedPdfRangeStartIndex &&
                selection.EndIndex == SelectedPdfRangeEndIndex)
            {
                return;
            }

            _isUpdatingPdfRangeSelection = true;
            try
            {
                SelectedPdfRangeStartIndex = selection.StartIndex;
                SelectedPdfRangeEndIndex = selection.EndIndex;
            }
            finally
            {
                _isUpdatingPdfRangeSelection = false;
            }

            if (SelectedPdfDocumentExportMode == PdfDocumentExportMode.PageRange &&
                SelectedImage is { IsPdfDocument: true } &&
                (SelectedPdfPageIndex < selection.StartIndex || SelectedPdfPageIndex > selection.EndIndex))
            {
                ApplyPdfPageSelection(selection.StartIndex, updatePreview: true, cacheSelection: true, preferImmediatePreview: false);
                return;
            }

            if (isCurrentImage)
            {
                RefreshPdfRangeState();
            }
        }

        private void RefreshPdfPageState()
        {
            OnPropertyChanged(nameof(CanGoToPreviousPdfPage));
            OnPropertyChanged(nameof(CanGoToNextPdfPage));
            OnPropertyChanged(nameof(PdfPageIndicatorText));
            GoToPreviousPdfPageCommand.NotifyCanExecuteChanged();
            GoToNextPdfPageCommand.NotifyCanExecuteChanged();
        }

        private void RefreshPdfRangeState()
        {
            OnPropertyChanged(nameof(PdfPageRangeIndicatorText));
        }

        private void CancelPendingPdfPreviewRender()
        {
            var cancellationSource = Interlocked.Exchange(ref _selectedPdfPreviewCts, null);
            if (cancellationSource is null)
            {
                return;
            }

            try
            {
                cancellationSource.Cancel();
            }
            catch
            {
                // Ignore races while shutting down pending preview work.
            }

            cancellationSource.Dispose();
        }

        private async Task LoadSelectedPdfPreviewAsync(
            string filePath,
            int pageIndex,
            long requestId,
            CancellationTokenSource cancellationSource,
            bool preferImmediatePreview)
        {
            Bitmap? lowPreview = null;
            Bitmap? highPreview = null;

            try
            {
                if (!preferImmediatePreview)
                {
                    await Task.Delay(PdfPreviewThrottleMilliseconds, cancellationSource.Token).ConfigureAwait(false);
                }

                cancellationSource.Token.ThrowIfCancellationRequested();
                lowPreview = await Task.Run(
                        () => _pdfRenderService.TryCreatePreview(filePath, pageIndex, PdfPreviewLowQualityWidth),
                        cancellationSource.Token)
                    .ConfigureAwait(false);

                if (lowPreview is not null)
                {
                    if (!await TryApplySelectedPdfPreviewAsync(filePath, pageIndex, requestId, lowPreview).ConfigureAwait(false))
                    {
                        return;
                    }

                    lowPreview = null;
                }

                cancellationSource.Token.ThrowIfCancellationRequested();
                highPreview = await Task.Run(
                        () => _pdfRenderService.TryCreatePreview(filePath, pageIndex, PdfPreviewHighQualityWidth),
                        cancellationSource.Token)
                    .ConfigureAwait(false);

                if (highPreview is not null)
                {
                    if (!await TryApplySelectedPdfPreviewAsync(filePath, pageIndex, requestId, highPreview).ConfigureAwait(false))
                    {
                        return;
                    }

                    highPreview = null;
                }
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellations so only the most recent preview request wins.
            }
            finally
            {
                lowPreview?.Dispose();
                highPreview?.Dispose();

                if (ReferenceEquals(Interlocked.CompareExchange(ref _selectedPdfPreviewCts, null, cancellationSource), cancellationSource))
                {
                    cancellationSource.Dispose();
                }
            }
        }

        private async Task<bool> TryApplySelectedPdfPreviewAsync(string filePath, int pageIndex, long requestId, Bitmap preview)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestId != _selectedPdfPreviewRequestId ||
                    SelectedImage is null ||
                    !SelectedImage.IsPdfDocument ||
                    !SelectedImage.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
                    SelectedPdfPageIndex != pageIndex)
                {
                    return false;
                }

                SelectedPreview?.Dispose();
                SelectedPreview = preview;
                return true;
            });
        }

        private int GetSelectedPdfPageCount()
        {
            return SelectedImage?.IsPdfDocument == true
                ? Math.Max(1, SelectedImage.PdfPageCount)
                : 0;
        }

        private static int ClampPdfPageIndex(ImageItemViewModel? image, int pageIndex)
        {
            if (image is null || !image.IsPdfDocument)
            {
                return 0;
            }

            return Math.Clamp(pageIndex, 0, Math.Max(0, image.PdfPageCount - 1));
        }

        private static PdfPageRangeSelection ClampPdfRangeSelection(ImageItemViewModel? image, PdfPageRangeSelection selection)
        {
            if (image is null || !image.IsPdfDocument)
            {
                return new PdfPageRangeSelection(0, 0);
            }

            var maxIndex = Math.Max(0, image.PdfPageCount - 1);
            var start = Math.Clamp(selection.StartIndex, 0, maxIndex);
            var end = Math.Clamp(selection.EndIndex, start, maxIndex);
            return new PdfPageRangeSelection(start, end);
        }
    }
}
