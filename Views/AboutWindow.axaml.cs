using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.Services;
using Imvix.ViewModels;

namespace Imvix.Views
{
    public partial class AboutWindow : Window
    {
        private const string OfficialWebsiteUrl = "https://lphysqs.github.io/ImvixWeb/";
        private const string RepositoryUrl = "https://github.com/LPHYSQS/Imvix";

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

        private void OnOpenOfficialWebsiteClick(object? sender, RoutedEventArgs e)
        {
            ExternalNavigationService.Open(OfficialWebsiteUrl);
        }

        private void OnOpenRepositoryClick(object? sender, RoutedEventArgs e)
        {
            ExternalNavigationService.Open(RepositoryUrl);
        }
    }
}
