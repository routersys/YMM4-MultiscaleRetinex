using ComputeSharp;

namespace MultiscaleRetinex;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitScratchShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[0] = 0;
        scratch[1] = 0;
        scratch[2] = 2147483647;
        scratch[3] = 2147483647;
        scratch[4] = -1;
        scratch[5] = -1;
        scratch[6] = 0;
        scratch[7] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct HashBoundsShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<int> scratch,
    int width,
    int height) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int width = width;
    private readonly int height = height;

    [GroupShared(6)]
    private static readonly int[] groupScratch = null!;

    public void Execute()
    {
        if (GroupIds.Index == 0)
        {
            groupScratch[0] = 0;
            groupScratch[1] = 0;
            groupScratch[2] = 2147483647;
            groupScratch[3] = 2147483647;
            groupScratch[4] = -1;
            groupScratch[5] = -1;
        }
        Hlsl.GroupMemoryBarrierWithGroupSync();

        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x < width && y < height)
        {
            var pixel = source[new Int2(x, y)];
            var packed = (uint)(pixel.W * 255f + 0.5f) << 24
                | (uint)(pixel.X * 255f + 0.5f) << 16
                | (uint)(pixel.Y * 255f + 0.5f) << 8
                | (uint)(pixel.Z * 255f + 0.5f);
            var mixed = packed * 0x9E3779B9u ^ (uint)(y * width + x) * 0x85EBCA6Bu;
            mixed ^= mixed >> 16;
            mixed *= 0xC2B2AE35u;
            mixed ^= mixed >> 13;
            Hlsl.InterlockedAdd(ref groupScratch[0], (int)mixed);
            Hlsl.InterlockedXor(ref groupScratch[1], (int)(mixed * 0x9E3779B9u));

            if (pixel.W > 0f)
            {
                Hlsl.InterlockedMin(ref groupScratch[2], x);
                Hlsl.InterlockedMin(ref groupScratch[3], y);
                Hlsl.InterlockedMax(ref groupScratch[4], x);
                Hlsl.InterlockedMax(ref groupScratch[5], y);
            }
        }
        Hlsl.GroupMemoryBarrierWithGroupSync();

        if (GroupIds.Index != 0)
            return;
        Hlsl.InterlockedAdd(ref scratch[0], groupScratch[0]);
        Hlsl.InterlockedXor(ref scratch[1], groupScratch[1]);
        Hlsl.InterlockedMin(ref scratch[2], groupScratch[2]);
        Hlsl.InterlockedMin(ref scratch[3], groupScratch[3]);
        Hlsl.InterlockedMax(ref scratch[4], groupScratch[4]);
        Hlsl.InterlockedMax(ref scratch[5], groupScratch[5]);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LinearizeShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<Float4> pyramid,
    int width,
    int height) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<Float4> pyramid = pyramid;
    private readonly int width = width;
    private readonly int height = height;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var pixel = source[new Int2(x, y)];
        var alpha = pixel.W;
        pyramid[y * width + x] = new Float4(
            Hlsl.Min(pixel.X, alpha),
            Hlsl.Min(pixel.Y, alpha),
            Hlsl.Min(pixel.Z, alpha),
            alpha);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DownsampleShader(
    ReadWriteBuffer<Float4> pyramid,
    int sourceOffset,
    int sourceWidth,
    int sourceHeight,
    int destinationOffset,
    int destinationWidth,
    int destinationHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> pyramid = pyramid;
    private readonly int sourceOffset = sourceOffset;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int destinationOffset = destinationOffset;
    private readonly int destinationWidth = destinationWidth;
    private readonly int destinationHeight = destinationHeight;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= destinationWidth || y >= destinationHeight)
            return;

        var x0 = Hlsl.Min(2 * x, sourceWidth - 1);
        var x1 = Hlsl.Min(2 * x + 1, sourceWidth - 1);
        var y0 = Hlsl.Min(2 * y, sourceHeight - 1);
        var y1 = Hlsl.Min(2 * y + 1, sourceHeight - 1);
        var sum = pyramid[sourceOffset + y0 * sourceWidth + x0]
            + pyramid[sourceOffset + y0 * sourceWidth + x1]
            + pyramid[sourceOffset + y1 * sourceWidth + x0]
            + pyramid[sourceOffset + y1 * sourceWidth + x1];
        pyramid[destinationOffset + y * destinationWidth + x] = sum * 0.25f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BlurHorizontalShader(
    ReadWriteBuffer<Float4> pyramid,
    ReadWriteBuffer<Float4> blurred,
    int levelOffset,
    int levelWidth,
    int levelHeight,
    int radius,
    float inverseSigmaSquared) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> pyramid = pyramid;
    private readonly ReadWriteBuffer<Float4> blurred = blurred;
    private readonly int levelOffset = levelOffset;
    private readonly int levelWidth = levelWidth;
    private readonly int levelHeight = levelHeight;
    private readonly int radius = radius;
    private readonly float inverseSigmaSquared = inverseSigmaSquared;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= levelWidth || y >= levelHeight)
            return;

        var rowOffset = levelOffset + y * levelWidth;
        var sum = new Float4(0f, 0f, 0f, 0f);
        var weightSum = 0f;
        for (var dx = -radius; dx <= radius; dx++)
        {
            var sx = Hlsl.Clamp(x + dx, 0, levelWidth - 1);
            var weight = Hlsl.Exp(-(dx * dx) * inverseSigmaSquared);
            sum += pyramid[rowOffset + sx] * weight;
            weightSum += weight;
        }
        blurred[y * levelWidth + x] = sum * (1f / weightSum);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BlurVerticalShader(
    ReadWriteBuffer<Float4> blurred,
    ReadWriteBuffer<Float4> surround,
    int levelWidth,
    int levelHeight,
    int radius,
    float inverseSigmaSquared) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> blurred = blurred;
    private readonly ReadWriteBuffer<Float4> surround = surround;
    private readonly int levelWidth = levelWidth;
    private readonly int levelHeight = levelHeight;
    private readonly int radius = radius;
    private readonly float inverseSigmaSquared = inverseSigmaSquared;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= levelWidth || y >= levelHeight)
            return;

        var sum = new Float4(0f, 0f, 0f, 0f);
        var weightSum = 0f;
        for (var dy = -radius; dy <= radius; dy++)
        {
            var sy = Hlsl.Clamp(y + dy, 0, levelHeight - 1);
            var weight = Hlsl.Exp(-(dy * dy) * inverseSigmaSquared);
            sum += blurred[sy * levelWidth + x] * weight;
            weightSum += weight;
        }
        surround[y * levelWidth + x] = sum * (1f / weightSum);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct AccumulateRetinexShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<Float4> surround,
    ReadWriteBuffer<Float3> retinex,
    int width,
    int height,
    int levelWidth,
    int levelHeight,
    float levelScale,
    float weight,
    int mode,
    int initialize) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<Float4> surround = surround;
    private readonly ReadWriteBuffer<Float3> retinex = retinex;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int levelWidth = levelWidth;
    private readonly int levelHeight = levelHeight;
    private readonly float levelScale = levelScale;
    private readonly float weight = weight;
    private readonly int mode = mode;
    private readonly int initialize = initialize;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var pixel = source[new Int2(x, y)];
        var alpha = pixel.W;
        if (alpha <= 0f)
            return;

        Float4 blurred;
        if (levelScale == 1f)
        {
            blurred = surround[y * levelWidth + x];
        }
        else
        {
            var lx = (x + 0.5f) / levelScale - 0.5f;
            var ly = (y + 0.5f) / levelScale - 0.5f;
            var ix0 = (int)Hlsl.Floor(lx);
            var iy0 = (int)Hlsl.Floor(ly);
            var fx = lx - ix0;
            var fy = ly - iy0;
            var cx0 = Hlsl.Clamp(ix0, 0, levelWidth - 1);
            var cx1 = Hlsl.Clamp(ix0 + 1, 0, levelWidth - 1);
            var cy0 = Hlsl.Clamp(iy0, 0, levelHeight - 1);
            var cy1 = Hlsl.Clamp(iy0 + 1, 0, levelHeight - 1);
            var top = Hlsl.Lerp(surround[cy0 * levelWidth + cx0], surround[cy0 * levelWidth + cx1], fx);
            var bottom = Hlsl.Lerp(surround[cy1 * levelWidth + cx0], surround[cy1 * levelWidth + cx1], fx);
            blurred = Hlsl.Lerp(top, bottom, fy);
        }
        var coverage = Hlsl.Max(blurred.W, MultiscaleRetinexSettings.LogEpsilon * MultiscaleRetinexSettings.LogEpsilon);

        var r = Hlsl.Saturate(pixel.X / alpha);
        var g = Hlsl.Saturate(pixel.Y / alpha);
        var b = Hlsl.Saturate(pixel.Z / alpha);
        var index = y * width + x;
        var accumulated = initialize != 0 ? new Float3(0f, 0f, 0f) : retinex[index];
        if (mode == 0)
        {
            var surroundR = Hlsl.Saturate(blurred.X / coverage);
            var surroundG = Hlsl.Saturate(blurred.Y / coverage);
            var surroundB = Hlsl.Saturate(blurred.Z / coverage);
            accumulated.X += weight * (Hlsl.Log(r + MultiscaleRetinexSettings.LogEpsilon) - Hlsl.Log(surroundR + MultiscaleRetinexSettings.LogEpsilon));
            accumulated.Y += weight * (Hlsl.Log(g + MultiscaleRetinexSettings.LogEpsilon) - Hlsl.Log(surroundG + MultiscaleRetinexSettings.LogEpsilon));
            accumulated.Z += weight * (Hlsl.Log(b + MultiscaleRetinexSettings.LogEpsilon) - Hlsl.Log(surroundB + MultiscaleRetinexSettings.LogEpsilon));
        }
        else
        {
            var intensity = (r + g + b) * (1f / 3f);
            var surroundIntensity = Hlsl.Saturate((blurred.X + blurred.Y + blurred.Z) / (3f * coverage));
            accumulated.X += weight * (Hlsl.Log(intensity + MultiscaleRetinexSettings.LogEpsilon) - Hlsl.Log(surroundIntensity + MultiscaleRetinexSettings.LogEpsilon));
        }
        retinex[index] = accumulated;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<Float3> retinex,
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int rectOffsetX,
    int rectOffsetY,
    int rectWidth,
    int rectHeight,
    int width,
    int height,
    float gain,
    float brightnessOffset,
    float colorRestoration,
    int mode) : IComputeShader
{
    private readonly ReadWriteBuffer<Float3> retinex = retinex;
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int rectOffsetX = rectOffsetX;
    private readonly int rectOffsetY = rectOffsetY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int width = width;
    private readonly int height = height;
    private readonly float gain = gain;
    private readonly float brightnessOffset = brightnessOffset;
    private readonly float colorRestoration = colorRestoration;
    private readonly int mode = mode;

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;
        var px = ThreadIds.X + rectOffsetX;
        var py = ThreadIds.Y + rectOffsetY;
        var pixel = source[new Int2(px, py)];
        var alpha = pixel.W;
        if (alpha <= 0f)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var r = Hlsl.Saturate(pixel.X / alpha);
        var g = Hlsl.Saturate(pixel.Y / alpha);
        var b = Hlsl.Saturate(pixel.Z / alpha);
        var values = retinex[py * width + px];

        Float3 color;
        if (mode == 0)
        {
            var channelSum = Hlsl.Max(r + g + b, MultiscaleRetinexSettings.LogEpsilon);
            var normalizer = 1f / Hlsl.Log(1f + MultiscaleRetinexSettings.CrfAlpha);
            var crfR = Hlsl.Log(1f + MultiscaleRetinexSettings.CrfAlpha * 3f * r / channelSum) * normalizer;
            var crfG = Hlsl.Log(1f + MultiscaleRetinexSettings.CrfAlpha * 3f * g / channelSum) * normalizer;
            var crfB = Hlsl.Log(1f + MultiscaleRetinexSettings.CrfAlpha * 3f * b / channelSum) * normalizer;
            var restoredR = 1f + (crfR - 1f) * colorRestoration;
            var restoredG = 1f + (crfG - 1f) * colorRestoration;
            var restoredB = 1f + (crfB - 1f) * colorRestoration;
            color = new Float3(
                Hlsl.Saturate(0.5f + gain * restoredR * values.X + brightnessOffset),
                Hlsl.Saturate(0.5f + gain * restoredG * values.Y + brightnessOffset),
                Hlsl.Saturate(0.5f + gain * restoredB * values.Z + brightnessOffset));
        }
        else
        {
            var intensity = (r + g + b) * (1f / 3f);
            var enhanced = Hlsl.Saturate(0.5f + gain * values.X + brightnessOffset);
            var ratio = enhanced / Hlsl.Max(intensity, MultiscaleRetinexSettings.LogEpsilon);
            var maxChannel = Hlsl.Max(r, Hlsl.Max(g, b));
            if (maxChannel > 0f)
                ratio = Hlsl.Min(ratio, 1f / maxChannel);
            color = new Float3(r * ratio, g * ratio, b * ratio);
        }
        output[ThreadIds.XY] = new Float4(color.X * alpha, color.Y * alpha, color.Z * alpha, alpha);
    }
}
