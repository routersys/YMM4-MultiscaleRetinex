using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace MultiscaleRetinex;

[VideoEffect(nameof(Texts.MultiscaleRetinex), [VideoEffectCategories.Filtering], [nameof(Texts.TagBacklight), nameof(Texts.TagHaze), nameof(Texts.TagColorConstancy)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class MultiscaleRetinexEffect : VideoEffectBase
{
    public override string Label => Texts.MultiscaleRetinex;

    public MultiscaleRetinexEffect()
    {
        MultiscaleRetinexUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 1, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public MultiscaleRetinexQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private MultiscaleRetinexQuality _quality = MultiscaleRetinexQuality.High;

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Mode), Description = nameof(Texts.ModeDescription), Order = 2, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public MultiscaleRetinexMode Mode { get => _mode; set => Set(ref _mode, value); }
    private MultiscaleRetinexMode _mode = MultiscaleRetinexMode.ColorConstancy;

    [Display(GroupName = nameof(Texts.ScaleGroup), Name = nameof(Texts.LocalScale), Description = nameof(Texts.LocalScaleDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0.5, 20)]
    public Animation LocalScale { get; } = new Animation(3, 0.1, 50);

    [Display(GroupName = nameof(Texts.ScaleGroup), Name = nameof(Texts.GlobalScale), Description = nameof(Texts.GlobalScaleDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 5, 100)]
    public Animation GlobalScale { get; } = new Animation(49, 1, 200);

    [Display(GroupName = nameof(Texts.AdjustGroup), Name = nameof(Texts.Contrast), Description = nameof(Texts.ContrastDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Contrast { get; } = new Animation(40, 0, 100);

    [Display(GroupName = nameof(Texts.AdjustGroup), Name = nameof(Texts.Brightness), Description = nameof(Texts.BrightnessDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", -100, 100)]
    public Animation Brightness { get; } = new Animation(0, -100, 100);

    [Display(GroupName = nameof(Texts.AdjustGroup), Name = nameof(Texts.ColorRestoration), Description = nameof(Texts.ColorRestorationDescription), Order = 22, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation ColorRestoration { get; } = new Animation(100, 0, 100);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new MultiscaleRetinexEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, LocalScale, GlobalScale, Contrast, Brightness, ColorRestoration];
}
