using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.ViewModels;

namespace Imvix.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        public AboutWindow(MainWindowViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
            Title = viewModel.AboutWindowTitleText;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
