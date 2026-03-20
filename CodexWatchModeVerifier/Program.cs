using ImvixPro.Models;
using ImvixPro.Services;
using ImvixPro.Services.PdfModule;
using ImvixPro.ViewModels;
using SkiaSharp;
using System.Reflection;

var workspaceRoot = Directory.GetCurrentDirectory();
var runtimeRoot = Path.Combine(workspaceRoot, "obj", "CodexWatchModeVerifierRuntime");
var assetsRoot = Path.Combine(runtimeRoot, "assets");
var outputsRoot = Path.Combine(runtimeRoot, "outputs");

RecreateDirectory(runtimeRoot);
Directory.CreateDirectory(assetsRoot);
Directory.CreateDirectory(outputsRoot);

var animatedGifSource = ResolveAnimatedGifSource(workspaceRoot);
var animatedGifPath = Path.Combine(assetsRoot, "animated.gif");
File.Copy(animatedGifSource, animatedGifPath, overwrite: true);

using var firstPage = CreateBaseBitmap();
using var secondPage = CreateAlternateBitmap();
var multiPagePdfPath = Path.Combine(assetsRoot, "document.pdf");
WritePdf(multiPagePdfPath, [firstPage, secondPage]);

var results = new List<TestResult>();

await RunTestAsync("WatchModeAnimatedGifOverridesFirstFrameFallback", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "gif");
    Directory.CreateDirectory(outputDir);

    using var item = CreateInputItem(animatedGifPath);
    var options = CreateDefaultOptions();
    options.OutputFormat = OutputImageFormat.Png;
    options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
    options.OutputDirectory = outputDir;
    options.GifHandlingMode = GifHandlingMode.FirstFrame;

    ApplyWatchContentAwareOptions(item, options);
    Assert(options.GifHandlingMode == GifHandlingMode.AllFrames, "Animated GIF watch mode should force all-frame export.");

    var summary = await ConvertItemsAsync([item], options);
    Assert(summary.FailureCount == 0, $"Expected 0 failures, got {summary.FailureCount}.");

    var frameDir = Path.Combine(outputDir, "animated");
    Assert(Directory.Exists(frameDir), $"Expected GIF output folder '{frameDir}'.");

    var files = Directory.GetFiles(frameDir, "*.png", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(files.SequenceEqual(["frame_1.png", "frame_2.png", "frame_3.png", "frame_4.png"]),
        $"Unexpected GIF frame names: {string.Join(", ", files)}");
}, results);

await RunTestAsync("WatchModePdfOverridesCurrentPageFallback", async () =>
{
    var outputDir = Path.Combine(outputsRoot, "pdf");
    Directory.CreateDirectory(outputDir);

    using var item = CreateInputItem(multiPagePdfPath);
    var options = CreateDefaultOptions();
    options.OutputFormat = OutputImageFormat.Png;
    options.OutputDirectoryRule = OutputDirectoryRule.SpecificFolder;
    options.OutputDirectory = outputDir;
    options.PdfImageExportMode = PdfImageExportMode.CurrentPage;

    ApplyWatchContentAwareOptions(item, options);
    Assert(options.PdfImageExportMode == PdfImageExportMode.AllPages, "Multi-page PDF watch mode should force all-page export.");

    var summary = await ConvertItemsAsync([item], options);
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

static string ResolveAnimatedGifSource(string workspaceRoot)
{
    var candidates = new[]
    {
        Path.Combine(workspaceRoot, "obj", "obj", "CodexGifTrimVerifier", "runtime", "source.gif"),
        Path.Combine(workspaceRoot, "obj", "CodexQaVerifier", "runtime", "assets", "animated.gif")
    };

    var match = candidates.FirstOrDefault(File.Exists);
    return match ?? throw new FileNotFoundException("Animated GIF verifier asset not found.", candidates[0]);
}

static ConversionOptions CreateDefaultOptions()
{
    return new ConversionOptions
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
}

static async Task<ConversionSummary> ConvertItemsAsync(
    IReadOnlyList<ImageItemViewModel> items,
    ConversionOptions options)
{
    var service = new ImageConversionService();
    return await service.ConvertAsync(items, options, progress: null);
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

static void ApplyWatchContentAwareOptions(ImageItemViewModel item, ConversionOptions options)
{
    var method = typeof(MainWindowViewModel).GetMethod(
        "ApplyWatchContentAwareOptions",
        BindingFlags.NonPublic | BindingFlags.Static);

    Assert(method is not null, "Could not find watch option normalization method.");
    method!.Invoke(null, [item, options]);
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
