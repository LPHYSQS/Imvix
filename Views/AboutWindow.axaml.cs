using Avalonia.Controls;
using Avalonia.Interactivity;
using ImvixPro.ViewModels;
using System;
using System.Diagnostics;

namespace ImvixPro.Views
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
            OpenExternalUrl(OfficialWebsiteUrl);
        }

        private void OnOpenRepositoryClick(object? sender, RoutedEventArgs e)
        {
            OpenExternalUrl(RepositoryUrl);
        }

        private static void OpenExternalUrl(string url)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    return;
                }

                if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", url);
                    return;
                }

                if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", url);
                }
            }
            catch
            {
            }
        }
    }
}
