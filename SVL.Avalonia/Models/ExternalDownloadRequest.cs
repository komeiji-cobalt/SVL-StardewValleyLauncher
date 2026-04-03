namespace SVL.Avalonia.Models;

public enum ExternalDownloadAction
{
    Install,
    SaveAs
}

public sealed class ExternalDownloadRequest
{
    public ExternalDownloadAction Action { get; init; } = ExternalDownloadAction.Install;

    public string ResourceName { get; init; } = string.Empty;

    public string ResourceSource { get; init; } = string.Empty;

    public string ResourceId { get; init; } = string.Empty;

    public string SourceToken { get; init; } = string.Empty;

    public string SourcePageUrl { get; init; } = string.Empty;

    public bool IsSmapiResource { get; init; }

    public string SelectedDownloadOption { get; init; } = string.Empty;

    public string ToTaskDisplayName()
    {
        var actionPrefix = Action == ExternalDownloadAction.SaveAs ? "[另存为]" : "[安装]";

        if (string.IsNullOrWhiteSpace(SelectedDownloadOption))
        {
            return string.IsNullOrWhiteSpace(ResourceSource)
                ? $"{actionPrefix} {ResourceName}"
                : $"{actionPrefix} {ResourceName} [{ResourceSource}]";
        }

        return string.IsNullOrWhiteSpace(ResourceSource)
            ? $"{actionPrefix} {ResourceName} | {SelectedDownloadOption}"
            : $"{actionPrefix} {ResourceName} [{ResourceSource}] | {SelectedDownloadOption}";
    }

    public string ResolveSuggestedFileName()
    {
        var option = SelectedDownloadOption?.Trim() ?? string.Empty;
        if (option.Length > 0)
        {
            var pipeIndex = option.IndexOf('|');
            if (pipeIndex > 0)
            {
                var leftPart = option[..pipeIndex].Trim();
                if (leftPart.Length > 0)
                {
                    return leftPart;
                }
            }

            if (option.StartsWith("File ", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = option.IndexOf(':');
                if (colonIndex > 0 && colonIndex < option.Length - 1)
                {
                    var filePart = option[(colonIndex + 1)..].Trim();
                    if (filePart.Length > 0)
                    {
                        return filePart;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(ResourceName))
        {
            return "download.zip";
        }

        return ResourceName.Trim();
    }
}
