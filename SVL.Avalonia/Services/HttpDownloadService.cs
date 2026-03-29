using System.Diagnostics;

namespace SVL.Avalonia.Services;

public sealed class HttpDownloadService
{
    private static readonly HttpClient Http = CreateClient();

    public async Task DownloadAsync(
        string url,
        string targetPath,
        Action<DownloadProgressSnapshot>? onProgress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("下载地址不能为空", nameof(url));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("目标文件路径不能为空", nameof(targetPath));
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, true);

        var buffer = new byte[1024 * 64];
        long downloadedBytes = 0;
        var sw = Stopwatch.StartNew();
        var lastReportedMs = 0L;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            if (sw.ElapsedMilliseconds - lastReportedMs >= 200)
            {
                onProgress?.Invoke(CreateSnapshot(downloadedBytes, totalBytes, sw.Elapsed.TotalSeconds));
                lastReportedMs = sw.ElapsedMilliseconds;
            }
        }

        onProgress?.Invoke(CreateSnapshot(downloadedBytes, totalBytes, sw.Elapsed.TotalSeconds));
    }

    private static DownloadProgressSnapshot CreateSnapshot(long downloadedBytes, long totalBytes, double elapsedSeconds)
    {
        var speed = elapsedSeconds <= 0 ? 0 : downloadedBytes / elapsedSeconds;
        var percent = totalBytes > 0
            ? Math.Min(100, downloadedBytes * 100d / totalBytes)
            : 0;

        return new DownloadProgressSnapshot(percent, downloadedBytes, totalBytes, speed);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(60)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("SVL-Avalonia/1.0");
        return client;
    }
}

public readonly record struct DownloadProgressSnapshot(
    double Percent,
    long DownloadedBytes,
    long TotalBytes,
    double BytesPerSecond);