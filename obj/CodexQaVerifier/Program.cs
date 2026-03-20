using ImvixPro.Models;
using ImvixPro.Services;
using ImvixPro.Services.PdfModule;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

var workspaceRoot = Directory.GetCurrentDirectory();
var runtimeRoot = Path.Combine(workspaceRoot, "obj", "CodexQaVerifier", "runtime");
var assetsRoot = Path.Combine(runtimeRoot, "assets");
var outputsRoot = Path.Combine(runtimeRoot, "outputs");

RecreateDirectory(runtimeRoot);
Directory.CreateDirectory(assetsRoot);
Directory.CreateDirectory(outputsRoot);

var animatedGifSource = Path.Combine(workspaceRoot, "obj", "obj", "CodexGifTrimVerifier", "runtime", "source.gif");
if (!File.Exists(animatedGifSource))
{
    throw new FileNotFoundException("Animated GIF test asset not found.", animatedGifSource);
}

var assetPaths = CreateAssets(assetsRoot, animatedGifSource);
var results = new List<TestResult>();

await RunTestAsync("MixedInputToJpeg", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "mixed-jpeg");
    Directory.CreateDirectory(outputDir);

    var summary = await ConvertAsync(
        [
            assetPaths.Png,
            assetPaths.Jpeg,
            assetPaths.Webp,
            assetPaths.Bmp,
            assetPaths.Tiff,
            assetPaths.Ico,
            assetPaths.Svg,
            assetPaths.StaticGif,
            assetPaths.SinglePagePdf
        ],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.PdfImageExportMode = PdfImageExportMode.CurrentPage;
            options.GifHandlingMode = GifHandlingMode.FirstFrame;
        });

    Assert(summary.FailureCount == 0, $"Expected 0 failures, got {summary.FailureCount}.");
    Assert(summary.SuccessCount == 9, $"Expected 9 successes, got {summary.SuccessCount}.");

    var files = Directory.GetFiles(outputDir, "*.jpg", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(files.Length == 9, $"Expected 9 JPEG files, got {files.Length}.");
    foreach (var file in files)
    {
        AssertValidRaster(file);
    }
}, results);

await RunTestAsync("SingleFrameGifUsesSingleFileOutput", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "single-frame-gif");
    Directory.CreateDirectory(outputDir);

    var summary = await ConvertAsync(
        [assetPaths.StaticGif],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Png;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.GifHandlingMode = GifHandlingMode.AllFrames;
        });

    Assert(summary.FailureCount == 0, $"Expected 0 failures, got {summary.FailureCount}.");
    Assert(File.Exists(Path.Combine(outputDir, "static.png")), "Expected single-frame GIF to produce static.png.");
    Assert(!Directory.Exists(Path.Combine(outputDir, "static")), "Single-frame GIF should not create a frame folder.");
}, results);

await RunTestAsync("AnimatedGifAllFramesUsesFolderAndFrameNames", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "gif-frames");
    Directory.CreateDirectory(outputDir);

    var summary = await ConvertAsync(
        [assetPaths.AnimatedGif],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.GifHandlingMode = GifHandlingMode.AllFrames;
        });

    Assert(summary.FailureCount == 0, $"Expected 0 failures, got {summary.FailureCount}.");

    var frameDir = Path.Combine(outputDir, "animated");
    Assert(Directory.Exists(frameDir), $"Expected GIF output folder '{frameDir}'.");

    var files = Directory.GetFiles(frameDir, "*.jpg", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(files.SequenceEqual(["frame_1.jpg", "frame_2.jpg", "frame_3.jpg", "frame_4.jpg"]),
        $"Unexpected GIF frame names: {string.Join(", ", files)}");
}, results);

await RunTestAsync("AnimatedGifFolderCollisionUsesParentheses", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "gif-collision");
    Directory.CreateDirectory(outputDir);

    await ConvertAsync(
        [assetPaths.AnimatedGif],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.GifHandlingMode = GifHandlingMode.AllFrames;
        });

    await ConvertAsync(
        [assetPaths.AnimatedGif],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.GifHandlingMode = GifHandlingMode.AllFrames;
        });

    var directories = Directory.GetDirectories(outputDir, "*", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(directories.SequenceEqual(["animated", "animated(1)"]),
        $"Unexpected GIF collision folders: {string.Join(", ", directories)}");
}, results);

