using Avalonia;
using Imvix.Services;
using System;
using System.Diagnostics;

namespace Imvix
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Let debugger-launched runs open normally even if another instance exists.
            if (Debugger.IsAttached)
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                return;
            }

            using var singleInstance = new SingleInstanceService("Imvix");
            if (!singleInstance.IsFirstInstance)
            {
                singleInstance.SignalExistingInstance();
                return;
            }

            App.SingleInstance = singleInstance;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
