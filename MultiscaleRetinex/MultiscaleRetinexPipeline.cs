using System.Runtime.InteropServices;
using ComputeSharp;

namespace MultiscaleRetinex;

internal sealed class MultiscaleRetinexPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly int[] _levelOffsets;
    private readonly int[] _levelWidths;
    private readonly int[] _levelHeights;
    private ReadWriteBuffer<Float4>? _pyramid;
    private ReadWriteBuffer<Float4>? _blurred;
    private ReadWriteBuffer<Float4>? _surround;
    private ReadWriteBuffer<Float3>? _retinex;
    private StructureKey? _structureKey;
    private int _width;
    private int _height;
    private int _levelCount;
    private int _cachedMinX;
    private int _cachedMinY;
    private int _cachedMaxX;
    private int _cachedMaxY;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private MultiscaleRetinexPipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(MultiscaleRetinexSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(MultiscaleRetinexSettings.ScratchLength);
        _levelOffsets = new int[MultiscaleRetinexSettings.MaximumLevelCount];
        _levelWidths = new int[MultiscaleRetinexSettings.MaximumLevelCount];
        _levelHeights = new int[MultiscaleRetinexSettings.MaximumLevelCount];
    }

    public static MultiscaleRetinexPipeline? TryCreate()
    {
        try
        {
            return new MultiscaleRetinexPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static MultiscaleRetinexPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new MultiscaleRetinexPipeline(device);
        }
        catch
        {
            return null;
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureGrid(width, height);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        using (ComputeContext context = _device.CreateComputeContext())
            RecordFullPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGrid(width, height);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGrid(width, height);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
    }

    internal bool Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGrid(width, height);
        using (ComputeContext context = _device.CreateComputeContext())
            RecordHashStage(in context, source, width, height);
        _scratchReadBack.CopyFrom(_scratch);
        var hashed = _scratchReadBack.Span;
        _cachedMinX = hashed[MultiscaleRetinexSettings.ScratchMinX];
        _cachedMinY = hashed[MultiscaleRetinexSettings.ScratchMinY];
        _cachedMaxX = hashed[MultiscaleRetinexSettings.ScratchMaxX];
        _cachedMaxY = hashed[MultiscaleRetinexSettings.ScratchMaxY];
        var key = new StructureKey(
            hashed[MultiscaleRetinexSettings.ScratchHashSum],
            hashed[MultiscaleRetinexSettings.ScratchHashMix],
            width,
            height,
            parameters.Quality,
            parameters.Mode,
            parameters.LocalScale,
            parameters.GlobalScale);
        if (_structureKey == key)
            return false;

        var derived = Derive(width, height, in parameters);
        using (ComputeContext context = _device.CreateComputeContext())
            RecordRetinexStage(in context, source, width, height, in derived);
        _structureKey = key;
        return true;
    }

    internal bool TryGetVisibleBounds(int width, int height, out PixelRect rect)
    {
        rect = default;
        if (_cachedMaxX < _cachedMinX || _cachedMaxY < _cachedMinY)
            return false;

        var left = Math.Clamp(_cachedMinX & ~3, 0, width);
        var top = Math.Clamp(_cachedMinY & ~3, 0, height);
        var right = Math.Clamp(_cachedMaxX + 1, 0, width);
        var bottom = Math.Clamp(_cachedMaxY + 1, 0, height);
        var rectWidth = Math.Min((right - left + 3) & ~3, width - left);
        var rectHeight = Math.Min((bottom - top + 3) & ~3, height - top);
        if (rectWidth <= 0 || rectHeight <= 0)
            return false;

        rect = new PixelRect(left, top, rectWidth, rectHeight);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(width, height, in parameters);
        using ComputeContext context = _device.CreateComputeContext();
        RecordRenderStage(in context, source, output, rect, width, height, in derived);
    }

    private void RecordFullPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        RecordRetinexStage(in context, source, width, height, in derived);
        RecordRenderStage(in context, source, output, new PixelRect(0, 0, width, height), width, height, in derived);
    }

    private void RecordHashStage(in ComputeContext context, ReadWriteTexture2D<Bgra32, Float4> source, int width, int height)
    {
        context.For(1, new InitScratchShader(_scratch));
        context.Barrier(_scratch);
        context.For(width, height, new HashBoundsShader(source, _scratch, width, height));
        context.Barrier(_scratch);
    }

    private void RecordRetinexStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int width,
        int height,
        in DerivedValues derived)
    {
        var pyramid = _pyramid!;
        var blurred = _blurred!;
        var surround = _surround!;
        var retinex = _retinex!;

        context.For(width, height, new LinearizeShader(source, pyramid, width, height));
        context.Barrier(pyramid);

        var deepestLevel = MultiscaleRetinexSettings.GetLevelForSigma(derived.MaximumSigma, derived.SigmaBase, _levelCount);
        for (var level = 1; level <= deepestLevel; level++)
        {
            context.For(_levelWidths[level], _levelHeights[level], new DownsampleShader(
                pyramid,
                _levelOffsets[level - 1], _levelWidths[level - 1], _levelHeights[level - 1],
                _levelOffsets[level], _levelWidths[level], _levelHeights[level]));
            context.Barrier(pyramid);
        }

        context.For(width, height, new ClearRetinexShader(retinex, width, height));
        context.Barrier(retinex);

        var weight = 1f / derived.ScaleCount;
        for (var scale = 0; scale < derived.ScaleCount; scale++)
        {
            var sigma = MultiscaleRetinexSettings.GetScaleSigma(scale, derived.ScaleCount, derived.MinimumSigma, derived.MaximumSigma);
            var level = MultiscaleRetinexSettings.GetLevelForSigma(sigma, derived.SigmaBase, _levelCount);
            var levelSigma = sigma / (1 << level);
            var radius = MultiscaleRetinexSettings.GetKernelRadius(levelSigma);
            var inverseSigmaSquared = 1f / (levelSigma * levelSigma);
            context.For(_levelWidths[level], _levelHeights[level], new BlurHorizontalShader(
                pyramid, blurred, _levelOffsets[level], _levelWidths[level], _levelHeights[level], radius, inverseSigmaSquared));
            context.Barrier(blurred);
            context.For(_levelWidths[level], _levelHeights[level], new BlurVerticalShader(
                blurred, surround, _levelWidths[level], _levelHeights[level], radius, inverseSigmaSquared));
            context.Barrier(surround);
            context.For(width, height, new AccumulateRetinexShader(
                source, surround, retinex, width, height,
                _levelWidths[level], _levelHeights[level], 1 << level, weight, derived.Mode));
            context.Barrier(retinex);
        }
    }

    private void RecordRenderStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        int width,
        int height,
        in DerivedValues derived)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            _retinex!, source, output,
            rect.X, rect.Y, rect.Width, rect.Height, width, height,
            derived.Gain, derived.BrightnessOffset, derived.ColorRestoration, derived.Mode));
    }

    private DerivedValues Derive(int width, int height, in Parameters parameters)
    {
        var settings = MultiscaleRetinexSettings.GetQuality(parameters.Quality);
        var longSide = Math.Max(Math.Max(width, height), 1);
        var minimumSigma = MultiscaleRetinexSettings.GetLocalSigma(parameters.LocalScale, longSide);
        var maximumSigma = Math.Max(MultiscaleRetinexSettings.GetGlobalSigma(parameters.GlobalScale, longSide), minimumSigma);
        return new DerivedValues(
            minimumSigma,
            maximumSigma,
            settings.ScaleCount,
            settings.SigmaBase,
            MultiscaleRetinexSettings.GetGain(parameters.Contrast),
            MultiscaleRetinexSettings.GetBrightnessOffset(parameters.Brightness),
            Math.Clamp(parameters.ColorRestoration, 0f, 1f),
            parameters.Mode == MultiscaleRetinexMode.Chromaticity ? 1 : 0);
    }

    private void EnsureGrid(int width, int height)
    {
        if (_width == width && _height == height)
            return;

        DisposeGridBuffers();
        var levelCount = MultiscaleRetinexSettings.GetLevelCount(width, height);
        var levelWidth = width;
        var levelHeight = height;
        var total = 0;
        for (var level = 0; level < levelCount; level++)
        {
            _levelOffsets[level] = total;
            _levelWidths[level] = levelWidth;
            _levelHeights[level] = levelHeight;
            total = checked(total + levelWidth * levelHeight);
            levelWidth = (levelWidth + 1) / 2;
            levelHeight = (levelHeight + 1) / 2;
        }
        var pixelCount = width * height;
        _pyramid = _device.AllocateReadWriteBuffer<Float4>(total);
        _blurred = _device.AllocateReadWriteBuffer<Float4>(pixelCount);
        _surround = _device.AllocateReadWriteBuffer<Float4>(pixelCount);
        _retinex = _device.AllocateReadWriteBuffer<Float3>(pixelCount);
        _width = width;
        _height = height;
        _levelCount = levelCount;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    private void DisposeGridBuffers()
    {
        _pyramid?.Dispose();
        _blurred?.Dispose();
        _surround?.Dispose();
        _retinex?.Dispose();
        _pyramid = null;
        _blurred = null;
        _surround = null;
        _retinex = null;
        _structureKey = null;
        _cachedMinX = 0;
        _cachedMinY = 0;
        _cachedMaxX = -1;
        _cachedMaxY = -1;
        _width = 0;
        _height = 0;
        _levelCount = 0;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct StructureKey(
        int PixelHashSum,
        int PixelHashMix,
        int Width,
        int Height,
        MultiscaleRetinexQuality Quality,
        MultiscaleRetinexMode Mode,
        float LocalScale,
        float GlobalScale);

    private readonly record struct DerivedValues(
        float MinimumSigma,
        float MaximumSigma,
        int ScaleCount,
        float SigmaBase,
        float Gain,
        float BrightnessOffset,
        float ColorRestoration,
        int Mode);

    internal readonly record struct Parameters(
        MultiscaleRetinexQuality Quality,
        MultiscaleRetinexMode Mode,
        float LocalScale,
        float GlobalScale,
        float Contrast,
        float Brightness,
        float ColorRestoration);
}
