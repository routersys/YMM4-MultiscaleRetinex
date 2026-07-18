using System.Diagnostics;
using ComputeSharp;
using MultiscaleRetinex;

var width = 1280;
var height = 720;
var outputDirectory = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : Path.Combine(AppContext.BaseDirectory, "harness-output");
Directory.CreateDirectory(outputDirectory);

using var pipeline = MultiscaleRetinexPipeline.TryCreate();
if (pipeline is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

var source = CreateTestImage(width, height);
var destination = new int[source.Length];

if (args.Contains("--golden"))
{
    var goldenCases = new (string Name, MultiscaleRetinexPipeline.Parameters Parameters)[]
    {
        ("balanced-default", new(MultiscaleRetinexQuality.Balanced, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, 1f)),
        ("high-default", new(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, 1f)),
        ("ultra-default", new(MultiscaleRetinexQuality.Ultra, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, 1f)),
        ("chromaticity", new(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.Chromaticity, 0.03f, 0.49f, 0.4f, 0f, 1f)),
        ("contrast-max", new(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 1f, 0f, 1f)),
        ("restoration-zero", new(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, 0f)),
        ("narrow-scales", new(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.005f, 0.1f, 0.4f, 0f, 1f)),
        ("brightness-plus", new(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0.5f, 1f)),
    };
    foreach (var (name, goldenParameters) in goldenCases)
    {
        var parameters = goldenParameters;
        pipeline.Process(source, destination, width, height, in parameters);
        var bytes = new byte[destination.Length * sizeof(int)];
        Buffer.BlockCopy(destination, 0, bytes, 0, bytes.Length);
        Console.WriteLine($"{name}: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
    }
    return 0;
}

foreach (var quality in new[] { MultiscaleRetinexQuality.Balanced, MultiscaleRetinexQuality.High, MultiscaleRetinexQuality.Ultra })
{
    var parameters = new MultiscaleRetinexPipeline.Parameters(quality, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, 1f);
    pipeline.Process(source, destination, width, height, in parameters);
    pipeline.Process(source, destination, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    const int frames = 5;
    for (var frame = 0; frame < frames; frame++)
        pipeline.Process(source, destination, width, height, in parameters);
    stopwatch.Stop();
    Console.WriteLine($"{quality}: {stopwatch.Elapsed.TotalMilliseconds / frames:F2} ms/frame ({width}x{height})");
}

{
    var device = GraphicsDevice.GetDefault();
    using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
    var pixels = new Bgra32[source.Length];
    for (var index = 0; index < source.Length; index++)
        pixels[index].PackedValue = unchecked((uint)source[index]);
    sourceTexture.CopyFrom(pixels);
    var parameters = new MultiscaleRetinexPipeline.Parameters(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, 1f);

    pipeline.Simulate(sourceTexture, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    pipeline.Simulate(sourceTexture, width, height, parameters with { GlobalScale = 0.5f });
    stopwatch.Stop();
    Console.WriteLine($"structure recompute: {stopwatch.Elapsed.TotalMilliseconds:F2} ms");

    stopwatch.Restart();
    const int hitFrames = 200;
    for (var frame = 0; frame < hitFrames; frame++)
        pipeline.Simulate(sourceTexture, width, height, parameters with { GlobalScale = 0.5f });
    stopwatch.Stop();
    Console.WriteLine($"cache-hit simulate: {stopwatch.Elapsed.TotalMilliseconds / hitFrames:F3} ms/frame");

    if (pipeline.TryGetVisibleBounds(width, height, out var rect))
    {
        using var rectOutput = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(sourceTexture, rectOutput, width, height, rect, in parameters);
        pipeline.WaitForCompletion();
        stopwatch.Restart();
        const int rectFrames = 20;
        for (var frame = 0; frame < rectFrames; frame++)
        {
            pipeline.Simulate(sourceTexture, width, height, in parameters);
            pipeline.TryGetVisibleBounds(width, height, out rect);
            pipeline.RenderVisible(sourceTexture, rectOutput, width, height, rect, in parameters);
        }
        pipeline.WaitForCompletion();
        stopwatch.Stop();
        Console.WriteLine($"cached frame with rect {rect.Width}x{rect.Height} at ({rect.X},{rect.Y}): {stopwatch.Elapsed.TotalMilliseconds / rectFrames:F2} ms/frame");
    }
}

foreach (var contrast in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
{
    var parameters = new MultiscaleRetinexPipeline.Parameters(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, contrast, 0f, 1f);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"contrast={contrast:F2}");
    WriteBmp(Path.Combine(outputDirectory, $"contrast{(int)(contrast * 100):D3}.bmp"), destination, width, height);
}

foreach (var restoration in new[] { 0f, 0.5f, 1f })
{
    var parameters = new MultiscaleRetinexPipeline.Parameters(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, 0.49f, 0.4f, 0f, restoration);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"restoration={restoration:F2}");
    WriteBmp(Path.Combine(outputDirectory, $"restoration{(int)(restoration * 100):D3}.bmp"), destination, width, height);
}

foreach (var mode in new[] { MultiscaleRetinexMode.ColorConstancy, MultiscaleRetinexMode.Chromaticity })
{
    var parameters = new MultiscaleRetinexPipeline.Parameters(MultiscaleRetinexQuality.High, mode, 0.03f, 0.49f, 0.4f, 0f, 1f);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"mode={mode}");
    WriteBmp(Path.Combine(outputDirectory, $"mode-{mode}.bmp".ToLowerInvariant()), destination, width, height);
}

foreach (var globalScale in new[] { 0.1f, 0.49f, 1f })
{
    var parameters = new MultiscaleRetinexPipeline.Parameters(MultiscaleRetinexQuality.High, MultiscaleRetinexMode.ColorConstancy, 0.03f, globalScale, 0.4f, 0f, 1f);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"globalScale={globalScale:F2}");
    WriteBmp(Path.Combine(outputDirectory, $"scale{(int)(globalScale * 100):D3}.bmp"), destination, width, height);
}

WriteBmp(Path.Combine(outputDirectory, "source.bmp"), source, width, height);
Console.WriteLine($"images written to {outputDirectory}");
return 0;

static int[] CreateTestImage(int width, int height)
{
    var pixels = new int[width * height];
    for (var y = 40; y < height - 40; y++)
    {
        for (var x = 40; x < width - 40; x++)
        {
            var lighting = 0.12 + 0.88 * Math.Exp(-Math.Pow((x - width * 0.72) / (width * 0.16), 2) - Math.Pow((y - height * 0.3) / (height * 0.22), 2));
            var texture = 1.0 + 0.25 * Math.Sin(x * 0.35) * Math.Sin(y * 0.35);
            var reflectanceR = (0.35 + 0.4 * ((x / 48 + y / 48) & 1)) * texture;
            var reflectanceG = (0.3 + 0.35 * ((x / 32) & 1)) * texture;
            var reflectanceB = (0.28 + 0.3 * ((y / 40) & 1)) * texture;
            var castR = 1.15;
            var castB = 0.8;
            var r = (int)Math.Clamp(255.0 * lighting * reflectanceR * castR, 0, 255);
            var g = (int)Math.Clamp(255.0 * lighting * reflectanceG, 0, 255);
            var b = (int)Math.Clamp(255.0 * lighting * reflectanceB * castB, 0, 255);
            pixels[y * width + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
        }
    }
    return pixels;
}

static void WriteBmp(string path, int[] pixels, int width, int height)
{
    var stride = width * 3;
    var padding = (4 - stride % 4) % 4;
    var dataSize = (stride + padding) * height;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(54 + dataSize);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((short)1);
    writer.Write((short)24);
    writer.Write(0);
    writer.Write(dataSize);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);
    var pad = new byte[padding];
    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            writer.Write((byte)(pixel & 255));
            writer.Write((byte)((pixel >> 8) & 255));
            writer.Write((byte)((pixel >> 16) & 255));
        }
        writer.Write(pad);
    }
}
