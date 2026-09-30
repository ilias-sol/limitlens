using LimitLens.Core.Settings;

namespace LimitLens.Tests;

public sealed class TaskbarColorsTests
{
    [Theory]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData(" 123456 ", "#123456")]
    [InlineData("#000000", "#000000")]
    public void AcceptsSixDigitOpaqueColours(string input, string expected)
    {
        Assert.True(TaskbarColors.TryNormalizeHex(input, out var color));
        Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("##FFFFFF")]
    [InlineData("#00FFFFFF")]
    [InlineData("#GGGGGG")]
    public void RejectsIncompleteInvalidOrTransparentColours(string? input)
    {
        Assert.False(TaskbarColors.TryNormalizeHex(input, out _));
    }
}
