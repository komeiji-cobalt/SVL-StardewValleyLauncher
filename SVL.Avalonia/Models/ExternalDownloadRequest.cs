namespace SVL.Avalonia.Models;

public sealed class ExternalDownloadRequest
{
    public string ResourceName { get; init; } = string.Empty;

    public string ResourceSource { get; init; } = string.Empty;

    public string SelectedDownloadOption { get; init; } = string.Empty;

    public string ToTaskDisplayName()
    {
        if (string.IsNullOrWhiteSpace(SelectedDownloadOption))
        {
            return string.IsNullOrWhiteSpace(ResourceSource)
                ? ResourceName
                : $"{ResourceName} [{ResourceSource}]";
        }

        return string.IsNullOrWhiteSpace(ResourceSource)
            ? $"{ResourceName} | {SelectedDownloadOption}"
            : $"{ResourceName} [{ResourceSource}] | {SelectedDownloadOption}";
    }
}
