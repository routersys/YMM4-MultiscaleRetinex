namespace MultiscaleRetinex;

internal static class MultiscaleRetinexSettings
{
    public const float LogEpsilon = 1f / 255f;
    public const float CrfAlpha = 125f;
    public const float MinimumSigmaPixels = 1f;
    public const float MinimumLocalScale = 0.001f;
    public const float MaximumLocalScale = 0.5f;
    public const float MinimumGlobalScale = 0.01f;
    public const float MaximumGlobalScale = 2f;
    public const float MinimumGain = 0.1f;
    public const float MaximumGain = 1.5f;
    public const float MaximumBrightnessOffset = 0.5f;
    public const float GaussianRadiusFactor = 3f;
    public const int MaximumKernelRadius = 64;
    public const int MinimumTopSize = 2;
    public const int MaximumLevelCount = 16;
    public const int MaximumCanvasSize = 8192;
    public const int MaximumPixelCount = 16777216;
    public const int ScratchLength = 8;
    public const int ScratchHashSum = 0;
    public const int ScratchHashMix = 1;
    public const int ScratchMinX = 2;
    public const int ScratchMinY = 3;
    public const int ScratchMaxX = 4;
    public const int ScratchMaxY = 5;

    public static QualitySettings GetQuality(MultiscaleRetinexQuality quality)
        => quality switch
        {
            MultiscaleRetinexQuality.Balanced => new QualitySettings(3, 4f),
            MultiscaleRetinexQuality.Ultra => new QualitySettings(5, 8f),
            _ => new QualitySettings(4, 6f),
        };

    public static int GetLevelCount(int width, int height)
    {
        var side = Math.Min(Math.Max(width, 1), Math.Max(height, 1));
        var count = 1;
        while (side > MinimumTopSize && count < MaximumLevelCount)
        {
            side = (side + 1) / 2;
            count++;
        }
        return count;
    }

    public static float GetScaleSigma(int scaleIndex, int scaleCount, float minimumSigma, float maximumSigma)
    {
        var lower = Math.Max(minimumSigma, MinimumSigmaPixels);
        var upper = Math.Max(maximumSigma, lower);
        if (scaleCount <= 1)
            return upper;
        var ratio = scaleIndex / (scaleCount - 1f);
        return lower * MathF.Pow(upper / lower, ratio);
    }

    public static int GetLevelForSigma(float sigma, float sigmaBase, int levelCount)
    {
        if (sigma <= sigmaBase)
            return 0;
        var level = (int)MathF.Floor(MathF.Log2(sigma / sigmaBase));
        return Math.Clamp(level, 0, levelCount - 1);
    }

    public static int GetKernelRadius(float sigmaLevel)
        => Math.Clamp((int)MathF.Ceiling(sigmaLevel * GaussianRadiusFactor), 1, MaximumKernelRadius);

    public static float GetLocalSigma(float localScale, int longSide)
        => Math.Clamp(localScale, MinimumLocalScale, MaximumLocalScale) * longSide;

    public static float GetGlobalSigma(float globalScale, int longSide)
        => Math.Clamp(globalScale, MinimumGlobalScale, MaximumGlobalScale) * longSide;

    public static float GetGain(float contrast)
        => MinimumGain + Math.Clamp(contrast, 0f, 1f) * (MaximumGain - MinimumGain);

    public static float GetBrightnessOffset(float brightness)
        => Math.Clamp(brightness, -1f, 1f) * MaximumBrightnessOffset;

    internal readonly record struct QualitySettings(int ScaleCount, float SigmaBase);
}
