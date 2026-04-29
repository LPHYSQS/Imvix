using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Imvix.Services
{
    public sealed class SystemIntegrationService
    {
        private const string AppShortcutName = "Imvix";
        private const string ShortcutExtension = ".lnk";

        public bool SetRunOnStartup(bool enabled)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var shortcutPath = GetStartupShortcutPath();
            if (string.IsNullOrWhiteSpace(shortcutPath))
            {
                return false;
            }

            if (!enabled)
            {
                TryDelete(shortcutPath);
                return true;
            }

            return TryCreateShortcut(shortcutPath);
        }

        public bool CreateDesktopShortcut()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var shortcutPath = GetDesktopShortcutPath();
            if (string.IsNullOrWhiteSpace(shortcutPath))
            {
                return false;
            }

            return TryCreateShortcut(shortcutPath);
        }

        public bool DesktopShortcutExists()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var shortcutPath = GetDesktopShortcutPath();
            if (string.IsNullOrWhiteSpace(shortcutPath))
            {
                return false;
            }

            return File.Exists(shortcutPath);
        }

        private static string GetStartupShortcutPath()
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(startup))
            {
                return string.Empty;
            }

            return Path.Combine(startup, $"{AppShortcutName}{ShortcutExtension}");
        }

        private static string GetDesktopShortcutPath()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                return string.Empty;
            }

            return Path.Combine(desktop, $"{AppShortcutName}{ShortcutExtension}");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore shortcut deletion failures.
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool TryCreateShortcut(string shortcutPath)
        {
            if (!TryResolveAppLaunch(out var targetPath, out var arguments, out var workingDirectory, out var iconPath))
            {
                return false;
            }

            try
            {
                return TryCreateWindowsShortcut(shortcutPath, targetPath, arguments, workingDirectory, iconPath);
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool TryCreateWindowsShortcut(
            string shortcutPath,
            string targetPath,
            string arguments,
            string workingDirectory,
            string iconPath)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                shortcut.Arguments = arguments;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                shortcut.WorkingDirectory = workingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                shortcut.IconLocation = iconPath;
            }

            shortcut.Save();
            return true;
        }

        private static bool TryResolveAppLaunch(
            out string targetPath,
            out string arguments,
            out string workingDirectory,
            out string iconPath)
        {
            targetPath = string.Empty;
            arguments = string.Empty;
            workingDirectory = string.Empty;
            iconPath = string.Empty;

            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath) && !IsDotNetHost(processPath))
            {
                targetPath = processPath;
                workingDirectory = Path.GetDirectoryName(processPath) ?? string.Empty;
                iconPath = processPath;
                return true;
            }

            var entryLocation = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryLocation) && File.Exists(entryLocation))
            {
                if (entryLocation.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = entryLocation;
                    workingDirectory = Path.GetDirectoryName(entryLocation) ?? string.Empty;
                    iconPath = entryLocation;
                    return true;
                }

                if (entryLocation.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var exeCandidate = Path.ChangeExtension(entryLocation, ".exe");
                    if (!string.IsNullOrWhiteSpace(exeCandidate) && File.Exists(exeCandidate))
                    {
                        targetPath = exeCandidate;
                        workingDirectory = Path.GetDirectoryName(exeCandidate) ?? string.Empty;
                        iconPath = exeCandidate;
                        return true;
                    }

                    var dotnetPath = ResolveDotNetHostPath(processPath);
                    if (!string.IsNullOrWhiteSpace(dotnetPath))
                    {
                        targetPath = dotnetPath;
                        arguments = $"\"{entryLocation}\"";
                        workingDirectory = Path.GetDirectoryName(entryLocation) ?? string.Empty;
                        iconPath = !string.IsNullOrWhiteSpace(exeCandidate) && File.Exists(exeCandidate)
                            ? exeCandidate
                            : dotnetPath;
                        return true;
                    }
                }
            }

            var modulePath = ResolveProcessModulePath();
            if (!string.IsNullOrWhiteSpace(modulePath) && File.Exists(modulePath))
            {
                targetPath = modulePath;
                workingDirectory = Path.GetDirectoryName(modulePath) ?? string.Empty;
                iconPath = modulePath;
                return true;
            }

            return false;
        }

        private static bool IsDotNetHost(string path)
        {
            var name = Path.GetFileName(path);
            return name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveDotNetHostPath(string? fallback)
        {
            if (!string.IsNullOrWhiteSpace(fallback) && File.Exists(fallback) && IsDotNetHost(fallback))
            {
                return fallback;
            }

            try
            {
                var module = Process.GetCurrentProcess().MainModule;
                if (module is not null && !string.IsNullOrWhiteSpace(module.FileName) && IsDotNetHost(module.FileName))
                {
                    return module.FileName;
                }
            }
            catch
            {
                // Ignore process module lookups that fail in restricted contexts.
            }

            return string.Empty;
        }

        private static string ResolveProcessModulePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
