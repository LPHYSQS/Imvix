using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Imvix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Imvix.Views
{
    public partial class ImagePreviewWindow : Window
    {
        private Bitmap? _previewBitmap;
        private readonly DispatcherTimer _gifPreviewTimer = new();
        private ImageConversionService.GifPreviewHandle? _gifPreviewHandle;
        private IReadOnlyList<Bitmap>? _gifPreviewFrames;
        private IReadOnlyList<TimeSpan>? _gifPreviewDurations;
        private int _gifPreviewIndex;
        private long _gifPreviewRequestId;
        private bool _isClosed;

        public ImagePreviewWindow()
        {
            InitializeComponent();
            _gifPreviewTimer.Tick += OnGifPreviewTick;
        }

        public ImagePreviewWindow(string filePath, bool svgUseBackground, string svgBackgroundColor)
            : this()
        {

            var fileName = Path.GetFileName(filePath);
            Title = fileName;
            FileNameText.Text = fileName;

            _previewBitmap = ImageConversionService.TryCreatePreview(filePath, 1400, svgUseBackground, svgBackgroundColor);
            PreviewImage.Source = _previewBitmap;

            _ = LoadGifPreviewAsync(filePath);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _isClosed = true;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            StopGifPreview();
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
            _gifPreviewIndex = 0;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            PreviewImage.Source = frames[0];
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

        private void OnGifPreviewTick(object? sender, EventArgs e)
        {
            if (_gifPreviewFrames is null || _gifPreviewDurations is null || _gifPreviewFrames.Count == 0)
            {
                StopGifPreview();
                return;
            }

            _gifPreviewIndex = (_gifPreviewIndex + 1) % _gifPreviewFrames.Count;
            PreviewImage.Source = _gifPreviewFrames[_gifPreviewIndex];

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
    }
}