await RunTestAsync("PdfAllPagesUsesFolderAndPageNames", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "pdf-pages");
    Directory.CreateDirectory(outputDir);

    var summary = await ConvertAsync(
        [assetPaths.MultiPagePdf],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Png;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.PdfImageExportMode = PdfImageExportMode.AllPages;
        });

    Assert(summary.FailureCount == 0, $"Expected 0 failures, got {summary.FailureCount}.");

    var pagesDir = Path.Combine(outputDir, "document");
    Assert(Directory.Exists(pagesDir), $"Expected PDF output folder '{pagesDir}'.");

    var files = Directory.GetFiles(pagesDir, "*.png", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(files.SequenceEqual(["page_1.png", "page_2.png"]),
        $"Unexpected PDF page names: {string.Join(", ", files)}");
}, results);

await RunTestAsync("PdfFolderCollisionUsesParentheses", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "pdf-collision");
    Directory.CreateDirectory(outputDir);

    await ConvertAsync(
        [assetPaths.MultiPagePdf],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Png;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.PdfImageExportMode = PdfImageExportMode.AllPages;
        });

    await ConvertAsync(
        [assetPaths.MultiPagePdf],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Png;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.PdfImageExportMode = PdfImageExportMode.AllPages;
        });

    var directories = Directory.GetDirectories(outputDir, "*", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(directories.SequenceEqual(["document", "document(1)"]),
        $"Unexpected PDF collision folders: {string.Join(", ", directories)}");
}, results);

await RunTestAsync("FileCollisionUsesParentheses", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "file-collision");
    Directory.CreateDirectory(outputDir);

    await ConvertAsync(
        [assetPaths.Png],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
        });

    await ConvertAsync(
        [assetPaths.Png],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
        });

    var files = Directory.GetFiles(outputDir, "*.jpg", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(files.Length == 2, $"Expected 2 JPEG outputs, got {files.Length}.");
    Assert(files.Contains("base.jpg", StringComparer.OrdinalIgnoreCase) &&
           files.Contains("base(1).jpg", StringComparer.OrdinalIgnoreCase),
        $"Unexpected file collision names: {string.Join(", ", files)}");
}, results);

await RunTestAsync("BatchConversionStaysStable", async () =>
{
    var sourceDir = Path.Combine(runtimeRoot, "batch-inputs");
    var outputDir = Path.Combine(outputsRoot, "batch-jpeg");
    Directory.CreateDirectory(sourceDir);
    Directory.CreateDirectory(outputDir);

    for (var i = 1; i <= 120; i++)
    {
        File.Copy(assetPaths.Png, Path.Combine(sourceDir, $"batch_{i:D3}.png"), overwrite: true);
    }

    var progressEvents = new List<ConversionProgress>();
    var summary = await ConvertAsync(
        Directory.GetFiles(sourceDir, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.MaxDegreeOfParallelism = 4;
        },
        progress => progressEvents.Add(progress));

    Assert(summary.FailureCount == 0, $"Expected 0 failures, got {summary.FailureCount}.");
    Assert(summary.SuccessCount == 120, $"Expected 120 successes, got {summary.SuccessCount}.");
    Assert(Directory.GetFiles(outputDir, "*.jpg", SearchOption.TopDirectoryOnly).Length == 120, "Expected 120 JPEG outputs.");

    var lastProgress = progressEvents.LastOrDefault();
    Assert(lastProgress is not null, "Expected at least one progress event.");
    Assert(lastProgress!.ProcessedFileCount == 120, $"Expected final processed file count 120, got {lastProgress.ProcessedFileCount}.");
    Assert(lastProgress.TotalFileCount == 120, $"Expected final total file count 120, got {lastProgress.TotalFileCount}.");
}, results);

