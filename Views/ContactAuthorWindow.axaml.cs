using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ImvixPro.ViewModels;
using System.Threading.Tasks;

namespace ImvixPro.Views
{
    public partial class ContactAuthorWindow : Window
    {
        public ContactAuthorWindow()
        {
            InitializeComponent();
        }

        public ContactAuthorWindow(MainWindowViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
            Title = viewModel.ContactAuthorTitleText;
        }

        private async void OnCopyEmailClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var email = vm.ContactAuthorEmailText;
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(email);
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
