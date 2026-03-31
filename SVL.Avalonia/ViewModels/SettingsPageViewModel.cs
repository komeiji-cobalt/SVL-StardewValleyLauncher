using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using System.Collections.ObjectModel;

namespace SVL.Avalonia.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    private readonly AppUserSettingsStore _settingsStore;
    private readonly DialogService _dialogService;
    private readonly NexusAuthService _nexusAuthService;
    private readonly NexusOAuthService _nexusOAuthService;
    private readonly LauncherUpdateService _launcherUpdateService;
    private readonly IExternalProcessService _externalProcessService;
    private readonly INxmProtocolRegistrationService _nxmProtocolRegistrationService;
    private readonly LocalizationService _localizationService;
    private readonly ImageResourceService _imageResourceService;

    public ObservableCollection<string> Tabs { get; } = ["基本设置", "下载设置", "个性化", "其他", "关于"];

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusMessage = "设置已加载";

    [ObservableProperty]
    private string _pageTitleText = "设置";

    [ObservableProperty]
    private string _tabBasicText = "基本设置";

    [ObservableProperty]
    private string _tabDownloadText = "下载设置";

    [ObservableProperty]
    private string _tabPersonalizationText = "个性化";

    [ObservableProperty]
    private string _tabOtherText = "其他";

    [ObservableProperty]
    private string _tabAboutText = "关于";

    [ObservableProperty]
    private string _otherSectionSubtitleText = "运行时行为与调试选项";

    [ObservableProperty]
    private string _showNotificationsLabelText = "显示通知";

    [ObservableProperty]
    private string _debugModeLabelText = "启用调试模式";

    [ObservableProperty]
    private string _logLevelLabelText = "日志级别";

    [ObservableProperty]
    private string _minimizeOnStartupLabelText = "启动时最小化到托盘";

    [ObservableProperty]
    private string _minimizeOnCloseLabelText = "关闭窗口时最小化到托盘";

    [ObservableProperty]
    private string _themeModeLabelText = "主题模式";

    [ObservableProperty]
    private string _uiLanguageLabelText = "界面语言";

    [ObservableProperty]
    private string _saveButtonText = "保存设置";

    [ObservableProperty]
    private string _instanceAutoConnectLabelText = "启动时自动连接服务器";

    [ObservableProperty]
    private string _instanceServerAddressLabelText = "服务器地址";

    [ObservableProperty]
    private string _instanceSteamInviteCodeLabelText = "Steam 邀请码";

    [ObservableProperty]
    private string _settingsPath = string.Empty;

    [ObservableProperty]
    private string _operationPathHint = "操作路径：基本设置 -> 下载设置 -> 个性化 -> 其他 -> 关于";

    [ObservableProperty]
    private string _updateCardTitleText = "更新设置";

    [ObservableProperty]
    private string _autoUpdateCheckLabelText = "启动时自动检查更新";

    [ObservableProperty]
    private string _updateChannelLabelText = "更新通道";

    [ObservableProperty]
    private string _updateSourcePreferenceLabelText = "更新源偏好";

    [ObservableProperty]
    private string _checkUpdateButtonText = "立即检查更新";

    [ObservableProperty]
    private string _updateStatusLabelText = "更新状态";

    [ObservableProperty]
    private string _latestVersionLabelText = "最新版本";

    [ObservableProperty]
    private string _updateSourceLabelText = "更新源";

    [ObservableProperty]
    private string _nxmProtocolCardTitleText = "NXM 协议";

    [ObservableProperty]
    private string _nxmProtocolStatusLabelText = "状态";

    [ObservableProperty]
    private string _nxmAutoRegisterLabelText = "启动时尝试注册 NXM 协议";

    [ObservableProperty]
    private string _nxmProtocolDescriptionText = "用于浏览器中的 nxm:// 链接快速回传到启动器下载页。";

    [ObservableProperty]
    private string _nxmRegisterNowButtonText = "重新注册 NXM 协议";

    [ObservableProperty]
    private string _basicCardIconSource = string.Empty;

    [ObservableProperty]
    private string _downloadCardIconSource = string.Empty;

    [ObservableProperty]
    private string _nexusCardIconSource = string.Empty;

    [ObservableProperty]
    private string _updateCardIconSource = string.Empty;

    [ObservableProperty]
    private string _nxmProtocolCardIconSource = string.Empty;

    [ObservableProperty]
    private string _personalizationCardIconSource = string.Empty;

    [ObservableProperty]
    private string _otherCardIconSource = string.Empty;

    [ObservableProperty]
    private string _aboutCardIconSource = string.Empty;

    [ObservableProperty]
    private string _gameWindowTitle = "<default>";

    [ObservableProperty]
    private string _launcherTitle = "Stardew Valley Launcher";

    [ObservableProperty]
    private string _launcherAppName = "SVL";

    [ObservableProperty]
    private bool _instanceAutoConnectServer;

    [ObservableProperty]
    private string _instanceServerAddress = string.Empty;

    [ObservableProperty]
    private string _instanceSteamInviteCode = string.Empty;

    [ObservableProperty]
    private bool _enableDownloadCache = true;

    [ObservableProperty]
    private bool _enableDownloadProxy;

    [ObservableProperty]
    private string _downloadProxyUrl = string.Empty;

    [ObservableProperty]
    private string _downloadProxyUserName = string.Empty;

    [ObservableProperty]
    private string _downloadProxyPassword = string.Empty;

    [ObservableProperty]
    private bool _enableDownloadFloatingTaskButton = true;

    [ObservableProperty]
    private bool _enableAutoUpdateCheck = true;

    [ObservableProperty]
    private string _selectedUpdateChannel = "稳定版";

    [ObservableProperty]
    private string _selectedUpdateSource = "GitHub (推荐)";

    [ObservableProperty]
    private string _skippedUpdateVersion = string.Empty;

    [ObservableProperty]
    private string _updateStatusText = "尚未检查更新";

    [ObservableProperty]
    private string _latestVersionText = "-";

    [ObservableProperty]
    private string _updateSourceText = "-";

    [ObservableProperty]
    private bool _isCheckingLauncherUpdate;

    [ObservableProperty]
    private bool _registerNxmProtocolOnStartup = true;

    [ObservableProperty]
    private string _nxmProtocolStatusText = "待确认";

    [ObservableProperty]
    private string _selectedCollectionConflictStrategy = "覆盖";

    [ObservableProperty]
    private int _selectedCollectionDownloadParallelism = 4;

    [ObservableProperty]
    private string _selectedThemeMode = "跟随系统";

    [ObservableProperty]
    private string _selectedUiLanguage = "zh-CN";

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private bool _debugMode;

    [ObservableProperty]
    private string _selectedLogLevel = "Info";

    [ObservableProperty]
    private bool _minimizeToTrayOnStartup;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private string _nexusApiKey = string.Empty;

    [ObservableProperty]
    private string _nexusOAuthAccessToken = string.Empty;

    [ObservableProperty]
    private string _nexusOAuthRefreshToken = string.Empty;

    [ObservableProperty]
    private string _nexusOAuthIdToken = string.Empty;

    [ObservableProperty]
    private string _nexusUserName = string.Empty;

    [ObservableProperty]
    private string _nexusMembershipType = string.Empty;

    [ObservableProperty]
    private int _nexusUserId;

    [ObservableProperty]
    private string _nexusStatus = "未登录";

    public ObservableCollection<string> ThemeModes { get; } = ["浅色", "深色", "跟随系统"];

    public ObservableCollection<string> UiLanguages { get; } = ["zh-CN", "en-US"];

    public ObservableCollection<string> LogLevelOptions { get; } = ["Debug", "Info", "Warning", "Error", "None"];

    public ObservableCollection<string> CollectionConflictStrategies { get; } = ["覆盖", "跳过", "仅备份"];

    public ObservableCollection<string> UpdateChannels { get; } = ["稳定版", "预览版"];

    public ObservableCollection<string> UpdateSourceOptions { get; } = ["GitHub (推荐)", "Gitee (国内加速)"];

    public ObservableCollection<int> CollectionDownloadParallelismOptions { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

    public bool IsNexusLoggedIn => !string.IsNullOrWhiteSpace(NexusApiKey) || !string.IsNullOrWhiteSpace(NexusOAuthAccessToken);

    public bool ShowNexusLoginGuide => !IsNexusLoggedIn;

    public string NexusDisplayName => string.IsNullOrWhiteSpace(NexusUserName)
        ? "未识别用户"
        : $"{NexusUserName} ({NexusMembershipType})";

    public bool CanCheckLauncherUpdate => !IsCheckingLauncherUpdate;

    public SettingsPageViewModel(
        AppUserSettingsStore settingsStore,
        DialogService dialogService,
        NexusAuthService nexusAuthService,
        NexusOAuthService nexusOAuthService,
        LauncherUpdateService launcherUpdateService,
        IExternalProcessService externalProcessService,
        INxmProtocolRegistrationService nxmProtocolRegistrationService,
        LocalizationService localizationService,
        ImageResourceService imageResourceService)
    {
        _settingsStore = settingsStore;
        _dialogService = dialogService;
        _nexusAuthService = nexusAuthService;
        _nexusOAuthService = nexusOAuthService;
        _launcherUpdateService = launcherUpdateService;
        _externalProcessService = externalProcessService;
        _nxmProtocolRegistrationService = nxmProtocolRegistrationService;
        _localizationService = localizationService;
        _imageResourceService = imageResourceService;
        _localizationService.LanguageChanged += ApplyLocalizedTexts;
        _imageResourceService.ResourcesChanged += ApplyImageResources;
        ApplyLocalizedTexts();
        ApplyImageResources();
        SettingsPath = _settingsStore.GetSettingsPath();

        var loaded = _settingsStore.Load();
        ApplySettings(loaded);
        RefreshNxmProtocolStatus();
        StatusMessage = "设置已从本地加载";

        _ = TryRefreshOAuthTokenSilentlyAsync();
    }

    private void ApplySettings(AppUserSettings settings)
    {
        GameWindowTitle = settings.GameWindowTitle;
        LauncherTitle = settings.LauncherTitle;
        LauncherAppName = settings.LauncherAppName;
        InstanceAutoConnectServer = settings.InstanceAutoConnectServer;
        InstanceServerAddress = settings.InstanceServerAddress;
        InstanceSteamInviteCode = settings.InstanceSteamInviteCode;
        EnableDownloadCache = settings.EnableDownloadCache;
        EnableDownloadProxy = settings.EnableDownloadProxy;
        DownloadProxyUrl = settings.DownloadProxyUrl;
        DownloadProxyUserName = settings.DownloadProxyUserName;
        DownloadProxyPassword = settings.DownloadProxyPassword;
        EnableDownloadFloatingTaskButton = settings.EnableDownloadFloatingTaskButton;
        EnableAutoUpdateCheck = settings.EnableAutoUpdateCheck;
        SelectedUpdateChannel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "稳定版" : settings.UpdateChannel;
        SelectedUpdateSource = string.IsNullOrWhiteSpace(settings.PreferredUpdateSource) ? "GitHub (推荐)" : settings.PreferredUpdateSource;
        SkippedUpdateVersion = settings.SkippedLauncherVersion;
        RegisterNxmProtocolOnStartup = settings.RegisterNxmProtocolOnStartup;
        NxmProtocolStatusText = RegisterNxmProtocolOnStartup ? "已启用自动注册" : "未启用自动注册";
        SelectedCollectionConflictStrategy = string.IsNullOrWhiteSpace(settings.CollectionInstallConflictStrategy)
            ? "覆盖"
            : settings.CollectionInstallConflictStrategy;
        SelectedCollectionDownloadParallelism = Math.Clamp(settings.CollectionDownloadParallelism, 1, 8);
        SelectedThemeMode = settings.ThemeMode;
        SelectedUiLanguage = string.IsNullOrWhiteSpace(settings.UiLanguage) ? "zh-CN" : settings.UiLanguage;
        ShowNotifications = settings.ShowNotifications;
        DebugMode = settings.DebugMode;
        SelectedLogLevel = string.IsNullOrWhiteSpace(settings.LogLevel) ? "Info" : settings.LogLevel;
        MinimizeToTrayOnStartup = settings.MinimizeToTrayOnStartup;
        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        SelectedTabIndex = Math.Clamp(settings.SettingsTabIndex, 0, Tabs.Count - 1);
        NexusApiKey = settings.NexusApiKey;
        NexusOAuthAccessToken = settings.NexusOAuthAccessToken;
        NexusOAuthRefreshToken = settings.NexusOAuthRefreshToken;
        NexusOAuthIdToken = settings.NexusOAuthIdToken;
        NexusUserName = settings.NexusUserName;
        NexusMembershipType = settings.NexusMembershipType;
        NexusUserId = settings.NexusUserId;
        NexusStatus = IsNexusLoggedIn ? "已登录" : "未登录";
    }

    private AppUserSettings BuildSettings()
    {
        var settings = _settingsStore.Load();
        settings.GameWindowTitle = GameWindowTitle;
        settings.LauncherTitle = LauncherTitle;
        settings.LauncherAppName = LauncherAppName;
        settings.InstanceAutoConnectServer = InstanceAutoConnectServer;
        settings.InstanceServerAddress = InstanceServerAddress?.Trim() ?? string.Empty;
        settings.InstanceSteamInviteCode = InstanceSteamInviteCode?.Trim() ?? string.Empty;
        settings.EnableDownloadCache = EnableDownloadCache;
        settings.EnableDownloadProxy = EnableDownloadProxy;
        settings.DownloadProxyUrl = DownloadProxyUrl?.Trim() ?? string.Empty;
        settings.DownloadProxyUserName = DownloadProxyUserName?.Trim() ?? string.Empty;
        settings.DownloadProxyPassword = DownloadProxyPassword ?? string.Empty;
        settings.EnableDownloadFloatingTaskButton = EnableDownloadFloatingTaskButton;
        settings.EnableAutoUpdateCheck = EnableAutoUpdateCheck;
        settings.UpdateChannel = SelectedUpdateChannel;
        settings.PreferredUpdateSource = SelectedUpdateSource;
        settings.SkippedLauncherVersion = SkippedUpdateVersion;
        settings.RegisterNxmProtocolOnStartup = RegisterNxmProtocolOnStartup;
        settings.CollectionInstallConflictStrategy = SelectedCollectionConflictStrategy;
        settings.CollectionDownloadParallelism = Math.Clamp(SelectedCollectionDownloadParallelism, 1, 8);
        settings.ThemeMode = SelectedThemeMode;
        settings.UiLanguage = SelectedUiLanguage;
        settings.ShowNotifications = ShowNotifications;
        settings.DebugMode = DebugMode;
        settings.LogLevel = SelectedLogLevel;
        settings.MinimizeToTrayOnStartup = MinimizeToTrayOnStartup;
        settings.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        settings.SettingsTabIndex = Math.Clamp(SelectedTabIndex, 0, Tabs.Count - 1);
        settings.NexusApiKey = NexusApiKey;
        settings.NexusOAuthAccessToken = NexusOAuthAccessToken;
        settings.NexusOAuthRefreshToken = NexusOAuthRefreshToken;
        settings.NexusOAuthIdToken = NexusOAuthIdToken;
        settings.NexusUserName = NexusUserName;
        settings.NexusMembershipType = NexusMembershipType;
        settings.NexusUserId = NexusUserId;
        return settings;
    }

    private void ApplyLocalizedTexts()
    {
        PageTitleText = _localizationService.Get("Settings.Title");
        TabBasicText = _localizationService.Get("Settings.Tab.Basic");
        TabDownloadText = _localizationService.Get("Settings.Tab.Download");
        TabPersonalizationText = _localizationService.Get("Settings.Tab.Personalization");
        TabOtherText = _localizationService.Get("Settings.Tab.Other");
        TabAboutText = _localizationService.Get("Settings.Tab.About");
        OtherSectionSubtitleText = _localizationService.Get("Settings.Other.Subtitle");
        ShowNotificationsLabelText = _localizationService.Get("Settings.Other.ShowNotifications");
        DebugModeLabelText = _localizationService.Get("Settings.Other.DebugMode");
        LogLevelLabelText = _localizationService.Get("Settings.Other.LogLevel");
        MinimizeOnStartupLabelText = _localizationService.Get("Settings.Other.MinimizeOnStartup");
        MinimizeOnCloseLabelText = _localizationService.Get("Settings.Other.MinimizeOnClose");
        ThemeModeLabelText = _localizationService.Get("Settings.ThemeMode");
        UiLanguageLabelText = _localizationService.Get("Settings.UiLanguage");
        SaveButtonText = _localizationService.Get("Settings.Save");
        InstanceAutoConnectLabelText = _localizationService.Get("Settings.Basic.AutoConnect");
        InstanceServerAddressLabelText = _localizationService.Get("Settings.Basic.ServerAddress");
        InstanceSteamInviteCodeLabelText = _localizationService.Get("Settings.Basic.SteamInviteCode");
        OperationPathHint = _localizationService.Get("Settings.OperationPath");
        UpdateCardTitleText = _localizationService.Get("Settings.Card.Update");
        AutoUpdateCheckLabelText = _localizationService.Get("Settings.Update.AutoCheck");
        UpdateChannelLabelText = _localizationService.Get("Settings.Update.Channel");
        UpdateSourcePreferenceLabelText = _localizationService.Get("Settings.Update.SourcePreference");
        CheckUpdateButtonText = _localizationService.Get("Settings.Update.CheckNow");
        UpdateStatusLabelText = _localizationService.Get("Settings.Update.StatusLabel");
        LatestVersionLabelText = _localizationService.Get("Settings.Update.LatestLabel");
        UpdateSourceLabelText = _localizationService.Get("Settings.Update.SourceLabel");
        NxmProtocolCardTitleText = _localizationService.Get("Settings.Card.NxmProtocol");
        NxmProtocolStatusLabelText = _localizationService.Get("Settings.Nxm.Status");
        NxmAutoRegisterLabelText = _localizationService.Get("Settings.Nxm.AutoRegister");
        NxmProtocolDescriptionText = _localizationService.Get("Settings.Nxm.Description");
        NxmRegisterNowButtonText = _localizationService.Get("Settings.Nxm.RegisterNow");
    }

    private void ApplyImageResources()
    {
        BasicCardIconSource = _imageResourceService.Get("settings.card.basic");
        DownloadCardIconSource = _imageResourceService.Get("settings.card.download");
        NexusCardIconSource = _imageResourceService.Get("settings.card.nexus");
        UpdateCardIconSource = _imageResourceService.Get("settings.card.update");
        NxmProtocolCardIconSource = _imageResourceService.Get("settings.card.nxm");
        PersonalizationCardIconSource = _imageResourceService.Get("settings.card.personalization");
        OtherCardIconSource = _imageResourceService.Get("settings.card.other");
        AboutCardIconSource = _imageResourceService.Get("settings.card.about");
    }

    public bool IsBasicTab => SelectedTabIndex == 0;

    public bool IsDownloadTab => SelectedTabIndex == 1;

    public bool IsPersonalizationTab => SelectedTabIndex == 2;

    public bool IsOtherTab => SelectedTabIndex == 3;

    public bool IsAboutTab => SelectedTabIndex == 4;

    partial void OnSelectedTabIndexChanged(int value)
    {
        var normalizedIndex = Math.Clamp(value, 0, Tabs.Count - 1);
        if (normalizedIndex != value)
        {
            SelectedTabIndex = normalizedIndex;
            return;
        }

        OnPropertyChanged(nameof(IsBasicTab));
        OnPropertyChanged(nameof(IsDownloadTab));
        OnPropertyChanged(nameof(IsPersonalizationTab));
        OnPropertyChanged(nameof(IsOtherTab));
        OnPropertyChanged(nameof(IsAboutTab));

        StatusMessage = $"当前标签：{Tabs[normalizedIndex]}";
    }

    partial void OnLauncherTitleChanged(string value)
    {
        StatusMessage = "启动器标题已修改（未保存）";
    }

    partial void OnLauncherAppNameChanged(string value)
    {
        StatusMessage = "启动器简称已修改（未保存）";
    }

    partial void OnEnableDownloadCacheChanged(bool value)
    {
        StatusMessage = value ? "已启用下载缓存（未保存）" : "已禁用下载缓存（未保存）";
    }

    partial void OnSelectedThemeModeChanged(string value)
    {
        StatusMessage = $"主题模式切换为：{value}（未保存）";
    }

    partial void OnSelectedUiLanguageChanged(string value)
    {
        StatusMessage = $"界面语言切换为：{value}（未保存）";
    }

    partial void OnShowNotificationsChanged(bool value)
    {
        StatusMessage = value ? "已启用通知（未保存）" : "已禁用通知（未保存）";
    }

    partial void OnSelectedLogLevelChanged(string value)
    {
        StatusMessage = $"日志级别切换为：{value}（未保存）";
    }

    partial void OnDebugModeChanged(bool value)
    {
        StatusMessage = value ? "已启用调试模式（未保存）" : "已禁用调试模式（未保存）";
    }

    partial void OnMinimizeToTrayOnStartupChanged(bool value)
    {
        StatusMessage = value ? "已启用启动时最小化到托盘（未保存）" : "已禁用启动时最小化到托盘（未保存）";
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        StatusMessage = value ? "已启用关闭时最小化到托盘（未保存）" : "已禁用关闭时最小化到托盘（未保存）";
    }

    partial void OnSelectedCollectionConflictStrategyChanged(string value)
    {
        StatusMessage = $"Collection 冲突策略切换为：{value}（未保存）";
    }

    partial void OnSelectedCollectionDownloadParallelismChanged(int value)
    {
        StatusMessage = $"Collection 下载并发切换为：{Math.Clamp(value, 1, 8)}（未保存）";
    }

    partial void OnEnableAutoUpdateCheckChanged(bool value)
    {
        StatusMessage = value ? "已启用自动检查更新（未保存）" : "已禁用自动检查更新（未保存）";
    }

    partial void OnSelectedUpdateChannelChanged(string value)
    {
        StatusMessage = $"更新通道切换为：{value}（未保存）";
    }

    partial void OnSelectedUpdateSourceChanged(string value)
    {
        StatusMessage = $"更新源偏好切换为：{value}（未保存）";
    }

    partial void OnIsCheckingLauncherUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheckLauncherUpdate));
    }

    partial void OnRegisterNxmProtocolOnStartupChanged(bool value)
    {
        if (value)
        {
            TryRegisterNxmProtocolInternal(updateStatusMessage: false);
            StatusMessage = "已启用 NXM 启动自动注册（未保存）";
            return;
        }

        RefreshNxmProtocolStatus();
        StatusMessage = "已禁用 NXM 启动自动注册（未保存）";
    }

    partial void OnNexusApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsNexusLoggedIn));
        OnPropertyChanged(nameof(ShowNexusLoginGuide));
    }

    partial void OnNexusOAuthAccessTokenChanged(string value)
    {
        OnPropertyChanged(nameof(IsNexusLoggedIn));
        OnPropertyChanged(nameof(ShowNexusLoginGuide));
    }

    partial void OnNexusUserNameChanged(string value)
    {
        OnPropertyChanged(nameof(NexusDisplayName));
    }

    partial void OnNexusMembershipTypeChanged(string value)
    {
        OnPropertyChanged(nameof(NexusDisplayName));
    }

    [RelayCommand]
    private void SelectTab(object? index)
    {
        var parsed = index switch
        {
            int intValue => intValue,
            string textValue when int.TryParse(textValue, out var intValue) => intValue,
            _ => SelectedTabIndex
        };

        SelectedTabIndex = Math.Clamp(parsed, 0, Tabs.Count - 1);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settingsStore.Save(BuildSettings());
        _localizationService.SetLanguage(SelectedUiLanguage);
        StatusMessage = $"设置已保存（{System.DateTime.Now:HH:mm:ss}）";
    }

    [RelayCommand]
    private async Task OpenNexusLoginAsync()
    {
        var result = await _dialogService.ShowNexusLoginAsync(
            NexusApiKey,
            NexusOAuthAccessToken,
            NexusOAuthRefreshToken,
            NexusUserName,
            NexusMembershipType,
            NexusUserId,
            _nexusAuthService,
            _nexusOAuthService);
        if (result == null)
        {
            return;
        }

        NexusApiKey = result.ApiKey;
        NexusOAuthAccessToken = result.OAuthAccessToken;
        NexusOAuthRefreshToken = result.OAuthRefreshToken;
        NexusOAuthIdToken = result.OAuthIdToken;
        NexusUserName = result.UserName;
        NexusMembershipType = result.MembershipType;
        NexusUserId = result.UserId;
        NexusStatus = result.IsOAuthLogin ? "已登录（OAuth）" : "已登录（API Key 已验证）";

        _settingsStore.Save(BuildSettings());
        StatusMessage = "Nexus 登录状态已更新并保存";
    }

    [RelayCommand]
    private async Task ValidateNexusAsync()
    {
        if (string.IsNullOrWhiteSpace(NexusApiKey) && string.IsNullOrWhiteSpace(NexusOAuthAccessToken))
        {
            NexusStatus = "未登录";
            StatusMessage = "请先登录 Nexus";
            return;
        }

        NexusStatus = "正在验证...";
        if (!string.IsNullOrWhiteSpace(NexusApiKey))
        {
            var apiKeyResult = await _nexusAuthService.ValidateApiKeyAsync(NexusApiKey);
            if (!apiKeyResult.IsSuccess)
            {
                NexusStatus = apiKeyResult.Message;
                StatusMessage = "Nexus 验证失败";
                return;
            }

            NexusUserName = apiKeyResult.UserName;
            NexusMembershipType = apiKeyResult.MembershipType;
            NexusUserId = apiKeyResult.UserId;
            NexusStatus = "已登录（API Key 验证通过）";
            _settingsStore.Save(BuildSettings());
            StatusMessage = "Nexus 状态验证成功";
            return;
        }

        var oauthResult = await _nexusOAuthService.ValidateAccessTokenAsync(NexusOAuthAccessToken);
        if (!oauthResult.IsSuccess)
        {
            if (!string.IsNullOrWhiteSpace(NexusOAuthRefreshToken))
            {
                NexusStatus = "OAuth Token 无效，正在自动刷新...";
                var refreshed = await RefreshOAuthTokenInternalAsync();
                if (!refreshed)
                {
                    NexusStatus = oauthResult.Message;
                    StatusMessage = "Nexus OAuth 验证失败";
                    return;
                }

                oauthResult = await _nexusOAuthService.ValidateAccessTokenAsync(NexusOAuthAccessToken);
                if (!oauthResult.IsSuccess)
                {
                    NexusStatus = oauthResult.Message;
                    StatusMessage = "Nexus OAuth 验证失败";
                    return;
                }
            }
            else
            {
                NexusStatus = oauthResult.Message;
                StatusMessage = "Nexus OAuth 验证失败";
                return;
            }
        }

        NexusUserName = oauthResult.UserName;
        NexusMembershipType = oauthResult.MembershipType;
        NexusUserId = oauthResult.UserId;
        NexusStatus = "已登录（OAuth 验证通过）";
        _settingsStore.Save(BuildSettings());
        StatusMessage = "Nexus 状态验证成功";
    }

    [RelayCommand]
    private async Task RefreshOAuthTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(NexusOAuthRefreshToken))
        {
            StatusMessage = "当前没有可用的 OAuth Refresh Token";
            return;
        }

        NexusStatus = "正在刷新 OAuth Token...";
        var ok = await RefreshOAuthTokenInternalAsync();
        if (ok)
        {
            NexusStatus = "已登录（OAuth Token 已刷新）";
            StatusMessage = "OAuth Token 刷新成功";
            return;
        }

        NexusStatus = "OAuth Token 刷新失败";
        StatusMessage = "请重新登录 Nexus";
    }

    [RelayCommand]
    private void LogoutNexus()
    {
        NexusApiKey = string.Empty;
        NexusOAuthAccessToken = string.Empty;
        NexusOAuthRefreshToken = string.Empty;
        NexusOAuthIdToken = string.Empty;
        NexusUserName = string.Empty;
        NexusMembershipType = string.Empty;
        NexusUserId = 0;
        NexusStatus = "未登录";
        _settingsStore.Save(BuildSettings());
        StatusMessage = "已退出 Nexus";
    }

    [RelayCommand]
    private async Task CheckLauncherUpdate()
    {
        if (IsCheckingLauncherUpdate)
        {
            return;
        }

        IsCheckingLauncherUpdate = true;
        UpdateStatusText = _localizationService.Get("Settings.Update.Checking");
        StatusMessage = UpdateStatusText;

        try
        {
            var includePrerelease = string.Equals(SelectedUpdateChannel, "预览版", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(SelectedUpdateChannel, "preview", StringComparison.OrdinalIgnoreCase);
            var preferGitee = SelectedUpdateSource.Contains("Gitee", StringComparison.OrdinalIgnoreCase);

            var result = await _launcherUpdateService.CheckForUpdateAsync(includePrerelease, preferGitee);
            if (!result.Success || result.ReleaseInfo == null)
            {
                var failedMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? _localizationService.Get("Settings.Update.Failed")
                    : result.ErrorMessage;
                UpdateStatusText = failedMessage;
                StatusMessage = failedMessage;
                return;
            }

            LatestVersionText = $"v{result.LatestVersion}";
            UpdateSourceText = result.Source;

            if (!result.HasUpdate)
            {
                UpdateStatusText = _localizationService.Get("Settings.Update.UpToDate");
                StatusMessage = UpdateStatusText;
                return;
            }

            var release = result.ReleaseInfo;
            var releaseTag = release.TagName;

            if (!string.IsNullOrWhiteSpace(SkippedUpdateVersion) &&
                string.Equals(SkippedUpdateVersion, releaseTag, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusText = string.Format(_localizationService.Get("Settings.Update.Skipped"), releaseTag);
                StatusMessage = UpdateStatusText;
                return;
            }

            UpdateStatusText = string.Format(_localizationService.Get("Settings.Update.Found"), releaseTag);
            StatusMessage = UpdateStatusText;

            var action = await _dialogService.ShowUpdateDialogAsync(result.CurrentVersion, release, result.Source);
            if (action == UpdateDialogAction.SkipVersion)
            {
                SkippedUpdateVersion = releaseTag;
                _settingsStore.Save(BuildSettings());
                UpdateStatusText = string.Format(_localizationService.Get("Settings.Update.Skipped"), releaseTag);
                StatusMessage = UpdateStatusText;
                return;
            }

            if (action == UpdateDialogAction.OpenRelease)
            {
                var opened = !string.IsNullOrWhiteSpace(release.HtmlUrl) && _externalProcessService.TryOpenUrl(release.HtmlUrl);
                UpdateStatusText = opened
                    ? _localizationService.Get("Settings.Update.OpenReleaseSuccess")
                    : _localizationService.Get("Settings.Update.OpenReleaseFailed");
                StatusMessage = UpdateStatusText;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"{_localizationService.Get("Settings.Update.Failed")}: {ex.Message}";
            StatusMessage = UpdateStatusText;
        }
        finally
        {
            IsCheckingLauncherUpdate = false;
        }
    }

    [RelayCommand]
    private void RegisterNxmProtocolNow()
    {
        var result = TryRegisterNxmProtocolInternal(updateStatusMessage: true);
        StatusMessage = result.Message;
    }

    private void RefreshNxmProtocolStatus()
    {
        var status = _nxmProtocolRegistrationService.GetStatus();
        NxmProtocolStatusText = status.Message;
    }

    private NxmProtocolRegistrationResult TryRegisterNxmProtocolInternal(bool updateStatusMessage)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            NxmProtocolStatusText = "无法定位启动器路径，NXM 协议注册失败";
            return new NxmProtocolRegistrationResult
            {
                IsSuccess = false,
                IsSupported = OperatingSystem.IsWindows(),
                IsRegistered = false,
                Message = NxmProtocolStatusText
            };
        }

        var result = _nxmProtocolRegistrationService.TryRegister(executablePath);
        NxmProtocolStatusText = result.Message;

        if (updateStatusMessage)
        {
            StatusMessage = result.Message;
        }

        return result;
    }

    private async Task TryRefreshOAuthTokenSilentlyAsync()
    {
        if (string.IsNullOrWhiteSpace(NexusOAuthAccessToken) || string.IsNullOrWhiteSpace(NexusOAuthRefreshToken))
        {
            return;
        }

        var validation = await _nexusOAuthService.ValidateAccessTokenAsync(NexusOAuthAccessToken);
        if (validation.IsSuccess)
        {
            return;
        }

        var refreshed = await RefreshOAuthTokenInternalAsync();
        if (refreshed)
        {
            NexusStatus = "已登录（OAuth Token 已自动刷新）";
            StatusMessage = "检测到过期 Token，已自动刷新";
        }
    }

    private async Task<bool> RefreshOAuthTokenInternalAsync()
    {
        var refreshResult = await _nexusOAuthService.RefreshAccessTokenAsync(NexusOAuthRefreshToken);
        if (!refreshResult.IsSuccess || refreshResult.Token == null)
        {
            return false;
        }

        NexusOAuthAccessToken = refreshResult.Token.AccessToken;
        if (!string.IsNullOrWhiteSpace(refreshResult.Token.RefreshToken))
        {
            NexusOAuthRefreshToken = refreshResult.Token.RefreshToken;
        }

        if (!string.IsNullOrWhiteSpace(refreshResult.Token.IdToken))
        {
            NexusOAuthIdToken = refreshResult.Token.IdToken;
        }

        if (!string.IsNullOrWhiteSpace(refreshResult.Profile.UserName))
        {
            NexusUserName = refreshResult.Profile.UserName;
            NexusMembershipType = refreshResult.Profile.MembershipType;
            NexusUserId = refreshResult.Profile.UserId;
        }

        _settingsStore.Save(BuildSettings());
        return true;
    }
}
