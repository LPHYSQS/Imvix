using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.Services;

namespace Imvix.Views
{
    public partial class RunningInstanceWarningWindow : Window
    {
        public RunningInstanceWarningWindow()
        {
            InitializeComponent();
            WindowWorkAreaAdapter.Attach(this);
        }

        public RunningInstanceWarningWindow(string title, string message, string closeText)
            : this()
        {
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            CloseButtonText.Text = closeText;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
