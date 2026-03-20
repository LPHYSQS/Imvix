using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace ImvixPro.Views.Controls
{
    public partial class PageRangeSelector : UserControl
    {
        private const double ThumbWidth = 14d;
        private const double ThumbHeight = 24d;
        private const double TrackHeight = 6d;
        private ActiveThumb _activeThumb = ActiveThumb.None;

        public static readonly StyledProperty<int> MinimumProperty =
            AvaloniaProperty.Register<PageRangeSelector, int>(nameof(Minimum), 0);

        public static readonly StyledProperty<int> MaximumProperty =
            AvaloniaProperty.Register<PageRangeSelector, int>(nameof(Maximum), 1);

        public static readonly StyledProperty<int> StartValueProperty =
            AvaloniaProperty.Register<PageRangeSelector, int>(nameof(StartValue), 0);

        public static readonly StyledProperty<int> EndValueProperty =
            AvaloniaProperty.Register<PageRangeSelector, int>(nameof(EndValue), 0);

        public event EventHandler<PageRangeChangedEventArgs>? RangeChanged;

        public PageRangeSelector()
        {
            InitializeComponent();

            PropertyChanged += OnSelectorPropertyChanged;
            SizeChanged += (_, _) => UpdateVisuals();
        }

        public int Minimum
        {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public int Maximum
        {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public int StartValue
        {
            get => GetValue(StartValueProperty);
            set => SetValue(StartValueProperty, value);
        }

        public int EndValue
        {
            get => GetValue(EndValueProperty);
            set => SetValue(EndValueProperty, value);
        }

        private void OnStartThumbPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsEnabled)
            {
                return;
            }

            _activeThumb = ActiveThumb.Start;
            e.Pointer.Capture(TrackCanvas);
            e.Handled = true;
        }

        private void OnEndThumbPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsEnabled)
            {
                return;
            }

            _activeThumb = ActiveThumb.End;
            e.Pointer.Capture(TrackCanvas);
            e.Handled = true;
        }

        private void OnTrackPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_activeThumb == ActiveThumb.None || !ReferenceEquals(e.Pointer.Captured, TrackCanvas))
            {
                return;
            }

            var trackWidth = Math.Max(1d, TrackCanvas.Bounds.Width - ThumbWidth);
            var position = e.GetPosition(TrackCanvas).X;
            var normalized = Math.Clamp((position - ThumbWidth / 2d) / trackWidth, 0d, 1d);
            var value = ValueFromNormalized(normalized);

            switch (_activeThumb)
            {
                case ActiveThumb.Start:
                    value = Math.Min(value, EndValue);
                    if (value != StartValue)
                    {
                        SetCurrentValue(StartValueProperty, value);
                        RangeChanged?.Invoke(this, new PageRangeChangedEventArgs(StartValue, EndValue));
                    }
                    break;
                case ActiveThumb.End:
                    value = Math.Max(value, StartValue);
                    if (value != EndValue)
                    {
                        SetCurrentValue(EndValueProperty, value);
                        RangeChanged?.Invoke(this, new PageRangeChangedEventArgs(StartValue, EndValue));
                    }
                    break;
            }

            e.Handled = true;
        }

        private void OnTrackPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer.Captured?.Equals(TrackCanvas) == true)
            {
                e.Pointer.Capture(null);
            }

            _activeThumb = ActiveThumb.None;
        }

        private void OnTrackPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _activeThumb = ActiveThumb.None;
        }

        private void OnSelectorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == MinimumProperty ||
                e.Property == MaximumProperty ||
                e.Property == StartValueProperty ||
                e.Property == EndValueProperty)
            {
                UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            if (TrackCanvas is null)
            {
                return;
            }

            var minimum = Minimum;
            var maximum = Math.Max(minimum, Maximum);
            var start = Math.Clamp(StartValue, minimum, maximum);
            var end = Math.Clamp(EndValue, start, maximum);

            if (start != StartValue)
            {
                SetCurrentValue(StartValueProperty, start);
            }

            if (end != EndValue)
            {
                SetCurrentValue(EndValueProperty, end);
            }

            var width = Math.Max(ThumbWidth, TrackCanvas.Bounds.Width);
            TrackBackground.Width = width - ThumbWidth;
            TrackBackground.Height = TrackHeight;
            Canvas.SetLeft(TrackBackground, ThumbWidth / 2d);
            Canvas.SetTop(TrackBackground, (TrackCanvas.Bounds.Height - TrackHeight) / 2d);

            var startX = PositionFromValue(start, minimum, maximum);
            var endX = PositionFromValue(end, minimum, maximum);

            SelectionRange.Width = Math.Max(0d, endX - startX);
            SelectionRange.Height = TrackHeight;
            Canvas.SetLeft(SelectionRange, startX);
            Canvas.SetTop(SelectionRange, (TrackCanvas.Bounds.Height - TrackHeight) / 2d);

            StartThumb.Width = ThumbWidth;
            StartThumb.Height = ThumbHeight;
            Canvas.SetLeft(StartThumb, startX - ThumbWidth / 2d);
            Canvas.SetTop(StartThumb, (TrackCanvas.Bounds.Height - ThumbHeight) / 2d);

            EndThumb.Width = ThumbWidth;
            EndThumb.Height = ThumbHeight;
            Canvas.SetLeft(EndThumb, endX - ThumbWidth / 2d);
            Canvas.SetTop(EndThumb, (TrackCanvas.Bounds.Height - ThumbHeight) / 2d);
        }

        private double PositionFromValue(int value, int minimum, int maximum)
        {
            var width = Math.Max(1d, TrackCanvas.Bounds.Width - ThumbWidth);
            if (maximum <= minimum)
            {
                return ThumbWidth / 2d;
            }

            var ratio = (value - minimum) / (double)(maximum - minimum);
            return ThumbWidth / 2d + (width * ratio);
        }

        private int ValueFromNormalized(double normalized)
        {
            var minimum = Minimum;
            var maximum = Math.Max(minimum, Maximum);
            if (maximum <= minimum)
            {
                return minimum;
            }

            var value = minimum + (normalized * (maximum - minimum));
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private enum ActiveThumb
        {
            None,
            Start,
            End
        }
    }

    public sealed class PageRangeChangedEventArgs : EventArgs
    {
        public PageRangeChangedEventArgs(int startValue, int endValue)
        {
            StartValue = startValue;
            EndValue = endValue;
        }

        public int StartValue { get; }

        public int EndValue { get; }
    }
}