await RunTestAsync("FolderWatchSuppressesDuplicateReadyEvents", async () =>
{
    var watchDir = Path.Combine(runtimeRoot, "watch-single");
    Directory.CreateDirectory(watchDir);

    using var watcher = new FolderWatchService();
    var readyEvents = new ConcurrentBag<string>();
    watcher.FileReady += (_, path) => readyEvents.Add(path);
    watcher.Start(watchDir, includeSubfolders: false);

    var targetPath = Path.Combine(watchDir, "chunked.png");
    var bytes = await File.ReadAllBytesAsync(assetPaths.Png);
    var midpoint = bytes.Length / 2;

    await using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true))
    {
        await stream.WriteAsync(bytes.AsMemory(0, midpoint));
        await stream.FlushAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.7));
        await stream.WriteAsync(bytes.AsMemory(midpoint));
        await stream.FlushAsync();
    }

    await Task.Delay(TimeSpan.FromSeconds(4));

    var readyCount = readyEvents.Count(path => path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
    Assert(readyCount == 1, $"Expected 1 ready event for chunked write, got {readyCount}.");
}, results);

await RunTestAsync("FolderWatchHandlesLargeBurstWithoutDuplicates", async () =>
{
    var watchDir = Path.Combine(runtimeRoot, "watch-batch");
    Directory.CreateDirectory(watchDir);

    using var watcher = new FolderWatchService();
    var readyEvents = new ConcurrentBag<string>();
    watcher.FileReady += (_, path) => readyEvents.Add(path);
    watcher.Start(watchDir, includeSubfolders: false);

    for (var i = 1; i <= 120; i++)
    {
        File.Copy(assetPaths.Png, Path.Combine(watchDir, $"burst_{i:D3}.png"), overwrite: true);
    }

    var timeout = Stopwatch.StartNew();
    while (timeout.Elapsed < TimeSpan.FromSeconds(20))
    {
        var uniqueCount = readyEvents
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (uniqueCount >= 120)
        {
            break;
        }

        await Task.Delay(200);
    }

    await Task.Delay(TimeSpan.FromSeconds(2));

    var uniqueReady = readyEvents
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    Assert(uniqueReady == 120, $"Expected 120 unique watch events, got {uniqueReady}.");
    Assert(readyEvents.Count == 120, $"Expected 120 total watch events, got {readyEvents.Count}.");
}, results);

await RunTestAsync("CorruptedInputsFailWithoutCrash", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "corrupted");
    Directory.CreateDirectory(outputDir);

    var corruptPng = Path.Combine(assetsRoot, "corrupt.png");
    var emptyGif = Path.Combine(assetsRoot, "empty.gif");
    await File.WriteAllBytesAsync(corruptPng, [1, 2, 3, 4, 5, 6]);
    await File.WriteAllBytesAsync(emptyGif, []);

    var summary = await ConvertAsync(
        [corruptPng, emptyGif],
        options =>
        {
            options.OutputFormat = OutputImageFormat.Jpeg;
            options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
            options.OutputDirectory = outputDir;
            options.GifHandlingMode = GifHandlingMode.FirstFrame;
        });

    Assert(summary.SuccessCount == 0, $"Expected 0 successes, got {summary.SuccessCount}.");
    Assert(summary.FailureCount == 2, $"Expected 2 failures, got {summary.FailureCount}.");
}, results);

foreach (var result in results)
{
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}: {result.Details}");
}

var failed = results.Where(result => !result.Passed).ToList();
if (failed.Count > 0)
{
    Console.Error.WriteLine($"FAILED={failed.Count}");
    Environment.ExitCode = 1;
}

return;

static async Task RunTestAsync(string name, Func<Task> test, ICollection<TestResult> results)
{
    try
    {
        await test();
        results.Add(new TestResult(name, true, "ok"));
    }
    catch (Exception ex)
    {
        results.Add(new TestResult(name, false, ex.Message));
    }
}

