using System;
using System.IO;

namespace ImvixPro
{
    internal static class AppIdentity
    {
        public const string DisplayName = "Imvix Pro";
        public const string InternalName = "ImvixPro";
        public const string ShortcutName = DisplayName;

        private const string LegacyDataDirectoryName = "Imvix";
        private const string DataDirectoryName = DisplayName;

        public static Uri GetAssetUri(string relativePath)
        {
            var assemblyName = typeof(AppIdentity).Assembly.GetName().Name ?? DisplayName;
            var normalizedPath = relativePath.TrimStart('/', '\\').Replace('\\', '/');
            return new Uri($"avares://{Uri.EscapeDataString(assemblyName)}/{normalizedPath}");
        }

        public static string GetAppDataDirectory()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var currentPath = Path.Combine(appDataPath, DataDirectoryName);
            var legacyPath = Path.Combine(appDataPath, LegacyDataDirectoryName);

            if (!Directory.Exists(currentPath) && Directory.Exists(legacyPath))
            {
                TryMigrateLegacyDirectory(legacyPath, currentPath);
            }

            Directory.CreateDirectory(currentPath);
            return currentPath;
        }

        private static void TryMigrateLegacyDirectory(string legacyPath, string currentPath)
        {
            try
            {
                Directory.Move(legacyPath, currentPath);
                return;
            }
            catch
            {
            }

            try
            {
                Directory.CreateDirectory(currentPath);

                foreach (var directory in Directory.EnumerateDirectories(legacyPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(legacyPath, directory);
                    Directory.CreateDirectory(Path.Combine(currentPath, relativePath));
                }

                foreach (var file in Directory.EnumerateFiles(legacyPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(legacyPath, file);
                    var destinationPath = Path.Combine(currentPath, relativePath);
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    if (!File.Exists(destinationPath))
                    {
                        File.Copy(file, destinationPath);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
