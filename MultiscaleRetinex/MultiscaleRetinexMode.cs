using System.ComponentModel.DataAnnotations;

namespace MultiscaleRetinex;

public enum MultiscaleRetinexMode
{
    [Display(Name = nameof(Texts.ModeColorConstancy), Description = nameof(Texts.ModeColorConstancyDescription), ResourceType = typeof(Texts))]
    ColorConstancy,
    [Display(Name = nameof(Texts.ModeChromaticity), Description = nameof(Texts.ModeChromaticityDescription), ResourceType = typeof(Texts))]
    Chromaticity,
}
