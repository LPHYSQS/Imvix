using ImvixPro.Models;
using ImvixPro.Services;

var workspaceRoot = Directory.GetCurrentDirectory();
var gifPath = Path.Combine(workspaceRoot, "obj", "obj", "CodexGifTrimVerifier", "runtime", "source.gif");
var runtimeRoot = Path.Combine(workspaceRoot, "obj", "CodexGifPdfVerifierCollisionRuntime");
var outDir = Path.Combine(runtimeRoot, "all");

if (Directory.Exists(runtimeRoot))
{
    Directory.Delete(runtimeRoot, recursive: true);
}

Directory.CreateDirectory(outDir);

if (!ImageItemViewModel.TryCreate(gifPath, out var item, out var error, generateThumbnail: false) || item is null)
{
    throw new InvalidOperationException($"Failed to create image item: {error}");
}

try
{
    var service = new ImageConversionService();
    var options = new ConversionOptions
    {
        OutputFormat = OutputImageFormat.Pdf,
        OutputDirectoryRule = OutputDirectoryRule.SpecificFolder,
        OutputDirectory = outDir,
        GifHandlingMode = GifHandlingMode.AllFrames
    };

    var first = await service.ConvertAsync([item], options, progress: null);
    var second = await service.ConvertAsync([item], options, progress: null);

    Console.WriteLine(string.Join(Environment.NewLine, Directory.GetDirectories(outDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
    Console.WriteLine($"FIRST={string.Join(';', first.OutputDirectories)}");
    Console.WriteLine($"SECOND={string.Join(';', second.OutputDirectories)}");
}
finally
{
    item.Dispose();
}
