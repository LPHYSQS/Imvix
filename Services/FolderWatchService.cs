using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imvix.Services
{
    public sealed class FolderWatchService : IDisposable
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher? _watcher;

        public event EventHandler<string>? FileReady;

        public bool IsRunning => _watcher?.EnableRaisingEvents == true;

        public string WatchedDirectory { get; private set; } = string.Empty;

        public void Start(string inputDirectory, bool includeSubfolders)
        {
            Stop();

            var fullPath = Path.GetFullPath(inputDirectory);
            _watcher = new FileSystemWatcher(fullPath)
            {
                IncludeSubdirectories = includeSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _watcher.Created += OnWatcherChanged;
            _watcher.Changed += OnWatcherChanged;
            _watcher.Renamed += OnWatcherRenamed;
            WatchedDirectory = fullPath;
        }

        public void Stop()
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnWatcherChanged;
                _watcher.Changed -= OnWatcherChanged;
                _watcher.Renamed -= OnWatcherRenamed;
                _watcher.Dispose();
                _watcher = null;
            }

            foreach (var pair in _pendingFiles)
            {
                pair.Value.Cancel();
                pair.Value.Dispose();
            }

            _pendingFiles.Clear();
            WatchedDirectory = string.Empty;
        }

        private void OnWatcherChanged(object sender, FileSystemEventArgs e)
        {
            QueueFile(e.FullPath);
        }

        private void OnWatcherRenamed(object sender, RenamedEventArgs e)
        {
            QueueFile(e.FullPath);
        }

        private void QueueFile(string path)
        {
            var extension = Path.GetExtension(path);
            if (!ImageConversionService.SupportedInputExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var cancellation = new CancellationTokenSource();
            var current = _pendingFiles.AddOrUpdate(
                fullPath,
                cancellation,
                (_, existing) =>
                {
                    existing.Cancel();
                    existing.Dispose();
                    return cancellation;
                });

            if (!ReferenceEquals(current, cancellation))
            {
                cancellation.Dispose();
                return;
            }

            _ = WaitForReadyAsync(fullPath, cancellation);
        }

        private async Task WaitForReadyAsync(string path, CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.2), cancellation.Token);

                for (var attempt = 0; attempt < 10; attempt++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();

                    if (File.Exists(path) && CanOpenForRead(path))
                    {
                        if (_pendingFiles.TryRemove(path, out var pending))
                        {
                            pending.Dispose();
                        }

                        FileReady?.Invoke(this, path);
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore debounced updates for the same file.
            }
            finally
            {
                if (_pendingFiles.TryGetValue(path, out var current) && ReferenceEquals(current, cancellation))
                {
                    _pendingFiles.TryRemove(path, out _);
                }

                cancellation.Dispose();
            }
        }

        private static bool CanOpenForRead(string path)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return stream.Length >= 0;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}


