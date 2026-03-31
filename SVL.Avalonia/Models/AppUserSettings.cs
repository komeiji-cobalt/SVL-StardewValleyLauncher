namespace SVL.Avalonia.Models;

public sealed class AppUserSettings
{
    public string GameWindowTitle { get; set; } = "<default>";

    public string InstanceCustomLaunchArguments { get; set; } = string.Empty;

    public bool InstanceAutoConnectServer { get; set; }

    public string InstanceServerAddress { get; set; } = string.Empty;

    public string InstanceSteamInviteCode { get; set; } = string.Empty;

    public string LauncherTitle { get; set; } = "Stardew Valley Launcher";

    public string LauncherAppName { get; set; } = "SVL";

    public bool EnableDownloadCache { get; set; } = true;

    public bool EnableDownloadProxy { get; set; }

    public string DownloadProxyUrl { get; set; } = string.Empty;

    public string DownloadProxyUserName { get; set; } = string.Empty;

    public string DownloadProxyPassword { get; set; } = string.Empty;

    public bool EnableDownloadFloatingTaskButton { get; set; } = true;

    public bool EnableAutoUpdateCheck { get; set; } = true;

    public string UpdateChannel { get; set; } = "稳定版";

    public string PreferredUpdateSource { get; set; } = "GitHub (推荐)";

    public string SkippedLauncherVersion { get; set; } = string.Empty;

    public bool RegisterNxmProtocolOnStartup { get; set; } = true;

    public string CollectionInstallConflictStrategy { get; set; } = "覆盖";

    public int CollectionDownloadParallelism { get; set; } = 4;

    public string ThemeMode { get; set; } = "跟随系统";

    public string UiLanguage { get; set; } = "zh-CN";

    public bool ShowNotifications { get; set; } = true;

    public bool DebugMode { get; set; }

    public string LogLevel { get; set; } = "Info";

    public bool MinimizeToTrayOnStartup { get; set; }

    public bool MinimizeToTrayOnClose { get; set; }

    public int SettingsTabIndex { get; set; } = 0;

    public string InstanceName { get; set; } = "Default Instance";

    public string InstanceDescription { get; set; } = string.Empty;

    public bool IsFavoriteInstance { get; set; }

    public string PreferredInstancePath { get; set; } = string.Empty;

    public List<string> FavoriteInstanceKeys { get; set; } = [];

    public bool OverrideSteamLaunchOptions { get; set; }

    public string SteamLaunchOptions { get; set; } = string.Empty;

    public string PreferredLaunchMode { get; set; } = "自动";

    public bool EnableSafeLaunch { get; set; }

    public string NexusApiKey { get; set; } = string.Empty;

    public string NexusOAuthAccessToken { get; set; } = string.Empty;

    public string NexusOAuthRefreshToken { get; set; } = string.Empty;

    public string NexusOAuthIdToken { get; set; } = string.Empty;

    public string NexusUserName { get; set; } = string.Empty;

    public string NexusMembershipType { get; set; } = string.Empty;

    public int NexusUserId { get; set; }
}
