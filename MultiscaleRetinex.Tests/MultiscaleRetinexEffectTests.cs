using System.Runtime.InteropServices;
using ComputeSharp;
using ComputeSharp.Interop;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace MultiscaleRetinex.Tests;

public sealed class MultiscaleRetinexEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static MultiscaleRetinexPipeline.Parameters CreateParameters(
        MultiscaleRetinexQuality quality = MultiscaleRetinexQuality.Balanced,
        MultiscaleRetinexMode mode = MultiscaleRetinexMode.ColorConstancy,
        float localScale = 0.03f,
        float globalScale = 0.49f,
        float contrast = 0.4f,
        float brightness = 0f,
        float colorRestoration = 1f)
        => new(quality, mode, localScale, globalScale, contrast, brightness, colorRestoration);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new MultiscaleRetinexEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(3d, ValueAt(effect.LocalScale), 6);
        Assert.Equal(49d, ValueAt(effect.GlobalScale), 6);
        Assert.Equal(40d, ValueAt(effect.Contrast), 6);
        Assert.Equal(0d, ValueAt(effect.Brightness), 6);
        Assert.Equal(100d, ValueAt(effect.ColorRestoration), 6);
        Assert.Equal(MultiscaleRetinexQuality.High, effect.Quality);
        Assert.Equal(MultiscaleRetinexMode.ColorConstancy, effect.Mode);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new MultiscaleRetinexEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Theory]
    [InlineData(MultiscaleRetinexQuality.Balanced, 3, 4f)]
    [InlineData(MultiscaleRetinexQuality.High, 4, 6f)]
    [InlineData(MultiscaleRetinexQuality.Ultra, 5, 8f)]
    public void QualitySettingsMatchSpecification(MultiscaleRetinexQuality quality, int scaleCount, float sigmaBase)
    {
        var settings = MultiscaleRetinexSettings.GetQuality(quality);

        Assert.Equal(scaleCount, settings.ScaleCount);
        Assert.Equal(sigmaBase, settings.SigmaBase);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(8, 8, 3)]
    [InlineData(1920, 1080, 11)]
    [InlineData(4096, 4096, 12)]
    [InlineData(8192, 8192, 13)]
    public void LevelCountReachesMinimumTopSize(int width, int height, int expected)
    {
        Assert.Equal(expected, MultiscaleRetinexSettings.GetLevelCount(width, height));
    }

    [Fact]
    public void ScaleSigmaIsGeometricAndBounded()
    {
        Assert.Equal(10f, MultiscaleRetinexSettings.GetScaleSigma(0, 3, 10f, 100f), 4);
        Assert.Equal(100f, MultiscaleRetinexSettings.GetScaleSigma(2, 3, 10f, 100f), 3);
        Assert.Equal(MathF.Sqrt(1000f), MultiscaleRetinexSettings.GetScaleSigma(1, 3, 10f, 100f), 3);
        Assert.Equal(100f, MultiscaleRetinexSettings.GetScaleSigma(0, 1, 10f, 100f), 3);
        Assert.Equal(50f, MultiscaleRetinexSettings.GetScaleSigma(2, 3, 50f, 20f), 3);
        Assert.Equal(MultiscaleRetinexSettings.MinimumSigmaPixels, MultiscaleRetinexSettings.GetScaleSigma(0, 3, 0f, 0f), 4);
        Assert.True(MultiscaleRetinexSettings.GetScaleSigma(1, 4, 10f, 100f) < MultiscaleRetinexSettings.GetScaleSigma(2, 4, 10f, 100f));
    }

    [Fact]
    public void LevelSelectionHalvesSigmaIntoBaseRange()
    {
        Assert.Equal(0, MultiscaleRetinexSettings.GetLevelForSigma(4f, 6f, 10));
        Assert.Equal(0, MultiscaleRetinexSettings.GetLevelForSigma(6f, 6f, 10));
        Assert.Equal(3, MultiscaleRetinexSettings.GetLevelForSigma(48f, 6f, 10));
        Assert.Equal(2, MultiscaleRetinexSettings.GetLevelForSigma(47f, 6f, 10));
        Assert.Equal(4, MultiscaleRetinexSettings.GetLevelForSigma(1000f, 6f, 5));
        for (var level = 0; level < 8; level++)
        {
            var sigma = 6f * (1 << level) * 1.5f;
            var chosen = MultiscaleRetinexSettings.GetLevelForSigma(sigma, 6f, 16);
            var levelSigma = sigma / (1 << chosen);
            Assert.InRange(levelSigma, 6f, 12f);
        }
    }

    [Fact]
    public void KernelRadiusCoversThreeSigmaAndIsCapped()
    {
        Assert.Equal(1, MultiscaleRetinexSettings.GetKernelRadius(0.1f));
        Assert.Equal(18, MultiscaleRetinexSettings.GetKernelRadius(6f));
        Assert.Equal(MultiscaleRetinexSettings.MaximumKernelRadius, MultiscaleRetinexSettings.GetKernelRadius(1000f));
    }

    [Fact]
    public void ParameterMappingsAreMonotonicAndBounded()
    {
        Assert.Equal(MultiscaleRetinexSettings.MinimumGain, MultiscaleRetinexSettings.GetGain(0f), 6);
        Assert.Equal(MultiscaleRetinexSettings.MaximumGain, MultiscaleRetinexSettings.GetGain(1f), 6);
        Assert.Equal(MultiscaleRetinexSettings.MinimumGain, MultiscaleRetinexSettings.GetGain(-5f), 6);
        Assert.Equal(MultiscaleRetinexSettings.MaximumGain, MultiscaleRetinexSettings.GetGain(5f), 6);
        Assert.True(MultiscaleRetinexSettings.GetGain(0.75f) > MultiscaleRetinexSettings.GetGain(0.25f));

        Assert.Equal(0f, MultiscaleRetinexSettings.GetBrightnessOffset(0f), 6);
        Assert.Equal(MultiscaleRetinexSettings.MaximumBrightnessOffset, MultiscaleRetinexSettings.GetBrightnessOffset(1f), 6);
        Assert.Equal(-MultiscaleRetinexSettings.MaximumBrightnessOffset, MultiscaleRetinexSettings.GetBrightnessOffset(-1f), 6);
        Assert.Equal(MultiscaleRetinexSettings.MaximumBrightnessOffset, MultiscaleRetinexSettings.GetBrightnessOffset(5f), 6);

        Assert.Equal(MultiscaleRetinexSettings.MinimumLocalScale * 1000f, MultiscaleRetinexSettings.GetLocalSigma(0f, 1000), 4);
        Assert.Equal(MultiscaleRetinexSettings.MaximumLocalScale * 1000f, MultiscaleRetinexSettings.GetLocalSigma(5f, 1000), 3);
        Assert.Equal(30f, MultiscaleRetinexSettings.GetLocalSigma(0.03f, 1000), 3);
        Assert.Equal(MultiscaleRetinexSettings.MinimumGlobalScale * 1000f, MultiscaleRetinexSettings.GetGlobalSigma(0f, 1000), 3);
        Assert.Equal(MultiscaleRetinexSettings.MaximumGlobalScale * 1000f, MultiscaleRetinexSettings.GetGlobalSigma(5f, 1000), 2);
        Assert.Equal(490f, MultiscaleRetinexSettings.GetGlobalSigma(0.49f, 1000), 2);
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(contrast: 0.8f, brightness: 0.2f);

        pipeline.Process(source, first, width, height, in parameters);
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentScalesProduceDifferentOutput()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(globalScale: 0.1f);
        var parametersB = CreateParameters(globalScale: 1f);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DifferentModesProduceDifferentOutput()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(mode: MultiscaleRetinexMode.ColorConstancy);
        var parametersB = CreateParameters(mode: MultiscaleRetinexMode.Chromaticity);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OutputAlphaMatchesInputAndStaysPremultiplied()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters(contrast: 1f, brightness: 0.5f);

        pipeline.Process(source, destination, width, height, in parameters);

        for (var index = 0; index < source.Length; index++)
        {
            var pixel = destination[index];
            var alpha = (pixel >> 24) & 255;
            Assert.Equal((source[index] >> 24) & 255, alpha);
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void ContrastIncreasesLocalVariance()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var soft = new int[source.Length];
        var hard = new int[source.Length];

        pipeline.Process(source, soft, width, height, CreateParameters(contrast: 0.1f));
        pipeline.Process(source, hard, width, height, CreateParameters(contrast: 0.7f));

        Assert.True(InteriorLuminanceVariance(hard, width, height) > InteriorLuminanceVariance(soft, width, height));
    }

    [Fact]
    public void BrightnessRaisesMeanLuminance()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var neutral = new int[source.Length];
        var brighter = new int[source.Length];

        pipeline.Process(source, neutral, width, height, CreateParameters(brightness: 0f));
        pipeline.Process(source, brighter, width, height, CreateParameters(brightness: 0.6f));

        Assert.True(InteriorLuminanceMean(brighter, width, height) > InteriorLuminanceMean(neutral, width, height));
    }

    [Fact]
    public void ChromaticityModePreservesChannelRatios()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = new int[width * height];
        for (var y = 8; y < height - 8; y++)
        {
            for (var x = 8; x < width - 8; x++)
            {
                var value = 40 + 120 * x / width;
                var r = value;
                var g = value / 2;
                var b = value / 4;
                source[y * width + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
            }
        }
        var destination = new int[source.Length];
        var parameters = CreateParameters(mode: MultiscaleRetinexMode.Chromaticity);

        pipeline.Process(source, destination, width, height, in parameters);

        for (var y = 16; y < height - 16; y++)
        {
            for (var x = 16; x < width - 16; x++)
            {
                var pixel = destination[y * width + x];
                var r = (pixel >> 16) & 255;
                var g = (pixel >> 8) & 255;
                var b = pixel & 255;
                if (r < 32 || r > 250)
                    continue;
                Assert.InRange(g, r / 2 - 3, r / 2 + 3);
                Assert.InRange(b, r / 4 - 3, r / 4 + 3);
            }
        }
    }

    [Fact]
    public void ColorConstancyEqualizesGrayWorldCast()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = new int[width * height];
        for (var y = 8; y < height - 8; y++)
        {
            for (var x = 8; x < width - 8; x++)
            {
                var texture = ((x / 4 + y / 4) & 1) == 0 ? 30 : -30;
                var r = Math.Clamp(170 + texture, 0, 255);
                var g = Math.Clamp(120 + texture, 0, 255);
                var b = Math.Clamp(80 + texture, 0, 255);
                source[y * width + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
            }
        }
        var destination = new int[source.Length];
        var parameters = CreateParameters(colorRestoration: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        double sumR = 0d;
        double sumG = 0d;
        double sumB = 0d;
        double sourceR = 0d;
        double sourceG = 0d;
        double sourceB = 0d;
        var count = 0;
        for (var y = 16; y < height - 16; y++)
        {
            for (var x = 16; x < width - 16; x++)
            {
                var pixel = destination[y * width + x];
                sumR += (pixel >> 16) & 255;
                sumG += (pixel >> 8) & 255;
                sumB += pixel & 255;
                var original = source[y * width + x];
                sourceR += (original >> 16) & 255;
                sourceG += (original >> 8) & 255;
                sourceB += original & 255;
                count++;
            }
        }
        var castBefore = (sourceR - sourceB) / count;
        var castAfter = (sumR - sumB) / count;
        Assert.True(castBefore > 60d);
        Assert.True(Math.Abs(castAfter) < castBefore * 0.5d);
    }

    [Fact]
    public void GpuPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateTexturedSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);
        pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateTexturedSource(width, height);
        var expected = new int[source.Length];
        var parameters = CreateParameters(contrast: 0.7f, brightness: -0.2f);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineAllocationsAmortizeToZero()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, width, height, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void VisibleBoundsCoverAllLitPixelsAndMatchFullRender()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateOffsetSource(width, height, 40, 56, 72, 48);
        var full = new int[source.Length];
        var parameters = CreateParameters(contrast: 0.8f, brightness: 0.1f);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        pipeline.Simulate(sourceTexture, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width, height, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width && rect.Y + rect.Height <= height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (((source[y * width + x] >> 24) & 255) == 0)
                    continue;
                Assert.InRange(x, rect.X, rect.X + rect.Width - 1);
                Assert.InRange(y, rect.Y, rect.Y + rect.Height - 1);
            }
        }

        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(sourceTexture, outputTexture, width, height, rect, in parameters);
        var result = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(result);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var expected = unchecked((uint)full[(rect.Y + y) * width + rect.X + x]);
                Assert.Equal(expected, result[y * rect.Width + x].PackedValue);
            }
        }
    }

    [Fact]
    public void SimulateCachesStructureUntilInputsChange()
    {
        using var pipeline = MultiscaleRetinexPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTexturedSource(width, height);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        var parameters = CreateParameters();
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in parameters));

        var renderOnlyChanged = parameters with { Contrast = 0.9f, Brightness = 0.3f, ColorRestoration = 0.2f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in renderOnlyChanged));

        var scaleChanged = parameters with { GlobalScale = 0.2f };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in scaleChanged));

        var modeChanged = scaleChanged with { Mode = MultiscaleRetinexMode.Chromaticity };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in modeChanged));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in modeChanged));

        var movedSource = CreateOffsetSource(width, height, 16, 16, 64, 64);
        for (var index = 0; index < movedSource.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)movedSource[index]);
        sourceTexture.CopyFrom(sourcePixels);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in modeChanged));
    }

    [Fact]
    public void Direct2DInteropProducesFilteredOutput()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var interop = MultiscaleRetinexGpuInterop.TryCreate(graphicsContext);
        if (interop is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var pipeline = MultiscaleRetinexPipeline.TryCreate(interop.Device);
        Assert.NotNull(pipeline);

        const int width = 96;
        const int height = 96;
        var pixels = CreateTexturedSource(width, height);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        using var inputBitmap = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }

        Assert.True(interop.EnsureResources(width, height));
        var bounds = new RawRectF(0f, 0f, width, height);
        var parameters = CreateParameters(contrast: 0.8f);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            interop.RenderInput(inputBitmap, bounds);
            interop.BeginCompute();
            try
            {
                pipeline!.Process(interop.SourceTexture, interop.OutputTexture, width, height, in parameters);
            }
            finally
            {
                interop.EndCompute();
            }
        }
        interop.WaitForIdle();

        using var staging = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        staging.CopyFromBitmap(interop.OutputBitmap);
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    if (alpha > 0)
                        lit++;
                }
            }
            Assert.True(lit > 0);
        }
        finally
        {
            staging.Unmap();
        }
    }

    private static int[] CreateTexturedSource(int width, int height)
    {
        var source = new int[width * height];
        for (var y = 8; y < height - 8; y++)
        {
            for (var x = 8; x < width - 8; x++)
            {
                var gradient = 96 + 64 * x / width;
                var texture = ((x / 4 + y / 4) & 1) == 0 ? 20 : -20;
                var value = Math.Clamp(gradient + texture, 0, 255);
                var r = value;
                var g = Math.Clamp(value - 16, 0, 255);
                var b = Math.Clamp(value - 32, 0, 255);
                source[y * width + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
            }
        }
        return source;
    }

    private static int[] CreateOffsetSource(int width, int height, int left, int top, int rectWidth, int rectHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + rectHeight; y++)
        {
            for (var x = left; x < left + rectWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                var texture = ((x / 4 + y / 4) & 1) == 0 ? 160 : 96;
                source[y * width + x] = unchecked((int)0xFF000000) | texture << 16 | texture << 8 | texture;
            }
        }
        return source;
    }

    private static double InteriorLuminanceVariance(int[] pixels, int width, int height)
    {
        var sum = 0d;
        var squares = 0d;
        var count = 0;
        for (var y = 16; y < height - 16; y++)
        {
            for (var x = 16; x < width - 16; x++)
            {
                var pixel = pixels[y * width + x];
                var luminance = 0.2126 * ((pixel >> 16) & 255) + 0.7152 * ((pixel >> 8) & 255) + 0.0722 * (pixel & 255);
                sum += luminance;
                squares += luminance * luminance;
                count++;
            }
        }
        var mean = sum / count;
        return squares / count - mean * mean;
    }

    private static double InteriorLuminanceMean(int[] pixels, int width, int height)
    {
        var sum = 0d;
        var count = 0;
        for (var y = 16; y < height - 16; y++)
        {
            for (var x = 16; x < width - 16; x++)
            {
                var pixel = pixels[y * width + x];
                sum += 0.2126 * ((pixel >> 16) & 255) + 0.7152 * ((pixel >> 8) & 255) + 0.0722 * (pixel & 255);
                count++;
            }
        }
        return sum / count;
    }
}
