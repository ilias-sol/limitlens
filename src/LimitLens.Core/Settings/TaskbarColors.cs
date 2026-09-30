namespace LimitLens.Core.Settings;

public enum TaskbarColorMode
{
    Automatic,
    White,
    Black,
    Gray,
    Custom,
}

public static class TaskbarColors
{
    public const string DefaultCustomColor = "#FFFFFF";

    public static bool TryNormalizeHex(string? value, out string normalized)
    {
        var digits = value?.Trim();
        if (digits?.StartsWith('#') == true) digits = digits[1..];
        if (digits is { Length: 6 } && digits.All(char.IsAsciiHexDigit))
        {
            normalized = "#" + digits.ToUpperInvariant();
            return true;
        }

        normalized = DefaultCustomColor;
        return false;
    }
}
