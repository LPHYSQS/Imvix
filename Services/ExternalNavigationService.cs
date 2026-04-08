using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Imvix.Services
{
    public static class ExternalNavigationService
    {
        public static void Open(string target)
        {
            TryOpen(target);
        }

        public static void OpenOrFallback(string primaryTarget, string fallbackTarget)
        {
            if (CanOpenTarget(primaryTarget) && TryOpen(primaryTarget))
            {
                return;
            }

            if (!string.Equals(primaryTarget, fallbackTarget, StringComparison.OrdinalIgnoreCase))
            {
                TryOpen(fallbackTarget);
            }
        }

        public static bool TryOpen(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                    return true;
                }

                if (OperatingSystem.IsMacOS())
                {
                    return Process.Start("open", target) is not null;
                }

                if (OperatingSystem.IsLinux())
                {
                    return Process.Start("xdg-open", target) is not null;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool CanOpenTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                return true;
            }

            if (!string.Equals(uri.Scheme, "ms-windows-store", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return HasProtocolHandler(uri.Scheme);
        }

        private static bool HasProtocolHandler(string scheme)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(scheme))
            {
                return false;
            }

            try
            {
                using var currentUserKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}");
                if (currentUserKey is not null)
                {
                    return true;
                }

                using var classesRootKey = Registry.ClassesRoot.OpenSubKey(scheme);
                return classesRootKey is not null;
            }
            catch
            {
                return false;
            }
        }
    }
}
