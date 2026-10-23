using LimitLens.Core.Settings;

namespace LimitLens.App.Services;

/// <summary>Flyout colours (ARGB hex by resource key) and the title font for one appearance.</summary>
public sealed record FlyoutPalette(IReadOnlyDictionary<string, string> Brushes, string TitleFont)
{
    private const string SegoeTitles = "Segoe UI Variable Display, Segoe UI";
    // A warm serif for headings, as in the Clay palettes' editorial style.
    private const string SerifTitles = "Georgia, Cambria, Segoe UI";

    public static FlyoutPalette Light { get; } = new(new Dictionary<string, string>
    {
        ["FlyoutSurface"] = "#A4F3F4F5",
        ["FlyoutLayer"] = "#64FFFFFF",
        ["FlyoutLayerStrong"] = "#7CFFFFFF",
        ["FlyoutInsight"] = "#80FFFFFF",
        ["FlyoutBorder"] = "#18000000",
        ["FlyoutDivider"] = "#12000000",
        ["FlyoutText"] = "#FF252932",
        ["FlyoutMuted"] = "#FF58616E",
        ["FlyoutDim"] = "#FF697380",
        ["FlyoutBlue"] = "#FF3478DF",
        ["FlyoutPrediction"] = "#FF7457C8",
        ["FlyoutTarget"] = "#FF7F8792",
        ["FlyoutGrid"] = "#14000000",
        ["FlyoutTrack"] = "#26000000",
        ["FlyoutHover"] = "#14000000",
        ["FlyoutPressed"] = "#20000000",
        ["FlyoutMenuSurface"] = "#FFF9FAFB",
    }, SegoeTitles);

    public static FlyoutPalette Dark { get; } = new(new Dictionary<string, string>
    {
        ["FlyoutSurface"] = "#A41A1D22",
        ["FlyoutLayer"] = "#6439434C",
        ["FlyoutLayerStrong"] = "#7C3D4750",
        ["FlyoutInsight"] = "#80282C33",
        ["FlyoutBorder"] = "#18FFFFFF",
        ["FlyoutDivider"] = "#12FFFFFF",
        ["FlyoutText"] = "#FFF4F5F7",
        ["FlyoutMuted"] = "#FFB2B8C2",
        ["FlyoutDim"] = "#FF9299A5",
        ["FlyoutBlue"] = "#FF68A4FF",
        ["FlyoutPrediction"] = "#FFB18CFF",
        ["FlyoutTarget"] = "#FF737C89",
        ["FlyoutGrid"] = "#14FFFFFF",
        ["FlyoutTrack"] = "#26FFFFFF",
        ["FlyoutHover"] = "#14FFFFFF",
        ["FlyoutPressed"] = "#20FFFFFF",
        ["FlyoutMenuSurface"] = "#FF26292F",
    }, SegoeTitles);

    // Ivory paper, slate ink, and a clay accent; FlyoutBlue is the accent role in every palette.
    public static FlyoutPalette Clay { get; } = new(new Dictionary<string, string>
    {
        ["FlyoutSurface"] = "#D9F0EEE6",
        ["FlyoutLayer"] = "#B3FAF9F5",
        ["FlyoutLayerStrong"] = "#CCFAF9F5",
        ["FlyoutInsight"] = "#99FFFFFF",
        ["FlyoutBorder"] = "#1A141413",
        ["FlyoutDivider"] = "#14141413",
        ["FlyoutText"] = "#FF141413",
        ["FlyoutMuted"] = "#FF5E5D59",
        ["FlyoutDim"] = "#FF87867F",
        ["FlyoutBlue"] = "#FFD97757",
        ["FlyoutPrediction"] = "#FF788C5D",
        ["FlyoutTarget"] = "#FFB0AEA5",
        ["FlyoutGrid"] = "#14141413",
        ["FlyoutTrack"] = "#1F141413",
        ["FlyoutHover"] = "#12141413",
        ["FlyoutPressed"] = "#1F141413",
        ["FlyoutMenuSurface"] = "#FFFAF9F5",
    }, SerifTitles);

    public static FlyoutPalette ClayDark { get; } = new(new Dictionary<string, string>
    {
        ["FlyoutSurface"] = "#D91F1E1D",
        ["FlyoutLayer"] = "#B330302E",
        ["FlyoutLayerStrong"] = "#CC3A3A37",
        ["FlyoutInsight"] = "#99262624",
        ["FlyoutBorder"] = "#1AFAF9F5",
        ["FlyoutDivider"] = "#14FAF9F5",
        ["FlyoutText"] = "#FFFAF9F5",
        ["FlyoutMuted"] = "#FFC2C0B6",
        ["FlyoutDim"] = "#FF9C9A92",
        ["FlyoutBlue"] = "#FFD97757",
        ["FlyoutPrediction"] = "#FF9DB07C",
        ["FlyoutTarget"] = "#FF87867F",
        ["FlyoutGrid"] = "#14FAF9F5",
        ["FlyoutTrack"] = "#26FAF9F5",
        ["FlyoutHover"] = "#14FAF9F5",
        ["FlyoutPressed"] = "#20FAF9F5",
        ["FlyoutMenuSurface"] = "#FF30302E",
    }, SerifTitles);

    public static FlyoutPalette For(DashboardTheme theme, bool light) => theme switch
    {
        DashboardTheme.Clay => Clay,
        DashboardTheme.ClayDark => ClayDark,
        _ => light ? Light : Dark,
    };
}
