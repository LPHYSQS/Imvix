using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ImvixPro.Services;
using ImvixPro.ViewModels;
using ImvixPro.Views;
using System.Linq;

namespace ImvixPro
{
    public partial class App : Application
    {
        public static SingleInstanceService? SingleInstance { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                DisableAvaloniaDataAnnotationValidation();

                var mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };

                mainWindow.SetStartupPaths(desktop.Args);
                desktop.MainWindow = mainWindow;

                if (SingleInstance is not null)
                {
                    void HandleActivation()
                    {
                        Dispatcher.UIThread.Post(() => _ = mainWindow.HandleSecondInstanceActivationAsync());
                    }

                    SingleInstance.ActivationRequested += HandleActivation;

                    if (SingleInstance.ConsumePendingActivation())
                    {
                        HandleActivation();
                    }
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}
