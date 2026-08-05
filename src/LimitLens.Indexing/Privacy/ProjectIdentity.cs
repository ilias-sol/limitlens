using System.Security.Cryptography;
using System.Text;

namespace LimitLens.Indexing.Privacy;

public sealed record ProjectIdentity(string Id, string DisplayName)
{
    public static ProjectIdentity FromPath(string? path, string privacySalt)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ProjectIdentity("unknown", "Unknown project");
        }

        var displayPath = NormalizeForDisplay(path);
        var normalized = displayPath.ToUpperInvariant();
        var key = Convert.FromHexString(privacySalt);
        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalized));
        var displayName = Path.GetFileName(displayPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Workspace";
        }

        return new ProjectIdentity(Convert.ToHexString(digest.AsSpan(0, 12)), displayName);
    }

    private static string NormalizeForDisplay(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception) when (path.Length > 0)
        {
            return path.Trim();
        }
    }
}
