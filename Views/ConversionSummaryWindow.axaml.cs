using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ImvixPro.Views
{
    public partial class ConversionSummaryWindow : Window
    {
        public ConversionSummaryWindow()
        {
            InitializeComponent();
        }

        public ConversionSummaryWindow(string title, string summary, string closeButtonText)
            : this()
        {
            Title = title;
            SummaryText.Text = summary;
            CloseButtonText.Text = closeButtonText;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