static AssetPaths CreateAssets(string assetsRoot, string animatedGifSource)
{
    using var bitmap = CreateBaseBitmap();

    var pngPath = Path.Combine(assetsRoot, "base.png");
    var jpegPath = Path.Combine(assetsRoot, "base.jpg");
    var webpPath = Path.Combine(assetsRoot, "base.webp");
    var bmpPath = Path.Combine(assetsRoot, "base.bmp");
    var tiffPath = Path.Combine(assetsRoot, "base.tiff");
    var icoPath = Path.Combine(assetsRoot, "base.ico");
    var svgPath = Path.Combine(assetsRoot, "base.svg");
    var staticGifPath = Path.Combine(assetsRoot, "static.gif");
    var animatedGifPath = Path.Combine(assetsRoot, "animated.gif");
    var singlePagePdfPath = Path.Combine(assetsRoot, "single.pdf");
    var multiPagePdfPath = Path.Combine(assetsRoot, "document.pdf");

    SaveWithSkia(bitmap, pngPath, SKEncodedImageFormat.Png, 100);
    SaveWithSkia(bitmap, jpegPath, SKEncodedImageFormat.Jpeg, 92);
    SaveWithSkia(bitmap, webpPath, SKEncodedImageFormat.Webp, 92);
    SaveWithSystemDrawing(bitmap, bmpPath, ImageFormat.Bmp);
    SaveWithSystemDrawing(bitmap, tiffPath, ImageFormat.Tiff);
    SaveWithSystemDrawing(bitmap, staticGifPath, ImageFormat.Gif);
    WriteIco(bitmap, icoPath);
    File.Copy(animatedGifSource, animatedGifPath, overwrite: true);
    File.WriteAllText(svgPath, """
<svg xmlns="http://www.w3.org/2000/svg" width="96" height="64" viewBox="0 0 96 64">
  <rect width="96" height="64" fill="#f6f6f6"/>
  <circle cx="24" cy="32" r="18" fill="#2e86de"/>
  <rect x="42" y="14" width="38" height="36" rx="6" fill="#ff7f50"/>
</svg>
""");

    WritePdf(singlePagePdfPath, [bitmap]);
    using var secondBitmap = CreateAlternateBitmap();
    WritePdf(multiPagePdfPath, [bitmap, secondBitmap]);

    return new AssetPaths(
        pngPath,
        jpegPath,
        webpPath,
        bmpPath,
        tiffPath,
        icoPath,
        svgPath,
        staticGifPath,
        animatedGifPath,
        singlePagePdfPath,
        multiPagePdfPath);
}

static async Task<ConversionSummary> ConvertAsync(
    IReadOnlyList<string> inputPaths,
    Action<ConversionOptions> configureOptions,
    Action<ConversionProgress>? onProgress = null)
{
    var service = new ImageConversionService();
    var options = new ConversionOptions
    {
        OutputFormat = OutputImageFormat.Png,
        CompressionMode = CompressionMode.Custom,
        Quality = 90,
        ResizeMode = ResizeMode.None,
        RenameMode = RenameMode.KeepOriginal,
        OutputDirectoryRule = OutputDirectoryRule.SourceFolder,
        AllowOverwrite = false,
        SvgUseBackground = false,
        SvgBackgroundColor = "#FFFFFFFF",
        GifHandlingMode = GifHandlingMode.FirstFrame,
        PdfImageExportMode = PdfImageExportMode.AllPages,
        PdfDocumentExportMode = PdfDocumentExportMode.AllPages,
        MaxDegreeOfParallelism = 4
    };

    configureOptions(options);

    var items = new List<ImageItemViewModel>();
    try
    {
        foreach (var inputPath in inputPaths)
        {
            var item = CreateInputItem(inputPath);
            items.Add(item);
        }

        var progress = onProgress is null ? null : new Progress<ConversionProgress>(onProgress);
        return await service.ConvertAsync(items, options, progress);
    }
    finally
    {
        foreach (var item in items)
        {
            item.Dispose();
        }
    }
}

static ImageItemViewModel CreateInputItem(string path)
{
    if (PdfImportService.IsPdfFile(path))
    {
        var pdfImport = new PdfImportService();
        if (pdfImport.TryCreate(path, out var item, out var error, generateThumbnail: false) && item is not null)
        {
            return item;
        }

        throw new InvalidOperationException($"Failed to import PDF '{path}': {error}");
    }

    if (ImageItemViewModel.TryCreate(path, out var rasterItem, out var rasterError, generateThumbnail: false) && rasterItem is not null)
    {
        return rasterItem;
    }

    throw new InvalidOperationException($"Failed to import image '{path}': {rasterError}");
}

