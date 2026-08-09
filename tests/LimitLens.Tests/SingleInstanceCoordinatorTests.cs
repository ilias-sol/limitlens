using System.IO;
using LimitLens.App.Services;

namespace LimitLens.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void PortableInstanceNameIsStableAndScopedToItsDirectory()
    {
        using var folder = new TempFolder();
        var firstDirectory = Directory.CreateDirectory(folder.GetPath("first")).FullName;
        var secondDirectory = Directory.CreateDirectory(folder.GetPath("second")).FullName;

        var first = SingleInstanceCoordinator.PortableInstanceName(firstDirectory);
        var firstWithTrailingSeparator = SingleInstanceCoordinator.PortableInstanceName(
            firstDirectory + Path.DirectorySeparatorChar);
        var second = SingleInstanceCoordinator.PortableInstanceName(secondDirectory);

        Assert.Equal(first, firstWithTrailingSeparator);
        Assert.NotEqual(first, second);
        Assert.StartsWith("Portable.", first, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentInstanceScopesCanRunTogether()
    {
        var unique = Guid.NewGuid().ToString("N");
        using var installed = new SingleInstanceCoordinator($"Test.Installed.{unique}");
        using var portable = new SingleInstanceCoordinator($"Test.Portable.{unique}");
        using var secondPortable = new SingleInstanceCoordinator($"Test.Portable.{unique}");

        Assert.True(installed.IsPrimary);
        Assert.True(portable.IsPrimary);
        Assert.False(secondPortable.IsPrimary);
    }
}
