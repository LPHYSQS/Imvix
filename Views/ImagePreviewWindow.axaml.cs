using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImvixPro.Models;
using ImvixPro.Services;
using ImvixPro.Services.PdfModule;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImvixPro.Views
{
    public partial class ImagePreviewWindow : Window
    {
        private const int PdfPreviewLowQualityWidth = 520;
        private const int PdfPreviewHighQualityWidth = 1400;
        private Bitmap? _previewBitmap;
        private readonly PdfRenderService _pdfRenderService = new();
        private readonly DispatcherTimer _gifPreviewTimer = new();
        private ImageConversionService.GifPreviewHandle? _gifPreviewHandle;
        private IReadOnlyList<Bitmap>? _gifPreviewFrames;
        private IReadOnlyList<TimeSpan>? _gifPreviewDurations;
        private int _gifPreviewIndex;
        private long _gifPreviewRequestId;
        private bool _isClosed;
        private readonly GifFrameRangeSelection? _gifFrameRange;
        private string? _pdfFilePath;
        private int _pdfPageIndex;
        private int _pdfPageCount;
        private bool _isPdfDocument;
        private CancellationTokenSource? _pdfPreviewCts;
        private long _pdfPreviewRequestId;

        public ImagePreviewWindow()
        {
            InitializeComponent();
            _gifPreviewTimer.Tick += OnGifPreviewTick;
        }

        public ImagePreviewWindow(
            string filePath,
            bool svgUseBackground,
            string svgBackgroundColor,
            GifFrameRangeSelection? gifFrameRange = null,
            int initialPdfPageIndex = 0,
            int pdfPageCount = 0)
            : this()
        {
            _gifFrameRange = gifFrameRange;
            _isPdfDocument = Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) || pdfPageCount > 0;
            _pdfFilePath = _isPdfDocument ? filePath : null;
            _pdfPageCount = Math.Max(0, pdfPageCount);
            _pdfPageIndex = Math.Max(0, initialPdfPageIndex);

            if (_isPdfDocument && _pdfPageCount <= 0 && _pdfRenderService.TryReadDocumentInfo(filePath, out var pdfInfo, out _))
            {
                _pdfPageCount = pdfInfo.PageCount;
            }

            var fileName = Path.GetFileName(filePath);
            Title = fileName;
            FileNameText.Text = fileName;

            if (_isPdfDocument)
            {
                RefreshPdfUiState();
                RefreshPdfPreview(preferImmediatePreview: true);
            }
            else
            {
                _previewBitmap = ImageConversionService.TryCreatePreview(filePath, 1400, svgUseBackground, svgBackgroundColor);
                PreviewImage.Source = _previewBitmap;
                _ = LoadGifPreviewAsync(filePath);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _isClosed = true;
            CancelPendingPdfPreview();
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            StopGifPreview();
        }

        private void RefreshPdfPreview(bool preferImmediatePreview)
        {
            CancelPendingPdfPreview();

            if (!_isPdfDocument || string.IsNullOrWhiteSpace(_pdfFilePath))
            {
                return;
            }

            var filePath = _pdfFilePath;
            var pageIndex = Math.Clamp(_pdfPageIndex, 0, Math.Max(0, _pdfPageCount - 1));
            var cancellationSource = new CancellationTokenSource();
            _pdfPreviewCts = cancellationSource;
            var requestId = Interlocked.Increment(ref _pdfPreviewRequestId);

            _ = LoadPdfPreviewAsync(filePath, pageIndex, requestId, cancellationSource, preferImmediatePreview);
        }

        private void RefreshPdfUiState()
        {
            if (!_isPdfDocument)
            {
                PreviousPageButton.IsVisible = false;
                NextPageButton.IsVisible = false;
                PageIndicatorText.IsVisible = false;
                PageIndicatorText.Text = string.Empty;
                return;
            }

            _pdfPageCount = Math.Max(1, _pdfPageCount);
            _pdfPageIndex = Math.Clamp(_pdfPageIndex, 0, _pdfPageCount - 1);

            var hasMultiplePages = _pdfPageCount > 1;
            PreviousPageButton.IsVisible = hasMultiplePages;
            NextPageButton.IsVisible = hasMultiplePages;
            PreviousPageButton.IsEnabled = _pdfPageIndex > 0;
            NextPageButton.IsEnabled = _pdfPageIndex < _pdfPageCount - 1;
            PageIndicatorText.IsVisible = true;
            PageIndicatorText.Text = $"{_pdfPageIndex + 1} / {_pdfPageCount}";
        }

        private async Task LoadGifPreviewAsync(string filePath)
        {
            var requestId = Interlocked.Increment(ref _gifPreviewRequestId);

            if (ImageConversionService.TryGetCachedGifPreview(filePath, 1400, out var cachedFull))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_isClosed || requestId != _gifPreviewRequestId)
                    {
                        cachedFull.Dispose();
                        return;
                    }

                    if (cachedFull.Frames.Count == 0 || cachedFull.Frames.Count != cachedFull.Durations.Count)
                    {
                        cachedFull.Dispose();
                        return;
                    }

                    StartGifPreview(cachedFull);
                });

                return;
            }

            if (ImageConversionService.TryGetCachedGifPreview(filePath, 760, out var cachedFallback))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_isClosed || requestId != _gifPreviewRequestId)
                    {
                        cachedFallback.Dispose();
                        return;
                    }

                    if (cachedFallback.Frames.Count == 0 || cachedFallback.Frames.Count != cachedFallback.Durations.Count)
                    {
                        cachedFallback.Dispose();
                        return;
                    }

                    StartGifPreview(cachedFallback);
                });
            }

            var fullHandle = await ImageConversionService.GetOrLoadGifPreviewAsync(filePath, 1400);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isClosed || requestId != _gifPreviewRequestId)
                {
                    fullHandle?.Dispose();
                    return;
                }

                if (fullHandle is null || fullHandle.Frames.Count == 0 || fullHandle.Frames.Count != fullHandle.Durations.Count)
                {
                    fullHandle?.Dispose();
                    return;
                }

                StartGifPreview(fullHandle);
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
            var selection = GetEffectiveGifFrameRange(frames.Count);
            _gifPreviewIndex = selection.StartIndex;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            PreviewImage.Source = frames[_gifPreviewIndex];
            _gifPreviewTimer.Interval = ClampGifDuration(durations[_gifPreviewIndex]);
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

        private void OnGifPreviewTick(object? sender, EventArgs e)
        {
            if (_gifPreviewFrames is null || _gifPreviewDurations is null || _gifPreviewFrames.Count == 0)
            {
                StopGifPreview();
                return;
            }

            var selection = GetEffectiveGifFrameRange(_gifPreviewFrames.Count);
            if (_gifPreviewIndex < selection.StartIndex || _gifPreviewIndex > selection.EndIndex)
            {
                _gifPreviewIndex = selection.StartIndex;
            }
            else
            {
                _gifPreviewIndex = _gifPreviewIndex >= selection.EndIndex
                    ? selection.StartIndex
                    : _gifPreviewIndex + 1;
            }

            PreviewImage.Source = _gifPreviewFrames[_gifPreviewIndex];

            if (_gifPreviewIndex < _gifPreviewDurations.Count)
            {
                _gifPreviewTimer.Interval = ClampGifDuration(_gifPreviewDurations[_gifPreviewIndex]);
            }
        }

        private GifFrameRangeSelection GetEffectiveGifFrameRange(int frameCount)
        {
            if (frameCount <= 0 || _gifFrameRange is null)
            {
                return new GifFrameRangeSelection(0, Math.Max(0, frameCount - 1));
            }

            var maxIndex = frameCount - 1;
            var start = Math.Clamp(_gifFrameRange.Value.StartIndex, 0, maxIndex);
            var end = Math.Clamp(_gifFrameRange.Value.EndIndex, start, maxIndex);
            return new GifFrameRangeSelection(start, end);
        }

        private static TimeSpan ClampGifDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero || duration.TotalMilliseconds <= 20)
            {
                return TimeSpan.FromMilliseconds(100);
            }

            return duration;
        }

        private void CancelPendingPdfPreview()
        {
            var cancellationSource = Interlocked.Exchange(ref _pdfPreviewCts, null);
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
                // Ignore races while replacing preview work.
            }

            cancellationSource.Dispose();
        }

        private async Task LoadPdfPreviewAsync(
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
                    await Task.Delay(32, cancellationSource.Token).ConfigureAwait(false);
                }

                cancellationSource.Token.ThrowIfCancellationRequested();
                lowPreview = await Task.Run(
                        () => _pdfRenderService.TryCreatePreview(filePath, pageIndex, PdfPreviewLowQualityWidth),
                        cancellationSource.Token)
                    .ConfigureAwait(false);

                if (lowPreview is not null)
                {
                    if (!await TryApplyPdfPreviewAsync(filePath, pageIndex, requestId, lowPreview).ConfigureAwait(false))
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
                    if (!await TryApplyPdfPreviewAsync(filePath, pageIndex, requestId, highPreview).ConfigureAwait(false))
                    {
                        return;
                    }

                    highPreview = null;
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore canceled preview requests so only the newest render is shown.
            }
            finally
            {
                lowPreview?.Dispose();
                highPreview?.Dispose();

                if (ReferenceEquals(Interlocked.CompareExchange(ref _pdfPreviewCts, null, cancellationSource), cancellationSource))
                {
                    cancellationSource.Dispose();
                }
            }
        }

        private async Task<bool> TryApplyPdfPreviewAsync(string filePath, int pageIndex, long requestId, Bitmap preview)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isClosed ||
                    requestId != _pdfPreviewRequestId ||
                    !_isPdfDocument ||
                    !string.Equals(_pdfFilePath, filePath, StringComparison.OrdinalIgnoreCase) ||
                    _pdfPageIndex != pageIndex)
                {
                    return false;
                }

                _previewBitmap?.Dispose();
                _previewBitmap = preview;
                PreviewImage.Source = preview;
                return true;
            });
        }

        private void OnPreviousPageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!_isPdfDocument || _pdfPageIndex <= 0)
            {
                return;
            }

            _pdfPageIndex--;
            RefreshPdfUiState();
            RefreshPdfPreview(preferImmediatePreview: true);
        }

        private void OnNextPageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!_isPdfDocument || _pdfPageIndex >= _pdfPageCount - 1)
            {
                return;
            }

            _pdfPageIndex++;
            RefreshPdfUiState();
            RefreshPdfPreview(preferImmediatePreview: true);
        }
    }
}
