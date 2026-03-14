﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Avalonia.Media.Imaging;
using Imvix.Models;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Drawing.Imaging;
using System.Runtime.Serialization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imvix.Services
{
    public sealed class ImageConversionService
    {
        private const int GifPreviewCacheLimit = 6;
        private static readonly object GifPreviewCacheGate = new();
        private static readonly Dictionary<string, GifPreviewCacheEntry> GifPreviewCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Task<GifPreviewCacheEntry?>> GifPreviewLoads = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim GifPreviewDecodeGate = new(2, 2);
        private static readonly Dictionary<OutputImageFormat, string> Extensions = new()
        {
            [OutputImageFormat.Png] = ".png",
            [OutputImageFormat.Jpeg] = ".jpg",
            [OutputImageFormat.Webp] = ".webp",
            [OutputImageFormat.Bmp] = ".bmp",
            [OutputImageFormat.Gif] = ".gif",
            [OutputImageFormat.Tiff] = ".tiff",
            [OutputImageFormat.Ico] = ".ico",
            [OutputImageFormat.Svg] = ".svg"
        };

        public static IReadOnlyCollection<string> SupportedInputExtensions { get; } =
        [
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".svg"
        ];

        public static bool TryGetCachedGifPreview(string filePath, int maxWidth, [NotNullWhen(true)] out GifPreviewHandle? handle)
        {
            handle = null;
            if (!IsGifFile(filePath))
            {
                return false;
            }

            var cacheKey = BuildGifPreviewCacheKey(filePath, maxWidth);
            lock (GifPreviewCacheGate)
            {
                if (!GifPreviewCache.TryGetValue(cacheKey, out var entry))
                {
                    return false;
                }

                entry.RefCount++;
                entry.LastAccessUtc = DateTime.UtcNow;
                handle = new GifPreviewHandle(cacheKey, entry);
                return true;
            }
        }

        public static Task<GifPreviewHandle?> GetOrLoadGifPreviewAsync(string filePath, int maxWidth)
        {
            if (!IsGifFile(filePath))
            {
                return Task.FromResult<GifPreviewHandle?>(null);
            }

            if (TryGetCachedGifPreview(filePath, maxWidth, out var cachedHandle))
            {
                return Task.FromResult<GifPreviewHandle?>(cachedHandle);
            }

            return GetOrLoadGifPreviewCoreAsync(filePath, maxWidth);
        }

        public static void WarmGifPreview(string filePath, int maxWidth)
        {
            if (!IsGifFile(filePath))
            {
                return;
            }

            var cacheKey = BuildGifPreviewCacheKey(filePath, maxWidth);
            lock (GifPreviewCacheGate)
            {
                if (GifPreviewCache.ContainsKey(cacheKey) || GifPreviewLoads.ContainsKey(cacheKey))
                {
                    return;
                }
            }

            var loader = GetOrCreateGifPreviewLoader(cacheKey, filePath, maxWidth);
            _ = loader.ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion || task.Result is null)
                {
                    lock (GifPreviewCacheGate)
                    {
                        GifPreviewLoads.Remove(cacheKey);
                    }

                    return;
                }

                lock (GifPreviewCacheGate)
                {
                    GifPreviewLoads.Remove(cacheKey);
                    if (!GifPreviewCache.ContainsKey(cacheKey))
                    {
                        GifPreviewCache[cacheKey] = task.Result;
                    }

                    task.Result.LastAccessUtc = DateTime.UtcNow;
                    TrimGifPreviewCache();
                }
            }, TaskScheduler.Default);
        }

        public static Bitmap? TryCreatePreview(string filePath, int maxWidth, bool svgUseBackground = false, string? svgBackgroundColor = null)
        {
            try
            {
                var extension = Path.GetExtension(filePath);
                if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    using var svgBitmap = DecodeSvgToBitmap(filePath, svgUseBackground, svgBackgroundColor);
                    return CreatePreviewFromBitmap(svgBitmap, maxWidth);
                }

                using var stream = File.OpenRead(filePath);
                return Bitmap.DecodeToWidth(stream, maxWidth);
            }
            catch
            {
                return null;
            }
        }

        public static bool TryLoadGifPreviewFrames(
            string filePath,
            int maxWidth,
            out List<Bitmap> frames,
            out List<TimeSpan> durations)
        {
            frames = [];
            durations = [];

            try
            {
                if (!Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);
                if (codec is null)
                {
                    return false;
                }

                var frameInfos = codec.FrameInfo;
                if (frameInfos.Length <= 1)
                {
                    return false;
                }

                var info = codec.Info;
                for (var i = 0; i < frameInfos.Length; i++)
                {
                    using var frameBitmap = new SKBitmap(info);
                    var decodeOptions = new SKCodecOptions(i)
                    {
                        PriorFrame = -1
                    };

                    var result = codec.GetPixels(info, frameBitmap.GetPixels(), decodeOptions);
                    if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                    {
                        DisposePreviewFrames(frames);
                        frames.Clear();
                        durations.Clear();
                        return false;
                    }

                    using var image = SKImage.FromBitmap(frameBitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    if (data is null)
                    {
                        DisposePreviewFrames(frames);
                        frames.Clear();
                        durations.Clear();
                        return false;
                    }

                    using var memory = new MemoryStream(data.ToArray());
                    frames.Add(Bitmap.DecodeToWidth(memory, maxWidth));

                    var duration = Math.Max(0, frameInfos[i].Duration);
                    durations.Add(TimeSpan.FromMilliseconds(duration));
                }

                return frames.Count > 0;
            }
            catch
            {
                DisposePreviewFrames(frames);
                frames.Clear();
                durations.Clear();
                return false;
            }
        }

        private static async Task<GifPreviewHandle?> GetOrLoadGifPreviewCoreAsync(string filePath, int maxWidth)
        {
            var cacheKey = BuildGifPreviewCacheKey(filePath, maxWidth);
            var loader = GetOrCreateGifPreviewLoader(cacheKey, filePath, maxWidth);
            GifPreviewCacheEntry? entry = null;
            try
            {
                entry = await loader.ConfigureAwait(false);
            }
            finally
            {
                lock (GifPreviewCacheGate)
                {
                    GifPreviewLoads.Remove(cacheKey);
                }
            }

            if (entry is null)
            {
                return null;
            }

            lock (GifPreviewCacheGate)
            {
                if (!GifPreviewCache.TryGetValue(cacheKey, out var cached))
                {
                    cached = entry;
                    GifPreviewCache[cacheKey] = cached;
                }

                cached.RefCount++;
                cached.LastAccessUtc = DateTime.UtcNow;
                TrimGifPreviewCache();
                return new GifPreviewHandle(cacheKey, cached);
            }
        }

        private static Task<GifPreviewCacheEntry?> GetOrCreateGifPreviewLoader(string cacheKey, string filePath, int maxWidth)
        {
            lock (GifPreviewCacheGate)
            {
                if (GifPreviewLoads.TryGetValue(cacheKey, out var existing))
                {
                    return existing;
                }

                var loader = Task.Run(async () =>
                {
                    await GifPreviewDecodeGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        return LoadGifPreviewEntry(filePath, maxWidth);
                    }
                    finally
                    {
                        GifPreviewDecodeGate.Release();
                    }
                });

                GifPreviewLoads[cacheKey] = loader;
                return loader;
            }
        }

        private static GifPreviewCacheEntry? LoadGifPreviewEntry(string filePath, int maxWidth)
        {
            var success = TryLoadGifPreviewFrames(filePath, maxWidth, out var frames, out var durations);
            if (!success || frames.Count == 0 || frames.Count != durations.Count)
            {
                DisposePreviewFrames(frames);
                return null;
            }

            return new GifPreviewCacheEntry(frames, durations);
        }

        private static void ReleaseGifPreview(string cacheKey, GifPreviewCacheEntry entry)
        {
            lock (GifPreviewCacheGate)
            {
                entry.RefCount = Math.Max(0, entry.RefCount - 1);
                entry.LastAccessUtc = DateTime.UtcNow;
                TrimGifPreviewCache();
            }
        }

        private static void TrimGifPreviewCache()
        {
            if (GifPreviewCache.Count <= GifPreviewCacheLimit)
            {
                return;
            }

            var candidates = GifPreviewCache
                .Where(static kvp => kvp.Value.RefCount == 0)
                .OrderBy(static kvp => kvp.Value.LastAccessUtc)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (GifPreviewCache.Count <= GifPreviewCacheLimit)
                {
                    break;
                }

                GifPreviewCache.Remove(candidate.Key);
                candidate.Value.Dispose();
            }
        }

        private static string BuildGifPreviewCacheKey(string filePath, int maxWidth)
        {
            return $"{filePath}|{maxWidth}";
        }

        private static bool IsGifFile(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        }

        public Task<ConversionSummary> ConvertAsync(
            IReadOnlyList<ImageItemViewModel> images,
            ConversionOptions options,
            IProgress<ConversionProgress>? progress,
            ConversionPauseController? pauseController = null,
            CancellationToken cancellationToken = default)
        {
            return ConvertInternalAsync(images, options, progress, pauseController, cancellationToken);
        }

        public Task<ConversionSummary> ConvertAsync(
            IReadOnlyList<ImageItemViewModel> images,
            OutputImageFormat outputFormat,
            int quality,
            string outputDirectory,
            bool useSourceFolder,
            bool allowOverwrite,
            bool svgUseBackground,
            string svgBackgroundColor,
            IProgress<ConversionProgress>? progress,
            ConversionPauseController? pauseController = null,
            CancellationToken cancellationToken = default)
        {
            var options = new ConversionOptions
            {
                OutputFormat = outputFormat,
                CompressionMode = CompressionMode.Custom,
                Quality = Math.Clamp(quality, 1, 100),
                ResizeMode = ResizeMode.None,
                RenameMode = RenameMode.KeepOriginal,
                OutputDirectoryRule = useSourceFolder ? OutputDirectoryRule.SourceFolder : OutputDirectoryRule.SpecificFolder,
                OutputDirectory = outputDirectory,
                AllowOverwrite = allowOverwrite,
                SvgUseBackground = svgUseBackground,
                SvgBackgroundColor = string.IsNullOrWhiteSpace(svgBackgroundColor) ? "#FFFFFFFF" : svgBackgroundColor,
                GifHandlingMode = GifHandlingMode.FirstFrame,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };

            return ConvertAsync(images, options, progress, pauseController, cancellationToken);
        }

        private static async Task<ConversionSummary> ConvertInternalAsync(
            IReadOnlyList<ImageItemViewModel> images,
            ConversionOptions options,
            IProgress<ConversionProgress>? progress,
            ConversionPauseController? pauseController,
            CancellationToken cancellationToken)
        {
            var normalized = NormalizeOptions(options);
            var stopwatch = Stopwatch.StartNew();

            var failures = new ConcurrentBag<ConversionFailure>();
            var outputFolders = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reservationGate = new object();

            var successCount = 0;
            var processedCount = 0;
            var wasCanceled = false;

            var workerCount = Math.Min(images.Count, Math.Max(1, normalized.MaxDegreeOfParallelism));
            if (workerCount == 0)
            {
                stopwatch.Stop();
                return new ConversionSummary(0, 0, 0, [], [], stopwatch.Elapsed);
            }

            var nextIndex = -1;
            var workers = Enumerable.Range(0, workerCount)
                .Select(_ => Task.Run(() =>
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pauseController?.WaitIfPaused(cancellationToken);

                        var index = Interlocked.Increment(ref nextIndex);
                        if (index >= images.Count)
                        {
                            break;
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        pauseController?.WaitIfPaused(cancellationToken);

                        var image = images[index];
                        var folder = ResolveOutputFolder(image.FilePath, normalized);
                        outputFolders.TryAdd(folder, 0);

                        var succeeded = false;
                        string? error = null;

                        try
                        {
                            Directory.CreateDirectory(folder);
                            cancellationToken.ThrowIfCancellationRequested();
                            pauseController?.WaitIfPaused(cancellationToken);

                            ConvertSingle(
                                image.FilePath,
                                folder,
                                normalized,
                                index,
                                reservationGate,
                                reservedDestinations);

                            Interlocked.Increment(ref successCount);
                            succeeded = true;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                            failures.Add(new ConversionFailure(image.FileName, ex.Message));
                        }
                        finally
                        {
                            if (succeeded || error is not null)
                            {
                                var processed = Interlocked.Increment(ref processedCount);
                                progress?.Report(new ConversionProgress(processed, images.Count, image.FileName, succeeded, error));
                            }
                        }
                    }
                }, cancellationToken))
                .ToArray();

            try
            {
                await Task.WhenAll(workers);
            }
            catch (OperationCanceledException)
            {
                wasCanceled = true;
            }

            stopwatch.Stop();

            var failureList = failures
                .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var folderList = outputFolders.Keys
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ConversionSummary(images.Count, processedCount, successCount, failureList, folderList, stopwatch.Elapsed, wasCanceled);
        }

        private static void ConvertSingle(
            string inputPath,
            string outputFolder,
            ConversionOptions options,
            int index,
            object reservationGate,
            HashSet<string> reservedDestinations)
        {
            var extension = Path.GetExtension(inputPath);
            if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
                options.GifHandlingMode == GifHandlingMode.AllFrames &&
                options.OutputFormat != OutputImageFormat.Gif)
            {
                ConvertGifToFrames(inputPath, outputFolder, options, index, reservationGate, reservedDestinations);
                return;
            }

            var destinationPath = BuildDestinationPath(
                inputPath,
                outputFolder,
                options,
                index,
                reservationGate,
                reservedDestinations);

            if (options.OutputFormat == OutputImageFormat.Svg)
            {
                ConvertToSvg(inputPath, destinationPath, options);
                return;
            }

            if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
                options.OutputFormat == OutputImageFormat.Gif)
            {
                ConvertGifToAnimatedGif(inputPath, destinationPath, options);
                return;
            }

            var forceWhiteForJpeg = options.OutputFormat == OutputImageFormat.Jpeg && !options.SvgUseBackground;
            var effectiveSvgBackground = options.SvgUseBackground || forceWhiteForJpeg;
            var effectiveBackgroundColor = forceWhiteForJpeg ? "#FFFFFFFF" : options.SvgBackgroundColor;

            using var sourceBitmap = DecodeToBitmap(inputPath, effectiveSvgBackground, effectiveBackgroundColor);
            if (sourceBitmap is null)
            {
                throw new InvalidOperationException("Unsupported or corrupted image file.");
            }

            using var preparedBitmap = CreatePreparedBitmap(sourceBitmap, options);
            SaveBitmap(preparedBitmap, destinationPath, options);
        }

        private static void ConvertGifToFrames(
            string inputPath,
            string outputFolder,
            ConversionOptions options,
            int index,
            object reservationGate,
            HashSet<string> reservedDestinations)
        {
            using var stream = File.OpenRead(inputPath);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                throw new InvalidOperationException("Unsupported or corrupted image file.");
            }

            var frameInfos = codec.FrameInfo;
            var frameCount = Math.Max(1, frameInfos.Length);
            var baseName = BuildBaseName(inputPath, options, index);
            var frameDigits = Math.Max(4, frameCount.ToString(CultureInfo.InvariantCulture).Length);
            var framesFolder = ReserveGifFramesFolder(outputFolder, baseName, options, reservationGate, reservedDestinations);

            Directory.CreateDirectory(framesFolder);

            var info = codec.Info;
            var extension = Extensions[options.OutputFormat];

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                using var frameBitmap = new SKBitmap(info);
                var decodeOptions = new SKCodecOptions(frameIndex)
                {
                    PriorFrame = -1
                };

                var result = codec.GetPixels(info, frameBitmap.GetPixels(), decodeOptions);
                if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                {
                    throw new InvalidOperationException("Failed to decode GIF frame.");
                }

                using var preparedBitmap = CreatePreparedBitmap(frameBitmap, options);
                var frameName = $"{baseName}_{(frameIndex + 1).ToString($"D{frameDigits}", CultureInfo.InvariantCulture)}{extension}";
                var destinationPath = Path.Combine(framesFolder, frameName);

                if (options.OutputFormat == OutputImageFormat.Svg)
                {
                    ConvertBitmapToSvg(preparedBitmap, destinationPath);
                }
                else
                {
                    SaveBitmap(preparedBitmap, destinationPath, options);
                }
            }
        }

        private static void ConvertGifToAnimatedGif(
            string inputPath,
            string destinationPath,
            ConversionOptions options)
        {
            using var stream = File.OpenRead(inputPath);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                throw new InvalidOperationException("Unsupported or corrupted image file.");
            }

            var frameInfos = codec.FrameInfo;
            var frameCount = Math.Max(1, frameInfos.Length);
            var gifQuality = ResolveGifQuality(options);
            var shouldApplyGifQuality = gifQuality < 100;

            if (frameCount <= 1)
            {
                using var sourceBitmap = DecodeToBitmap(inputPath, false, null);
                if (sourceBitmap is null)
                {
                    throw new InvalidOperationException("Failed to decode static GIF.");
                }
                using var preparedBitmap = CreatePreparedBitmap(sourceBitmap, options);
                SaveBitmap(preparedBitmap, destinationPath, options);
                return;
            }

            var info = codec.Info;
            using var firstFrame = new SKBitmap(info);
            var decodeOptions = new SKCodecOptions(0) { PriorFrame = -1 };
            codec.GetPixels(info, firstFrame.GetPixels(), decodeOptions);

            var (targetWidth, targetHeight) = CalculateTargetDimensions(firstFrame.Width, firstFrame.Height, options);
            var needsResize = targetWidth != firstFrame.Width || targetHeight != firstFrame.Height;

            if (!needsResize && options.ResizeMode == ResizeMode.None && !shouldApplyGifQuality)
            {
                stream.Position = 0;
                using var outputStream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(outputStream);
                return;
            }

            ConvertGifToAnimatedGifWithSkia(inputPath, destinationPath, options, codec, frameInfos, frameCount, info, targetWidth, targetHeight, gifQuality);
        }

        private static void ConvertGifToAnimatedGifWithSkia(
            string inputPath,
            string destinationPath,
            ConversionOptions options,
            SKCodec codec,
            SKCodecFrameInfo[] frameInfos,
            int frameCount,
            SKImageInfo info,
            int targetWidth,
            int targetHeight,
            int gifQuality)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"Imvix_Gif_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var shouldQuantize = TryCreateGifQuantizationTable(gifQuality, out var quantizationTable);

            try
            {
                var framePaths = new List<string>();
                var durations = new List<int>();
                var frameDurations = BuildGifFrameDurationsMs(inputPath, frameInfos, frameCount);

                using var accumulatedBitmap = new SKBitmap(info);
                using var accumulatedCanvas = new SKCanvas(accumulatedBitmap);

                for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    var frameInfo = frameInfos[frameIndex];
                    var disposeMethod = frameInfo.DisposalMethod;

                    using var frameBitmap = new SKBitmap(info);
                    var frameDecodeOptions = new SKCodecOptions(frameIndex);
                    var result = codec.GetPixels(info, frameBitmap.GetPixels(), frameDecodeOptions);

                    if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                    {
                        continue;
                    }

                    switch (disposeMethod)
                    {
                        case SKCodecAnimationDisposalMethod.RestorePrevious:
                            break;
                        case SKCodecAnimationDisposalMethod.RestoreBackgroundColor:
                            accumulatedCanvas.Clear(SKColors.Transparent);
                            accumulatedCanvas.Flush();
                            break;
                        default:
                            break;
                    }

                    accumulatedCanvas.DrawBitmap(frameBitmap, 0, 0);
                    accumulatedCanvas.Flush();

                    using var snapshot = SKImage.FromBitmap(accumulatedBitmap);
                    using var preparedFrame = ResizeImage(snapshot, targetWidth, targetHeight);
                    using var quantizedFrame = shouldQuantize && quantizationTable is not null
                        ? QuantizeGifColors(preparedFrame, quantizationTable)
                        : null;
                    var frameOutputBitmap = quantizedFrame ?? preparedFrame;
                    var framePath = Path.Combine(tempDir, $"frame_{frameIndex:D4}.png");

                    using (var pngData = frameOutputBitmap.Encode(SKEncodedImageFormat.Png, 100))
                    using (var fileStream = File.OpenWrite(framePath))
                    {
                        pngData.SaveTo(fileStream);
                    }

                    framePaths.Add(framePath);

                    if (frameIndex < frameDurations.Count)
                    {
                        durations.Add(frameDurations[frameIndex]);
                    }
                    else
                    {
                        durations.Add(Math.Max(20, frameInfo.Duration));
                    }

                    if (disposeMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
                    {
                        accumulatedCanvas.Clear(SKColors.Transparent);
                        accumulatedCanvas.Flush();
                    }
                }

                if (framePaths.Count == 0)
                {
                    throw new InvalidOperationException("No frames could be decoded from the GIF.");
                }

                CreateAnimatedGifFromFrames(framePaths, durations, destinationPath);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static List<int> BuildGifFrameDurationsMs(
            string inputPath,
            SKCodecFrameInfo[] frameInfos,
            int frameCount)
        {
            if (OperatingSystem.IsWindows() &&
                TryReadGifFrameDelays(inputPath, frameCount, out var delaysMs))
            {
                return delaysMs;
            }

            var durations = new List<int>(frameCount);
            for (var i = 0; i < frameCount; i++)
            {
                var duration = Math.Max(20, frameInfos[i].Duration);
                durations.Add(duration);
            }

            return durations;
        }

        private static bool TryReadGifFrameDelays(string inputPath, int frameCount, out List<int> delaysMs)
        {
            delaysMs = [];
            try
            {
                using var stream = File.OpenRead(inputPath);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);

                if (!image.PropertyIdList.Contains(GifPropertyTagFrameDelay))
                {
                    return false;
                }

                var item = image.GetPropertyItem(GifPropertyTagFrameDelay);
                if (item is null)
                {
                    return false;
                }
                var value = item.Value;
                if (value is null || value.Length < 4)
                {
                    return false;
                }

                var available = value.Length / 4;
                if (available <= 0)
                {
                    return false;
                }

                var count = Math.Min(frameCount, available);
                delaysMs = new List<int>(frameCount);

                var lastNonZeroCs = 0;
                for (var i = 0; i < count; i++)
                {
                    var delayCs = BitConverter.ToInt32(value, i * 4);
                    if (delayCs <= 0)
                    {
                        delayCs = lastNonZeroCs > 0 ? lastNonZeroCs : 10;
                    }
                    else
                    {
                        lastNonZeroCs = delayCs;
                    }

                    delaysMs.Add(delayCs * 10);
                }

                var paddingCs = lastNonZeroCs > 0 ? lastNonZeroCs : 10;
                for (var i = count; i < frameCount; i++)
                {
                    delaysMs.Add(paddingCs * 10);
                }

                return delaysMs.Count > 0;
            }
            catch
            {
                delaysMs.Clear();
                return false;
            }
        }

        private static SKBitmap ResizeImage(SKImage source, int targetWidth, int targetHeight)
        {
            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var targetCanvas = new SKCanvas(result);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };

            targetCanvas.Clear(SKColors.Transparent);

            if (source.Width == targetWidth && source.Height == targetHeight)
            {
                targetCanvas.DrawImage(source, 0, 0);
            }
            else
            {
                targetCanvas.DrawImage(source, SKRect.Create(targetWidth, targetHeight), paint);
            }

            targetCanvas.Flush();

            return result;
        }

        private const int GifPropertyTagFrameDelay = 0x5100;
        private const int GifPropertyTagLoopCount = 0x5101;
        private const short GifPropertyTypeShort = 3;
        private const short GifPropertyTypeLong = 4;

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void CreateAnimatedGifFromFrames(
            List<string> framePaths,
            List<int> durations,
            string destinationPath)
        {
            if (framePaths.Count == 0)
            {
                throw new InvalidOperationException("No frames to encode.");
            }

            if (framePaths.Count != durations.Count)
            {
                throw new InvalidOperationException("Frame count and duration count mismatch.");
            }

            var gifEncoder = GetGifEncoder();

            using var firstFrame = new System.Drawing.Bitmap(framePaths[0]);
            ApplyGifFrameMetadata(firstFrame, durations);

            using var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
            firstFrame.Save(destinationPath, gifEncoder, encoderParameters);

            encoderParameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
            for (var i = 1; i < framePaths.Count; i++)
            {
                using var frame = new System.Drawing.Bitmap(framePaths[i]);
                firstFrame.SaveAdd(frame, encoderParameters);
            }

            encoderParameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
            firstFrame.SaveAdd(encoderParameters);
        }

        private static ImageCodecInfo GetGifEncoder()
        {
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec => codec.FormatID == ImageFormat.Gif.Guid);

            if (encoder is null)
            {
                throw new InvalidOperationException("GIF encoder not found.");
            }

            return encoder;
        }

        private static void ApplyGifFrameMetadata(System.Drawing.Image image, List<int> durations)
        {
            var delayBytes = new byte[durations.Count * 4];

            for (var i = 0; i < durations.Count; i++)
            {
                var delay = Math.Max(1, (int)Math.Round(durations[i] / 10d));
                var bytes = BitConverter.GetBytes(delay);
                var offset = i * 4;
                delayBytes[offset] = bytes[0];
                delayBytes[offset + 1] = bytes[1];
                delayBytes[offset + 2] = bytes[2];
                delayBytes[offset + 3] = bytes[3];
            }

            try
            {
                image.SetPropertyItem(CreateGifPropertyItem(GifPropertyTagFrameDelay, GifPropertyTypeLong, delayBytes));
                image.SetPropertyItem(CreateGifPropertyItem(GifPropertyTagLoopCount, GifPropertyTypeShort, BitConverter.GetBytes((ushort)0)));
            }
            catch
            {
            }
        }

        private static PropertyItem CreateGifPropertyItem(int id, short type, byte[] value)
        {
            var item = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            item.Id = id;
            item.Type = type;
            item.Len = value.Length;
            item.Value = value;
            return item;
        }

        private static string ReserveGifFramesFolder(
            string outputFolder,
            string baseName,
            ConversionOptions options,
            object reservationGate,
            HashSet<string> reservedDestinations)
        {
            var baseFolderName = $"{baseName}_frames";

            lock (reservationGate)
            {
                var folderPath = Path.Combine(outputFolder, baseFolderName);
                if (options.AllowOverwrite)
                {
                    reservedDestinations.Add(folderPath);
                    return folderPath;
                }

                var suffix = 1;
                while (Directory.Exists(folderPath) || reservedDestinations.Contains(folderPath))
                {
                    folderPath = Path.Combine(outputFolder, $"{baseFolderName}_{suffix}");
                    suffix++;
                }

                reservedDestinations.Add(folderPath);
                return folderPath;
            }
        }

        private static void ConvertBitmapToSvg(SKBitmap sourceBitmap, string destinationPath)
        {
            using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var canvas = SKSvgCanvas.Create(SKRect.Create(sourceBitmap.Width, sourceBitmap.Height), stream);

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(sourceBitmap, 0, 0);
            canvas.Flush();
        }

        private static void SaveBitmap(SKBitmap sourceBitmap, string destinationPath, ConversionOptions options)
        {
            if (options.OutputFormat == OutputImageFormat.Ico)
            {
                ConvertToIco(sourceBitmap, destinationPath);
                return;
            }

            if (options.OutputFormat == OutputImageFormat.Bmp)
            {
                ConvertToBmp(sourceBitmap, destinationPath);
                return;
            }

            if (options.OutputFormat == OutputImageFormat.Gif)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("GIF output is only supported on Windows in this build.");
                }

                var gifQuality = ResolveGifQuality(options);
                if (TryCreateGifQuantizationTable(gifQuality, out var quantizationTable) && quantizationTable is not null)
                {
                    using var quantized = QuantizeGifColors(sourceBitmap, quantizationTable);
                    ConvertToGif(quantized, destinationPath);
                }
                else
                {
                    ConvertToGif(sourceBitmap, destinationPath);
                }
                return;
            }

            if (options.OutputFormat == OutputImageFormat.Tiff)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("TIFF output is only supported on Windows in this build.");
                }

                ConvertToTiff(sourceBitmap, destinationPath);
                return;
            }

            using var image = SKImage.FromBitmap(sourceBitmap);
            var skiaFormat = ToSkiaFormat(options.OutputFormat);
            var encodedQuality = options.OutputFormat is OutputImageFormat.Jpeg or OutputImageFormat.Webp
                ? ResolveQuality(options)
                : 100;

            using var data = image.Encode(skiaFormat, encodedQuality);
            if (data is null)
            {
                throw new InvalidOperationException("Failed to encode image.");
            }

            using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
        }

        private static ConversionOptions NormalizeOptions(ConversionOptions options)
        {
            return new ConversionOptions
            {
                OutputFormat = options.OutputFormat,
                CompressionMode = options.CompressionMode,
                Quality = Math.Clamp(options.Quality, 1, 100),
                ResizeMode = options.ResizeMode,
                ResizeWidth = Math.Max(1, options.ResizeWidth),
                ResizeHeight = Math.Max(1, options.ResizeHeight),
                ResizePercent = Math.Clamp(options.ResizePercent, 1, 1000),
                RenameMode = options.RenameMode,
                RenamePrefix = options.RenamePrefix ?? string.Empty,
                RenameSuffix = options.RenameSuffix ?? string.Empty,
                RenameStartNumber = Math.Max(0, options.RenameStartNumber),
                RenameNumberDigits = Math.Clamp(options.RenameNumberDigits, 1, 8),
                OutputDirectoryRule = options.OutputDirectoryRule,
                OutputDirectory = options.OutputDirectory ?? string.Empty,
                AutoResultFolderName = string.IsNullOrWhiteSpace(options.AutoResultFolderName)
                    ? "Imvix_Output"
                    : options.AutoResultFolderName.Trim(),
                AllowOverwrite = options.AllowOverwrite,
                SvgUseBackground = options.SvgUseBackground,
                SvgBackgroundColor = string.IsNullOrWhiteSpace(options.SvgBackgroundColor)
                    ? "#FFFFFFFF"
                    : options.SvgBackgroundColor,
                GifHandlingMode = options.GifHandlingMode,
                MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism)
            };
        }

        private static int ResolveQuality(ConversionOptions options)
        {
            return options.CompressionMode switch
            {
                CompressionMode.HighQuality => 92,
                CompressionMode.Balanced => 80,
                CompressionMode.HighCompression => 60,
                _ => Math.Clamp(options.Quality, 1, 100)
            };
        }

        private static int ResolveGifQuality(ConversionOptions options)
        {
            return options.CompressionMode switch
            {
                CompressionMode.HighQuality => 100,
                CompressionMode.Balanced => 80,
                CompressionMode.HighCompression => 60,
                _ => Math.Clamp(options.Quality, 1, 100)
            };
        }

        private static bool TryCreateGifQuantizationTable(int gifQuality, [NotNullWhen(true)] out byte[]? table)
        {
            table = null;
            var clamped = Math.Clamp(gifQuality, 1, 100);
            if (clamped >= 100)
            {
                return false;
            }

            var levels = clamped switch
            {
                >= 90 => 6,
                >= 70 => 5,
                >= 50 => 4,
                >= 30 => 3,
                _ => 2
            };

            table = BuildGifQuantizationTable(levels);
            return true;
        }

        private static byte[] BuildGifQuantizationTable(int levels)
        {
            var table = new byte[256];
            var safeLevels = Math.Clamp(levels, 2, 6);
            var step = 255d / (safeLevels - 1);

            for (var i = 0; i < table.Length; i++)
            {
                var level = (int)Math.Round(i / step);
                level = Math.Clamp(level, 0, safeLevels - 1);
                var value = (int)Math.Round(level * step);
                table[i] = (byte)Math.Clamp(value, 0, 255);
            }

            return table;
        }

        private static SKBitmap QuantizeGifColors(SKBitmap sourceBitmap, byte[] quantizationTable)
        {
            var result = sourceBitmap.Copy();
            var pixels = result.Pixels;

            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                if (color.Alpha == 0)
                {
                    continue;
                }

                pixels[i] = new SKColor(
                    quantizationTable[color.Red],
                    quantizationTable[color.Green],
                    quantizationTable[color.Blue],
                    color.Alpha);
            }

            result.Pixels = pixels;
            return result;
        }

        private static SKBitmap CreatePreparedBitmap(SKBitmap sourceBitmap, ConversionOptions options)
        {
            var (targetWidth, targetHeight) = CalculateTargetDimensions(sourceBitmap.Width, sourceBitmap.Height, options);

            if (targetWidth == sourceBitmap.Width && targetHeight == sourceBitmap.Height)
            {
                return sourceBitmap.Copy();
            }

            var info = new SKImageInfo(targetWidth, targetHeight, sourceBitmap.ColorType, sourceBitmap.AlphaType);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(sourceBitmap, SKRect.Create(targetWidth, targetHeight), paint);
            canvas.Flush();

            return result;
        }

        private static (int Width, int Height) CalculateTargetDimensions(int sourceWidth, int sourceHeight, ConversionOptions options)
        {
            if (sourceWidth < 1 || sourceHeight < 1)
            {
                return (1, 1);
            }

            return options.ResizeMode switch
            {
                ResizeMode.FixedWidth =>
                    (Math.Max(1, options.ResizeWidth), Math.Max(1, (int)Math.Round(sourceHeight * (options.ResizeWidth / (double)sourceWidth)))),
                ResizeMode.FixedHeight =>
                    (Math.Max(1, (int)Math.Round(sourceWidth * (options.ResizeHeight / (double)sourceHeight))), Math.Max(1, options.ResizeHeight)),
                ResizeMode.ScalePercent =>
                    (
                        Math.Max(1, (int)Math.Round(sourceWidth * options.ResizePercent / 100d)),
                        Math.Max(1, (int)Math.Round(sourceHeight * options.ResizePercent / 100d))
                    ),
                ResizeMode.CustomSize => (Math.Max(1, options.ResizeWidth), Math.Max(1, options.ResizeHeight)),
                _ => (sourceWidth, sourceHeight)
            };
        }

        private static void ConvertToIco(SKBitmap sourceBitmap, string destinationPath)
        {
            using var iconBitmap = PrepareBitmapForIco(sourceBitmap);
            using var image = SKImage.FromBitmap(iconBitmap);
            using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
            if (pngData is null)
            {
                throw new InvalidOperationException("Failed to encode image.");
            }

            var pngBytes = pngData.ToArray();

            using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);

            writer.Write(iconBitmap.Width == 256 ? (byte)0 : (byte)iconBitmap.Width);
            writer.Write(iconBitmap.Height == 256 ? (byte)0 : (byte)iconBitmap.Height);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)pngBytes.Length);
            writer.Write((uint)22);
            writer.Write(pngBytes);
        }

        private static void ConvertToBmp(SKBitmap sourceBitmap, string destinationPath)
        {
            if (sourceBitmap.Width < 1 || sourceBitmap.Height < 1)
            {
                throw new InvalidOperationException("Invalid image dimensions for BMP conversion.");
            }

            const int fileHeaderSize = 14;
            const int dibHeaderSize = 40;
            const int bitsPerPixel = 32;
            const int bytesPerPixel = bitsPerPixel / 8;

            int rowStride;
            int pixelDataSize;
            int fileSize;
            checked
            {
                rowStride = sourceBitmap.Width * bytesPerPixel;
                pixelDataSize = rowStride * sourceBitmap.Height;
                fileSize = fileHeaderSize + dibHeaderSize + pixelDataSize;
            }

            using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);

            writer.Write((ushort)0x4D42);
            writer.Write(fileSize);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(fileHeaderSize + dibHeaderSize);

            writer.Write(dibHeaderSize);
            writer.Write(sourceBitmap.Width);
            writer.Write(-sourceBitmap.Height);
            writer.Write((ushort)1);
            writer.Write((ushort)bitsPerPixel);
            writer.Write(0);
            writer.Write(pixelDataSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            for (var y = 0; y < sourceBitmap.Height; y++)
            {
                for (var x = 0; x < sourceBitmap.Width; x++)
                {
                    var color = sourceBitmap.GetPixel(x, y);
                    writer.Write(color.Blue);
                    writer.Write(color.Green);
                    writer.Write(color.Red);
                    writer.Write(color.Alpha);
                }
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void ConvertToGif(SKBitmap sourceBitmap, string destinationPath)
        {
            using var image = SKImage.FromBitmap(sourceBitmap);
            using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
            if (pngData is null)
            {
                throw new InvalidOperationException("Failed to encode image for GIF conversion.");
            }

            using var pngStream = new MemoryStream(pngData.ToArray());
            using var gifImage = System.Drawing.Image.FromStream(
                pngStream,
                useEmbeddedColorManagement: true,
                validateImageData: true);
            gifImage.Save(destinationPath, System.Drawing.Imaging.ImageFormat.Gif);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void ConvertToTiff(SKBitmap sourceBitmap, string destinationPath)
        {
            using var image = SKImage.FromBitmap(sourceBitmap);
            using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
            if (pngData is null)
            {
                throw new InvalidOperationException("Failed to encode image for TIFF conversion.");
            }

            using var pngStream = new MemoryStream(pngData.ToArray());
            using var tiffImage = System.Drawing.Image.FromStream(
                pngStream,
                useEmbeddedColorManagement: true,
                validateImageData: true);
            tiffImage.Save(destinationPath, System.Drawing.Imaging.ImageFormat.Tiff);
        }

        private static SKBitmap PrepareBitmapForIco(SKBitmap sourceBitmap)
        {
            const int maxIconSize = 256;

            if (sourceBitmap.Width < 1 || sourceBitmap.Height < 1)
            {
                throw new InvalidOperationException("Invalid image dimensions for ICO conversion.");
            }

            var targetSize = Math.Min(maxIconSize, Math.Max(sourceBitmap.Width, sourceBitmap.Height));
            var scale = Math.Min((float)targetSize / sourceBitmap.Width, (float)targetSize / sourceBitmap.Height);
            var drawWidth = Math.Max(1, (int)Math.Round(sourceBitmap.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));
            var offsetX = (targetSize - drawWidth) / 2f;
            var offsetY = (targetSize - drawHeight) / 2f;

            var info = new SKImageInfo(targetSize, targetSize, SKColorType.Bgra8888, SKAlphaType.Premul);
            var iconBitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(iconBitmap);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };

            canvas.Clear(SKColors.Transparent);
            var destination = SKRect.Create(offsetX, offsetY, drawWidth, drawHeight);
            canvas.DrawBitmap(sourceBitmap, destination, paint);
            canvas.Flush();

            return iconBitmap;
        }

        private static void ConvertToSvg(string inputPath, string destinationPath, ConversionOptions options)
        {
            var extension = Path.GetExtension(inputPath);
            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) && options.ResizeMode == ResizeMode.None && !options.SvgUseBackground)
            {
                File.Copy(inputPath, destinationPath, overwrite: true);
                return;
            }

            using var bitmap = DecodeToBitmap(inputPath, options.SvgUseBackground, options.SvgBackgroundColor);
            if (bitmap is null)
            {
                throw new InvalidOperationException("Unsupported or corrupted image file.");
            }

            using var prepared = CreatePreparedBitmap(bitmap, options);

            using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var canvas = SKSvgCanvas.Create(SKRect.Create(prepared.Width, prepared.Height), stream);

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(prepared, 0, 0);
            canvas.Flush();
        }

        private static SKBitmap? DecodeToBitmap(string inputPath, bool svgUseBackground, string? svgBackgroundColor)
        {
            var extension = Path.GetExtension(inputPath);
            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return DecodeSvgToBitmap(inputPath, svgUseBackground, svgBackgroundColor);
            }

            return SKBitmap.Decode(inputPath);
        }

        private static SKBitmap DecodeSvgToBitmap(string inputPath, bool svgUseBackground, string? svgBackgroundColor)
        {
            var svg = new SKSvg();
            var picture = svg.Load(inputPath);
            if (picture is null)
            {
                throw new InvalidOperationException("Invalid SVG file.");
            }

            var bounds = picture.CullRect;
            var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
            var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(bitmap);
            if (svgUseBackground)
            {
                canvas.Clear(ParseBackgroundColor(svgBackgroundColor));
            }
            else
            {
                canvas.Clear(SKColors.Transparent);
            }

            var matrix = SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture, ref matrix);
            canvas.Flush();

            return bitmap;
        }

        private static SKColor ParseBackgroundColor(string? svgBackgroundColor)
        {
            if (!string.IsNullOrWhiteSpace(svgBackgroundColor) && SKColor.TryParse(svgBackgroundColor, out var parsed))
            {
                return parsed;
            }

            return SKColors.White;
        }

        private static string ResolveOutputFolder(string inputPath, ConversionOptions options)
        {
            var sourceFolder = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;

            return options.OutputDirectoryRule switch
            {
                OutputDirectoryRule.SourceFolder => sourceFolder,
                OutputDirectoryRule.SpecificFolder =>
                    string.IsNullOrWhiteSpace(options.OutputDirectory) ? sourceFolder : options.OutputDirectory,
                OutputDirectoryRule.AutoCreateResultFolder =>
                    Path.Combine(
                        string.IsNullOrWhiteSpace(options.OutputDirectory) ? sourceFolder : options.OutputDirectory,
                        options.AutoResultFolderName),
                _ => sourceFolder
            };
        }

        private static string BuildDestinationPath(
            string inputPath,
            string outputFolder,
            ConversionOptions options,
            int index,
            object reservationGate,
            HashSet<string> reservedDestinations)
        {
            var extension = Extensions[options.OutputFormat];
            var baseName = BuildBaseName(inputPath, options, index);

            lock (reservationGate)
            {
                var destinationPath = Path.Combine(outputFolder, $"{baseName}{extension}");

                if (options.AllowOverwrite)
                {
                    reservedDestinations.Add(destinationPath);
                    return destinationPath;
                }

                var suffix = 1;
                while (File.Exists(destinationPath) || reservedDestinations.Contains(destinationPath))
                {
                    destinationPath = Path.Combine(outputFolder, $"{baseName}_{suffix}{extension}");
                    suffix++;
                }

                reservedDestinations.Add(destinationPath);
                return destinationPath;
            }
        }

        private static string BuildBaseName(string inputPath, ConversionOptions options, int index)
        {
            var original = Path.GetFileNameWithoutExtension(inputPath);

            return options.RenameMode switch
            {
                RenameMode.AutoNumber => (options.RenameStartNumber + index).ToString($"D{options.RenameNumberDigits}", System.Globalization.CultureInfo.InvariantCulture),
                RenameMode.Prefix => SanitizeNameSegment(options.RenamePrefix) + original,
                RenameMode.Suffix => original + SanitizeNameSegment(options.RenameSuffix),
                _ => original
            };
        }

        private static string SanitizeNameSegment(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var result = text;
            foreach (var invalidChar in invalidChars)
            {
                result = result.Replace(invalidChar.ToString(), string.Empty, StringComparison.Ordinal);
            }

            return result;
        }

        private static void DisposePreviewFrames(IReadOnlyList<Bitmap> frames)
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }

        private static Bitmap? CreatePreviewFromBitmap(SKBitmap bitmap, int maxWidth)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                return null;
            }

            using var memory = new MemoryStream(data.ToArray());
            return Bitmap.DecodeToWidth(memory, maxWidth);
        }

        private static SKEncodedImageFormat ToSkiaFormat(OutputImageFormat outputFormat)
        {
            return outputFormat switch
            {
                OutputImageFormat.Png => SKEncodedImageFormat.Png,
                OutputImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                OutputImageFormat.Webp => SKEncodedImageFormat.Webp,
                _ => throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, null)
            };
        }

        public sealed class GifPreviewHandle : IDisposable
        {
            private readonly string _cacheKey;
            private readonly GifPreviewCacheEntry _entry;
            private bool _isDisposed;

            internal GifPreviewHandle(string cacheKey, GifPreviewCacheEntry entry)
            {
                _cacheKey = cacheKey;
                _entry = entry;
            }

            public IReadOnlyList<Bitmap> Frames => _entry.Frames;

            public IReadOnlyList<TimeSpan> Durations => _entry.Durations;

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                ReleaseGifPreview(_cacheKey, _entry);
            }
        }

        internal sealed class GifPreviewCacheEntry
        {
            public GifPreviewCacheEntry(List<Bitmap> frames, List<TimeSpan> durations)
            {
                Frames = frames;
                Durations = durations;
            }

            public List<Bitmap> Frames { get; }

            public List<TimeSpan> Durations { get; }

            public int RefCount { get; set; }

            public DateTime LastAccessUtc { get; set; }

            public void Dispose()
            {
                DisposePreviewFrames(Frames);
            }
        }
    }
}
