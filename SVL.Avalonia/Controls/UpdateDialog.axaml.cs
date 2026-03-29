using Avalonia.Controls;
using Avalonia.Interactivity;
using SVL.Avalonia.Models;

namespace SVL.Avalonia.Controls;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
        : this(new Version(0, 0, 0, 0), new LauncherReleaseInfo(), "-")
    {
    }

    public UpdateDialog(Version currentVersion, LauncherReleaseInfo releaseInfo, string source)
    {
        InitializeComponent();

        VersionText.Text = $"v{currentVersion} -> {releaseInfo.TagName}";
        SourceText.Text = string.IsNullOrWhiteSpace(source) ? "-" : source;
        PublishedAtText.Text = releaseInfo.PublishedAt == DateTime.MinValue
            ? "-"
            : releaseInfo.PublishedAt.ToString("yyyy-MM-dd HH:mm");

        if (!string.IsNullOrWhiteSpace(releaseInfo.UpdateLog))
        {
            ChangelogText.Text = releaseInfo.UpdateLog.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(releaseInfo.Body))
        {
            ChangelogText.Text = releaseInfo.Body.Trim();
        }
        else
        {
            ChangelogText.Text = "暂无更新日志";
        }
    }

    private void Later_Click(object? sender, RoutedEventArgs e)
    {
        Close(UpdateDialogAction.Later);
    }

    private void SkipVersion_Click(object? sender, RoutedEventArgs e)
    {
        Close(UpdateDialogAction.SkipVersion);
    }

    private void OpenRelease_Click(object? sender, RoutedEventArgs e)
    {
        Close(UpdateDialogAction.OpenRelease);
    }
}
