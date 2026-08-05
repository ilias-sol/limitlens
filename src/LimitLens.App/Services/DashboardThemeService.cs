using System.Windows;
using System.Windows.Media;
using LimitLens.Core.Settings;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;

namespace LimitLens.App.Services;

public static class DashboardThemeService
{
    public static void Apply(DashboardTheme selectedTheme)
    {
        var useLight = UsesLightPalette(selectedTheme);
        if (System.Windows.Application.Current is { } application)
        {
            var themeProperty = typeof(System.Windows.Application).GetProperty("ThemeMode");
            if (themeProperty is not null)
            {
                var themeName = selectedTheme switch
                {
                    DashboardTheme.Light => "Light",
                    DashboardTheme.System => "System",
                    _ => "Dark",
                };
                var themeType = Nullable.GetUnderlyingType(themeProperty.PropertyType) ?? themeProperty.PropertyType;
                var themeValue = themeType.IsEnum
                    ? Enum.Parse(themeType, themeName)
                    : themeType.GetProperty(
                        themeName,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (themeValue is not null)
                {
                    themeProperty.SetValue(application, themeValue);
                }
            }

            ApplyPalette(application.Resources, useLight);
        }
    }

    public static bool UsesLightPalette(DashboardTheme selectedTheme) =>
        selectedTheme == DashboardTheme.Light ||
        selectedTheme == DashboardTheme.System && SystemPrefersLight();

    private static bool SystemPrefersLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private static void ApplyPalette(ResourceDictionary resources, bool light)
    {
        SetBrush(resources, "WindowBrush", light ? "#FFF0F2F5" : "#FF181A1E");
        SetBrush(resources, "TitleBarBrush", light ? "#FFF0F2F5" : "#FF181A1E");
        SetBrush(resources, "SidebarBrush", light ? "#FFE7EAF0" : "#FF202329");
        SetBrush(resources, "PanelBrush", light ? "#FFF9FAFC" : "#FF22252B");
        SetBrush(resources, "PanelHoverBrush", light ? "#FFF0F2F6" : "#FF2A2E35");
        SetBrush(resources, "BorderBrush", light ? "#FFD9DEE7" : "#FF353A43");
        SetBrush(resources, "BorderStrongBrush", light ? "#FFCCD2DD" : "#FF454B56");
        SetBrush(resources, "TextBrush", light ? "#FF303442" : "#FFF1F2F4");
        SetBrush(resources, "MutedTextBrush", light ? "#FF62697B" : "#FFB0B4BE");
        SetBrush(resources, "DimTextBrush", light ? "#FF848A99" : "#FF858B97");
        SetBrush(resources, "AccentBrush", light ? "#FF2E5D50" : "#FF9EC3B5");
        SetBrush(resources, "AccentSoftBrush", light ? "#FFE3ECE8" : "#FF2D3935");
        SetBrush(resources, "BlueBrush", light ? "#FF5278B7" : "#FF8EADE0");
        SetBrush(resources, "WarningBrush", light ? "#FFB26A28" : "#FFE0A45E");
        SetBrush(resources, "DangerBrush", light ? "#FFB44B55" : "#FFE48189");
        SetBrush(resources, "ChartGridBrush", light ? "#FFE3E6EC" : "#FF31353D");
        SetBrush(resources, "ChartTargetBrush", light ? "#FF7D9B76" : "#FF91AE88");
        SetBrush(resources, "UsageBarBrush", light ? "#FF4B4E56" : "#FFC1C4CB");
    }

    private static void SetBrush(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
