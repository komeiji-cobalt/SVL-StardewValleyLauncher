using System.IO.Compression;

namespace SVL.Avalonia.Services;

public sealed class SmapiInstallService
{
    public async Task<SmapiInstallResult> InstallFromZipAsync(
        string zipFilePath,
        string gameBasePath,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
        {
            return SmapiInstallResult.Failed("SMAPI 压缩包不存在");
        }

        if (string.IsNullOrWhiteSpace(gameBasePath) || !Directory.Exists(gameBasePath))
        {
            return SmapiInstallResult.Failed("游戏基础路径无效");
        }

        var safeInstanceName = SanitizeFolderName(instanceName);
        if (string.IsNullOrWhiteSpace(safeInstanceName))
        {
            return SmapiInstallResult.Failed("实例名称无效");
        }

        var versionRoot = Path.Combine(gameBasePath, "versions", safeInstanceName);
        var runtimePath = Path.Combine(versionRoot, "game");
        if (Directory.Exists(versionRoot))
        {
            return SmapiInstallResult.Failed($"实例已存在: {safeInstanceName}");
        }

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "svl-smapi-install",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(versionRoot);
            Directory.CreateDirectory(runtimePath);
            Directory.CreateDirectory(tempRoot);

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                CopyBaseGame(gameBasePath, runtimePath, cancellationToken);
            }, cancellationToken);

            var extractPath = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(extractPath);

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ZipFile.ExtractToDirectory(zipFilePath, extractPath, true);
            }, cancellationToken);

            var commonPayload = FindPayloadDirectory(extractPath, "internal files");
            var platformPayload = FindPlatformPayloadDirectory(extractPath);
            var directPayload = FindDirectPayloadDirectory(extractPath);

            if (commonPayload == null && platformPayload == null && directPayload == null)
            {
                return SmapiInstallResult.Failed("未识别到 SMAPI 安装内容，请确认下载的是官方安装包");
            }

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (commonPayload != null)
                {
                    CopyDirectory(commonPayload, runtimePath, true, cancellationToken);
                }

                if (platformPayload != null)
                {
                    CopyDirectory(platformPayload, runtimePath, true, cancellationToken);
                }

                if (commonPayload == null && platformPayload == null && directPayload != null)
                {
                    CopyDirectory(directPayload, runtimePath, true, cancellationToken);
                }

                Directory.CreateDirectory(Path.Combine(runtimePath, "Mods"));
            }, cancellationToken);

            if (!HasSmapiMarkers(runtimePath))
            {
                return SmapiInstallResult.Failed("安装后未检测到 SMAPI 可执行文件，请确认安装包与系统平台匹配");
            }

            return SmapiInstallResult.Success(runtimePath, versionRoot);
        }
        catch (OperationCanceledException)
        {
            return SmapiInstallResult.Cancelled("SMAPI 安装已取消");
        }
        catch (Exception ex)
        {
            return SmapiInstallResult.Failed($"SMAPI 安装失败: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void CopyBaseGame(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetPath);

        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(sourcePath, directory);
            if (ShouldSkipRelativePath(relative))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(sourcePath, file);
            if (ShouldSkipRelativePath(relative))
            {
                continue;
            }

            var destination = Path.Combine(targetPath, relative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, destination, true);
        }
    }

    private static bool ShouldSkipRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return false;
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var firstSegment = relativePath
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return false;
        }

        return string.Equals(firstSegment, "versions", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstSegment, ".git", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindPayloadDirectory(string rootPath, string folderName)
    {
        var normalizedTarget = NormalizeFolderToken(folderName);
        foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            if (NormalizeFolderToken(Path.GetFileName(directory)) == normalizedTarget)
            {
                return directory;
            }
        }

        return null;
    }

    private static string? FindPlatformPayloadDirectory(string rootPath)
    {
        var preferredCandidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            preferredCandidates.Add("internal windows");
            preferredCandidates.Add("windows");
        }
        else if (OperatingSystem.IsMacOS())
        {
            preferredCandidates.Add("internal macos");
            preferredCandidates.Add("macos");
        }
        else
        {
            preferredCandidates.Add("internal linux");
            preferredCandidates.Add("linux");
        }

        foreach (var candidate in preferredCandidates)
        {
            var found = FindPayloadDirectory(rootPath, candidate);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        return null;
    }

    private static string? FindDirectPayloadDirectory(string rootPath)
    {
        var direct = Directory
            .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .FirstOrDefault(HasSmapiMarkers);

        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return HasSmapiMarkers(rootPath) ? rootPath : null;
    }

    private static bool HasSmapiMarkers(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        var markers = new[]
        {
            "StardewModdingAPI.exe",
            "StardewModdingAPI",
            "StardewModdingAPI.dll"
        };

        return markers.Any(marker => File.Exists(Path.Combine(path, marker)));
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDir, file);
            var targetFile = Path.Combine(targetDir, relative);
            var parent = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, targetFile, overwrite);
        }
    }

    private static string SanitizeFolderName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var cleaned = string.Concat(rawName.Trim().Split(Path.GetInvalidFileNameChars()));
        return cleaned.Trim();
    }

    private static string NormalizeFolderToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Trim()
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '_' && ch != '.');
        return new string(chars.ToArray()).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // Keep temp cleanup best-effort.
        }
    }
}

public sealed class SmapiInstallResult
{
    public bool IsSuccess { get; init; }

    public bool IsCancelled { get; init; }

    public string Message { get; init; } = string.Empty;

    public string RuntimePath { get; init; } = string.Empty;

    public string VersionRootPath { get; init; } = string.Empty;

    public static SmapiInstallResult Success(string runtimePath, string versionRootPath)
    {
        return new SmapiInstallResult
        {
            IsSuccess = true,
            Message = "SMAPI 安装成功",
            RuntimePath = runtimePath,
            VersionRootPath = versionRootPath
        };
    }

    public static SmapiInstallResult Failed(string message)
    {
        return new SmapiInstallResult
        {
            IsSuccess = false,
            IsCancelled = false,
            Message = message
        };
    }

    public static SmapiInstallResult Cancelled(string message)
    {
        return new SmapiInstallResult
        {
            IsSuccess = false,
            IsCancelled = true,
            Message = message
        };
    }
}