static void WritePdf(string destinationPath, IReadOnlyList<SKBitmap> bitmaps)
{
    var pdfExport = new PdfExportService();
    var pages = new List<PdfExportService.RenderedJpegPage>(bitmaps.Count);

    foreach (var bitmap in bitmaps)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        if (data is null)
        {
            throw new InvalidOperationException("Failed to encode JPEG page for PDF generation.");
        }

        pages.Add(new PdfExportService.RenderedJpegPage(data.ToArray(), bitmap.Width, bitmap.Height));
    }

    File.WriteAllBytes(destinationPath, pdfExport.CreatePdfFromJpegs(pages));
}

static SKBitmap CreateBaseBitmap()
{
    var bitmap = new SKBitmap(96, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(new SKColor(245, 247, 250));

    using var blue = new SKPaint { Color = new SKColor(46, 134, 222), IsAntialias = true };
    using var coral = new SKPaint { Color = new SKColor(255, 127, 80), IsAntialias = true };
    using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };

    canvas.DrawCircle(24, 32, 18, blue);
    canvas.DrawRoundRect(new SKRoundRect(new SKRect(42, 14, 82, 50), 8, 8), coral);
    canvas.DrawRect(new SKRect(50, 22, 74, 42), white);
    canvas.Flush();
    return bitmap;
}

static SKBitmap CreateAlternateBitmap()
{
    var bitmap = new SKBitmap(96, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(new SKColor(255, 248, 240));

    using var green = new SKPaint { Color = new SKColor(46, 204, 113), IsAntialias = true };
    using var navy = new SKPaint { Color = new SKColor(52, 73, 94), IsAntialias = true };

    canvas.DrawRoundRect(new SKRoundRect(new SKRect(12, 12, 84, 52), 10, 10), green);
    canvas.DrawCircle(48, 32, 12, navy);
    canvas.Flush();
    return bitmap;
}

static void SaveWithSkia(SKBitmap bitmap, string destinationPath, SKEncodedImageFormat format, int quality)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(format, quality);
    if (data is null)
    {
        throw new InvalidOperationException($"Failed to encode {format}.");
    }

    using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
    data.SaveTo(stream);
}

static void SaveWithSystemDrawing(SKBitmap bitmap, string destinationPath, ImageFormat format)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
    if (pngData is null)
    {
        throw new InvalidOperationException("Failed to encode PNG bridge image.");
    }

    using var memory = new MemoryStream(pngData.ToArray());
    using var drawingImage = Image.FromStream(memory, useEmbeddedColorManagement: true, validateImageData: true);
    drawingImage.Save(destinationPath, format);
}

static void WriteIco(SKBitmap bitmap, string destinationPath)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
    if (pngData is null)
    {
        throw new InvalidOperationException("Failed to encode PNG for ICO.");
    }

    var pngBytes = pngData.ToArray();
    using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
    using var writer = new BinaryWriter(stream);

    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)1);
    writer.Write(bitmap.Width >= 256 ? (byte)0 : (byte)bitmap.Width);
    writer.Write(bitmap.Height >= 256 ? (byte)0 : (byte)bitmap.Height);
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)pngBytes.Length);
    writer.Write((uint)22);
    writer.Write(pngBytes);
}

static void AssertValidRaster(string filePath)
{
    using var bitmap = SKBitmap.Decode(filePath);
    Assert(bitmap is not null, $"Raster output '{filePath}' could not be decoded.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void RecreateDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }

    Directory.CreateDirectory(path);
}

sealed record TestResult(string Name, bool Passed, string Details);

sealed record AssetPaths(
    string Png,
    string Jpeg,
    string Webp,
    string Bmp,
    string Tiff,
    string Ico,
    string Svg,
    string StaticGif,
    string AnimatedGif,
    string SinglePagePdf,
    string MultiPagePdf);

