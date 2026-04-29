using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.Services;

namespace Imvix.Views
{
    public partial class ConfirmationDialogWindow : Window
    {
        public ConfirmationDialogWindow()
        {
            InitializeComponent();
            WindowWorkAreaAdapter.Attach(this);
        }

        public ConfirmationDialogWindow(string title, string message, string confirmText, string cancelText)
            : this()
        {
            Title = title;
            MessageText.Text = message;
            ConfirmButtonText.Text = confirmText;
            CancelButtonText.Text = cancelText;
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
