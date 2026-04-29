using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.Services;

namespace Imvix.Views
{
    public partial class ConversionSummaryWindow : Window
    {
        public ConversionSummaryWindow()
        {
            InitializeComponent();
            WindowWorkAreaAdapter.Attach(this);
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
