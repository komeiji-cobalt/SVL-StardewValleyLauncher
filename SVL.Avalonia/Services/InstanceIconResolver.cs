using System.IO;

namespace SVL.Avalonia.Services;

public static class InstanceIconResolver
{
    private static readonly string[] IconExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp",
        ".gif"
    ];

    public static string ResolveIconPath(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            return string.Empty;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateIconCandidates(instancePath))
        {
            if (!seen.Add(candidate))
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    public static string ResolveStorageDirectory(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            return string.Empty;
        }

        var directoryName = Path.GetFileName(instancePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(directoryName, "game", StringComparison.OrdinalIgnoreCase))
        {
            return instancePath;
        }

        var parent = Directory.GetParent(instancePath);
        return parent?.Exists == true ? parent.FullName : instancePath;
    }

    private static IEnumerable<string> EnumerateIconCandidates(string instancePath)
    {
        var storageDirectory = ResolveStorageDirectory(instancePath);
        if (!string.IsNullOrWhiteSpace(storageDirectory))
        {
            foreach (var candidate in BuildCandidates(storageDirectory))
            {
                yield return candidate;
            }
        }

        if (!string.Equals(storageDirectory, instancePath, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in BuildCandidates(instancePath))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> BuildCandidates(string directory)
    {
        yield return Path.Combine(directory, ".svl-instance-icon");

        foreach (var extension in IconExtensions)
        {
            yield return Path.Combine(directory, $".svl-instance-icon{extension}");
        }
    }
}