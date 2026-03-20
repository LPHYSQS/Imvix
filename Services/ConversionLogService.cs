using ImvixPro.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ImvixPro.Services
{
    public sealed class ConversionLogService
    {
        private readonly string _logDirectory;

        public ConversionLogService()
        {
            _logDirectory = Path.Combine(AppIdentity.GetAppDataDirectory(), "Logs");
            Directory.CreateDirectory(_logDirectory);
        }

        public string? WriteFailureLog(
            ConversionSummary summary,
            ConversionOptions options,
            IReadOnlyList<ImageItemViewModel> images,
            ConversionTriggerSource source)
        {
            if (summary.FailureCount == 0)
            {
                return null;
            }

            var timestamp = DateTimeOffset.Now;
            var logPath = Path.Combine(
                _logDirectory,
                $"conversion-{timestamp:yyyyMMdd-HHmmss}.log");

            var builder = new StringBuilder();
            builder.AppendLine($"{AppIdentity.DisplayName} Conversion Error Log");
            builder.AppendLine($"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss zzz}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"Output Format: {options.OutputFormat}");
            builder.AppendLine($"Compression Mode: {options.CompressionMode}");
            builder.AppendLine($"Quality: {options.Quality}");
            builder.AppendLine($"Resize Mode: {options.ResizeMode}");
            builder.AppendLine($"GIF Handling: {options.GifHandlingMode}");
            builder.AppendLine($"Total Files: {summary.TotalCount}");
            builder.AppendLine($"Processed Files: {summary.ProcessedCount}");
            builder.AppendLine($"Succeeded: {summary.SuccessCount}");
            builder.AppendLine($"Failed: {summary.FailureCount}");
            builder.AppendLine($"Canceled: {summary.WasCanceled}");
            builder.AppendLine($"Duration: {summary.Duration.ToString("c", CultureInfo.InvariantCulture)}");
            builder.AppendLine();
            builder.AppendLine("Selected Files:");

            foreach (var image in images)
            {
                builder.AppendLine($"- {image.FilePath}");
            }

            builder.AppendLine();
            builder.AppendLine("Failures:");
            foreach (var failure in summary.Failures.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {failure.FileName}: {failure.Reason}");
            }

            File.WriteAllText(logPath, builder.ToString(), Encoding.UTF8);
            return logPath;
        }
    }
}



