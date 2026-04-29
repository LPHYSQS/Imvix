using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Imvix.Services
{
    internal static class WindowWorkAreaAdapter
    {
        private static readonly ConditionalWeakTable<Window, WindowWorkAreaController> Controllers = new();

        public static void Attach(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            Controllers.GetValue(window, static target => new WindowWorkAreaController(target));
        }

        private sealed class WindowWorkAreaController : IDisposable
        {
            private const double FallbackWidth = 960d;
            private const double FallbackHeight = 720d;
            private const double FallbackMinWidth = 320d;
            private const double FallbackMinHeight = 240d;

            private readonly Window _window;
            private readonly double _initialWidth;
            private readonly double _initialHeight;
            private readonly double _initialMinWidth;
            private readonly double _initialMinHeight;

            private Screens? _screens;
            private Screen? _lastScreen;
            private bool _isOpened;
            private bool _isDisposed;
            private bool _isAdjusting;
            private bool _adjustmentQueued;

            public WindowWorkAreaController(Window window)
            {
                _window = window;
                _initialWidth = NormalizeSize(window.Width, FallbackWidth);
                _initialHeight = NormalizeSize(window.Height, FallbackHeight);
                _initialMinWidth = NormalizeMinimum(window.MinWidth, _initialWidth, FallbackMinWidth);
                _initialMinHeight = NormalizeMinimum(window.MinHeight, _initialHeight, FallbackMinHeight);

                SubscribeToScreens(window.Screens);

                _window.Opened += OnOpened;
                _window.Closed += OnClosed;
                _window.PositionChanged += OnPositionChanged;
                _window.Resized += OnResized;
                _window.ScalingChanged += OnScalingChanged;
                _window.PropertyChanged += OnWindowPropertyChanged;
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;

                _window.Opened -= OnOpened;
                _window.Closed -= OnClosed;
                _window.PositionChanged -= OnPositionChanged;
                _window.Resized -= OnResized;
                _window.ScalingChanged -= OnScalingChanged;
                _window.PropertyChanged -= OnWindowPropertyChanged;

                SubscribeToScreens(null);
            }

            private void OnOpened(object? sender, EventArgs e)
            {
                _isOpened = true;
                SubscribeToScreens(_window.Screens);
                ScheduleAdjustment();
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                Dispose();
            }

            private void OnScreensChanged(object? sender, EventArgs e)
            {
                ScheduleAdjustment();
            }

            private void OnScalingChanged(object? sender, EventArgs e)
            {
                ScheduleAdjustment();
            }

            private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Window.WindowStateProperty)
                {
                    ScheduleAdjustment();
                }
            }

            private void OnPositionChanged(object? sender, PixelPointEventArgs e)
            {
                if (!_isOpened || _isAdjusting || _window.WindowState != WindowState.Normal)
                {
                    return;
                }

                var screen = ResolveScreen();
                if (screen is null)
                {
                    return;
                }

                if (!ReferenceEquals(screen, _lastScreen) || !IsFrameInsideWorkingArea(screen))
                {
                    _lastScreen = screen;
                    ScheduleAdjustment();
                }
            }

            private void OnResized(object? sender, WindowResizedEventArgs e)
            {
                if (!_isOpened || _isAdjusting || _window.WindowState == WindowState.Minimized)
                {
                    return;
                }

                ScheduleAdjustment();
            }

            private void ScheduleAdjustment()
            {
                if (_isDisposed || _adjustmentQueued)
                {
                    return;
                }

                _adjustmentQueued = true;

                Dispatcher.UIThread.Post(() =>
                {
                    _adjustmentQueued = false;

                    if (_isDisposed)
                    {
                        return;
                    }

                    EnsureWindowFitsWorkingArea();
                }, DispatcherPriority.Background);
            }

            private void EnsureWindowFitsWorkingArea()
            {
                if (!_isOpened || _window.WindowState == WindowState.Minimized)
                {
                    return;
                }

                SubscribeToScreens(_window.Screens);

                var screen = ResolveScreen();
                if (screen is null)
                {
                    return;
                }

                var metrics = ScreenMetrics.Create(_window, screen, _initialMinWidth, _initialMinHeight);
                _lastScreen = screen;

                _isAdjusting = true;
                try
                {
                    ApplyDynamicMinimums(metrics);

                    if (_window.WindowState != WindowState.Normal)
                    {
                        return;
                    }

                    var targetClientWidth = Math.Clamp(GetCurrentClientWidth(), metrics.MinWidth, metrics.MaxClientWidth);
                    var targetClientHeight = Math.Clamp(GetCurrentClientHeight(), metrics.MinHeight, metrics.MaxClientHeight);

                    if (!NearlyEqual(_window.Width, targetClientWidth))
                    {
                        _window.Width = targetClientWidth;
                    }

                    if (!NearlyEqual(_window.Height, targetClientHeight))
                    {
                        _window.Height = targetClientHeight;
                    }

                    var targetPosition = ClampToWorkingArea(screen, metrics, targetClientWidth, targetClientHeight);
                    if (_window.Position != targetPosition)
                    {
                        _window.WindowStartupLocation = WindowStartupLocation.Manual;
                        _window.Position = targetPosition;
                    }
                }
                finally
                {
                    _isAdjusting = false;
                }
            }

            private void ApplyDynamicMinimums(ScreenMetrics metrics)
            {
                if (!NearlyEqual(_window.MinWidth, metrics.MinWidth))
                {
                    _window.MinWidth = metrics.MinWidth;
                }

                if (!NearlyEqual(_window.MinHeight, metrics.MinHeight))
                {
                    _window.MinHeight = metrics.MinHeight;
                }
            }

            private PixelPoint ClampToWorkingArea(
                Screen screen,
                ScreenMetrics metrics,
                double clientWidthDip,
                double clientHeightDip)
            {
                var workingArea = screen.WorkingArea;
                var frameWidthPx = DipToPixels(clientWidthDip + metrics.DecorationWidthDip, metrics.Scaling);
                var frameHeightPx = DipToPixels(clientHeightDip + metrics.DecorationHeightDip, metrics.Scaling);

                var maxX = workingArea.Right - frameWidthPx;
                var maxY = workingArea.Bottom - frameHeightPx;

                var x = maxX < workingArea.X
                    ? workingArea.X
                    : Math.Clamp(_window.Position.X, workingArea.X, maxX);

                var y = maxY < workingArea.Y
                    ? workingArea.Y
                    : Math.Clamp(_window.Position.Y, workingArea.Y, maxY);

                return new PixelPoint(x, y);
            }

            private bool IsFrameInsideWorkingArea(Screen screen)
            {
                var workingArea = screen.WorkingArea;
                var metrics = ScreenMetrics.Create(_window, screen, _initialMinWidth, _initialMinHeight);
                var frameWidthPx = DipToPixels(GetCurrentClientWidth() + metrics.DecorationWidthDip, metrics.Scaling);
                var frameHeightPx = DipToPixels(GetCurrentClientHeight() + metrics.DecorationHeightDip, metrics.Scaling);

                return _window.Position.X >= workingArea.X
                    && _window.Position.Y >= workingArea.Y
                    && _window.Position.X + frameWidthPx <= workingArea.Right
                    && _window.Position.Y + frameHeightPx <= workingArea.Bottom;
            }

            private Screen? ResolveScreen()
            {
                var screens = _window.Screens;
                if (screens is null)
                {
                    return null;
                }

                var screen = screens.ScreenFromWindow(_window);
                if (screen is not null)
                {
                    return screen;
                }

                if (_window.Owner is Window owner)
                {
                    screen = owner.Screens?.ScreenFromWindow(owner);
                    if (screen is not null)
                    {
                        return screen;
                    }
                }

                var estimatedRect = GetEstimatedFrameRect(screens);
                screen = screens.ScreenFromBounds(estimatedRect);
                return screen
                    ?? screens.Primary
                    ?? screens.All.FirstOrDefault();
            }

            private PixelRect GetEstimatedFrameRect(Screens screens)
            {
                var screen = screens.Primary ?? screens.All.FirstOrDefault();
                var scaling = screen?.Scaling > 0 ? screen.Scaling : Math.Max(_window.RenderScaling, 1d);
                var clientWidthDip = GetCurrentClientWidth();
                var clientHeightDip = GetCurrentClientHeight();
                var decorationWidthDip = 0d;
                var decorationHeightDip = 0d;

                if (_window.FrameSize is { } frameSize)
                {
                    decorationWidthDip = Math.Max(0d, frameSize.Width - _window.ClientSize.Width);
                    decorationHeightDip = Math.Max(0d, frameSize.Height - _window.ClientSize.Height);
                }

                return new PixelRect(
                    _window.Position.X,
                    _window.Position.Y,
                    DipToPixels(clientWidthDip + decorationWidthDip, scaling),
                    DipToPixels(clientHeightDip + decorationHeightDip, scaling));
            }

            private double GetCurrentClientWidth()
            {
                if (_window.ClientSize.Width > 0)
                {
                    return _window.ClientSize.Width;
                }

                if (_window.Bounds.Width > 0)
                {
                    return _window.Bounds.Width;
                }

                return NormalizeSize(_window.Width, _initialWidth);
            }

            private double GetCurrentClientHeight()
            {
                if (_window.ClientSize.Height > 0)
                {
                    return _window.ClientSize.Height;
                }

                if (_window.Bounds.Height > 0)
                {
                    return _window.Bounds.Height;
                }

                return NormalizeSize(_window.Height, _initialHeight);
            }

            private void SubscribeToScreens(Screens? screens)
            {
                if (ReferenceEquals(_screens, screens))
                {
                    return;
                }

                if (_screens is not null)
                {
                    _screens.Changed -= OnScreensChanged;
                }

                _screens = screens;

                if (_screens is not null)
                {
                    _screens.Changed += OnScreensChanged;
                }
            }

            private static bool NearlyEqual(double left, double right)
            {
                return Math.Abs(left - right) < 0.5d;
            }

            private static double NormalizeSize(double value, double fallback)
            {
                return !double.IsNaN(value) && value > 0
                    ? value
                    : fallback;
            }

            private static double NormalizeMinimum(double minValue, double sizeValue, double fallback)
            {
                if (!double.IsNaN(minValue) && minValue > 0)
                {
                    return minValue;
                }

                return !double.IsNaN(sizeValue) && sizeValue > 0
                    ? Math.Min(sizeValue, fallback)
                    : fallback;
            }

            private static int DipToPixels(double dip, double scaling)
            {
                var safeScaling = scaling > 0 ? scaling : 1d;
                return Math.Max(1, (int)Math.Ceiling(dip * safeScaling));
            }
        }

        private readonly record struct ScreenMetrics(
            PixelRect WorkingArea,
            double Scaling,
            double DecorationWidthDip,
            double DecorationHeightDip,
            double MinWidth,
            double MinHeight,
            double MaxClientWidth,
            double MaxClientHeight)
        {
            public static ScreenMetrics Create(Window window, Screen screen, double initialMinWidth, double initialMinHeight)
            {
                var scaling = screen.Scaling > 0 ? screen.Scaling : Math.Max(window.RenderScaling, 1d);
                var frameSize = window.FrameSize;
                var decorationWidthDip = frameSize is { } actualFrame
                    ? Math.Max(0d, actualFrame.Width - window.ClientSize.Width)
                    : 0d;
                var decorationHeightDip = frameSize is { } actualFrameSize
                    ? Math.Max(0d, actualFrameSize.Height - window.ClientSize.Height)
                    : 0d;

                var maxClientWidth = Math.Max(1d, Math.Floor(screen.WorkingArea.Width / scaling) - decorationWidthDip);
                var maxClientHeight = Math.Max(1d, Math.Floor(screen.WorkingArea.Height / scaling) - decorationHeightDip);
                var minWidth = Math.Min(initialMinWidth, maxClientWidth);
                var minHeight = Math.Min(initialMinHeight, maxClientHeight);

                return new ScreenMetrics(
                    screen.WorkingArea,
                    scaling,
                    decorationWidthDip,
                    decorationHeightDip,
                    minWidth,
                    minHeight,
                    maxClientWidth,
                    maxClientHeight);
            }
        }
    }
}
