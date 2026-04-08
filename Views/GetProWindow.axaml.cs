using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.Services;
using Imvix.ViewModels;
using System;

namespace Imvix.Views
{
    public partial class GetProWindow : Window
    {
        private const string OfficialWebsiteUrl = "https://lphysqs.github.io/ImvixWeb/";
        private const string ProStoreUrl = "ms-windows-store://pdp/?productid=9P0NZSF11CS6";

        public GetProWindow()
        {
            InitializeComponent();
        }

        public GetProWindow(MainWindowViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
            Title = viewModel.GetProWindowTitleText;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnOpenMicrosoftStoreClick(object? sender, RoutedEventArgs e)
        {
            if (OperatingSystem.IsWindows())
            {
                ExternalNavigationService.OpenOrFallback(ProStoreUrl, OfficialWebsiteUrl);
                return;
            }

            ExternalNavigationService.Open(OfficialWebsiteUrl);
        }
    }
}
