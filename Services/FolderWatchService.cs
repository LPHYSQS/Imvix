using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ImvixPro.Services
{
    public sealed class FolderWatchService : IDisposable
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FileReadyState> _lastSignaledFiles = new(StringComparer.OrdinalIgnoreCase);

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
            _lastSignaledFiles.Clear();
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
            FileReadyState? previousReadyState = null;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.2), cancellation.Token);

                for (var attempt = 0; attempt < 12; attempt++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();

                    if (TryGetReadyState(path, out var currentReadyState))
                    {
                        if (previousReadyState.HasValue && previousReadyState.Value.Equals(currentReadyState))
                        {
                            if (TryRememberSignaledState(path, currentReadyState))
                            {
                                FileReady?.Invoke(this, path);
                            }

                            return;
                        }

                        previousReadyState = currentReadyState;
                    }
                    else
                    {
                        previousReadyState = null;
                    }

                    if (attempt == 11)
                    {
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

        private static bool TryGetReadyState(string path, out FileReadyState state)
        {
            state = default;

            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                state = new FileReadyState(stream.Length, File.GetLastWriteTimeUtc(path));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryRememberSignaledState(string path, FileReadyState state)
        {
            while (true)
            {
                if (_lastSignaledFiles.TryGetValue(path, out var existing))
                {
                    if (existing.Equals(state))
                    {
                        return false;
                    }

                    if (_lastSignaledFiles.TryUpdate(path, state, existing))
                    {
                        return true;
                    }

                    continue;
                }

                if (_lastSignaledFiles.TryAdd(path, state))
                {
                    return true;
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private readonly record struct FileReadyState(long Length, DateTime LastWriteTimeUtc);
    }
}
