using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform;
using SVL.Avalonia.Models;
using SVL.Core.Platform.Abstractions;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SVL.Avalonia.ViewModels;

public abstract partial class FeaturePageViewModelBase : ObservableObject
{
    public abstract string Title { get; }

    public abstract string Description { get; }
}

public sealed partial class TaskStatusPageViewModel : FeaturePageViewModelBase
{
    public override string Title => "任务状态";
    public override string Description => "对应 WPF TaskStatusView，承载下载任务进度、错误重试与状态跟踪。";

    public string CurrentTaskName { get; private set; } = "暂无任务";

    public string CurrentTaskStatus { get; private set; } = "-";

    public ObservableCollection<string> TaskLogs { get; } = [];

    public ObservableCollection<string> ConflictPreviewItems { get; } = [];

    public ObservableCollection<string> RetryReportHistory { get; } = [];

    public bool CanRetryFailedItems { get; private set; }

    public event Action? RetryFailedItemsRequested;

    public void SetCurrentTask(string name, string status)
    {
        CurrentTaskName = string.IsNullOrWhiteSpace(name) ? "暂无任务" : name;
        CurrentTaskStatus = string.IsNullOrWhiteSpace(status) ? "-" : status;
        OnPropertyChanged(nameof(CurrentTaskName));
        OnPropertyChanged(nameof(CurrentTaskStatus));

        var canRetry = status.Contains("可重试", StringComparison.Ordinal) ||
                       status.Contains("失败", StringComparison.Ordinal);
        SetCanRetryFailedItems(canRetry);
    }

    public void SetCanRetryFailedItems(bool canRetry)
    {
        if (CanRetryFailedItems == canRetry)
        {
            return;
        }

        CanRetryFailedItems = canRetry;
        OnPropertyChanged(nameof(CanRetryFailedItems));
    }

    public void AddLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        TaskLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (TaskLogs.Count > 200)
        {
            TaskLogs.RemoveAt(TaskLogs.Count - 1);
        }
    }

    public void AddRetryReport(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        if (RetryReportHistory.Contains(reportPath))
        {
            return;
        }

        RetryReportHistory.Insert(0, reportPath);
        while (RetryReportHistory.Count > 30)
        {
            RetryReportHistory.RemoveAt(RetryReportHistory.Count - 1);
        }
    }

    public void SetConflictPreview(IEnumerable<string> previewItems)
    {
        ConflictPreviewItems.Clear();
        foreach (var item in previewItems)
        {
            ConflictPreviewItems.Add(item);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        TaskLogs.Clear();
        AddLog("已清空任务日志");
    }

    [RelayCommand]
    private void RetryFailedItems()
    {
        if (!CanRetryFailedItems)
        {
            AddLog("当前任务没有可重试失败项");
            return;
        }

        RetryFailedItemsRequested?.Invoke();
        AddLog("已发起失败项重试请求");
    }
}

public sealed partial class ModSearchPageViewModel : FeaturePageViewModelBase
{
    private readonly Services.RemoteCatalogService _catalogService;

    public override string Title => "Mod 搜索";
    public override string Description => "对应 WPF ModSearchView，承载 Nexus/Curseforge 搜索与筛选。";

    public event Action<string>? OpenDetailsRequested;

    public ObservableCollection<string> Sources { get; } = ["全部", "NexusMods", "Curseforge"];

    public ObservableCollection<string> Results { get; } = [];

    public string Query { get; set; } = string.Empty;

    public string SelectedSource { get; set; } = "全部";

    public string Status { get; private set; } = "请输入关键词开始搜索";

    public ModSearchPageViewModel(Services.RemoteCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [RelayCommand]
    private async Task Search()
    {
        Results.Clear();

        if (string.IsNullOrWhiteSpace(Query))
        {
            Status = "请输入 Mod 关键词";
            OnPropertyChanged(nameof(Status));
            return;
        }

        Status = "正在请求远程数据源...";
        OnPropertyChanged(nameof(Status));

        try
        {
            var remote = await _catalogService.SearchModsAsync(Query, SelectedSource);
            foreach (var item in remote)
            {
                Results.Add(item);
            }
        }
        catch (Exception ex)
        {
            Status = $"远程搜索失败: {ex.Message}";
            OnPropertyChanged(nameof(Status));
            return;
        }

        Status = $"已找到 {Results.Count} 条结果";
        OnPropertyChanged(nameof(Status));
    }

    [RelayCommand]
    private void OpenDetails(string? item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        OpenDetailsRequested?.Invoke(item);
    }
}

public sealed partial class ModpackSearchPageViewModel : FeaturePageViewModelBase
{
    private readonly Services.RemoteCatalogService _catalogService;

    public override string Title => "Modpack 搜索";
    public override string Description => "对应 WPF ModpackSearchView，承载整合包搜索与导入流程。";

    public event Action<string>? OpenDetailsRequested;

    public ObservableCollection<string> Sources { get; } = ["全部", "NexusMods", "Curseforge"];

    public ObservableCollection<string> Results { get; } = [];

    public string Query { get; set; } = string.Empty;

    public string SelectedSource { get; set; } = "全部";

    public string Status { get; private set; } = "请输入关键词开始搜索";

    public ModpackSearchPageViewModel(Services.RemoteCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [RelayCommand]
    private async Task Search()
    {
        Results.Clear();

        if (string.IsNullOrWhiteSpace(Query))
        {
            Status = "请输入 Modpack 关键词";
            OnPropertyChanged(nameof(Status));
            return;
        }

        Status = "正在请求远程数据源...";
        OnPropertyChanged(nameof(Status));

        try
        {
            var remote = await _catalogService.SearchModpacksAsync(Query, SelectedSource);
            foreach (var item in remote)
            {
                Results.Add(item);
            }
        }
        catch (Exception ex)
        {
            Status = $"远程搜索失败: {ex.Message}";
            OnPropertyChanged(nameof(Status));
            return;
        }

        Status = $"已找到 {Results.Count} 条结果";
        OnPropertyChanged(nameof(Status));
    }

    [RelayCommand]
    private void OpenDetails(string? item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        OpenDetailsRequested?.Invoke(item);
    }
}

public sealed partial class ModDetailsPageViewModel : FeaturePageViewModelBase
{
    private readonly Services.RemoteCatalogService _catalogService;

    public override string Title => "资源详情";
    public override string Description => "对应 WPF ModDetailsView，展示资源信息、版本与下载动作。";

    public event Action<ExternalDownloadRequest>? QueueDownloadRequested;

    public string ResourceName { get; private set; } = "未选择资源";

    public string ResourceNotes { get; private set; } = "-";

    public string ResourceSource { get; private set; } = "-";

    public string DetailsStatus { get; private set; } = "等待加载";

    public ObservableCollection<string> VersionOptions { get; } = [];

    public ObservableCollection<string> DependencyItems { get; } = [];

    public ObservableCollection<string> DownloadOptions { get; } = [];

    public string SelectedDownloadOption { get; set; } = string.Empty;

    public ModDetailsPageViewModel(Services.RemoteCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public void SetResource(string name, string notes)
    {
        ResourceName = string.IsNullOrWhiteSpace(name) ? "未选择资源" : name;
        ResourceNotes = string.IsNullOrWhiteSpace(notes) ? "-" : notes;
        ResourceSource = "-";
        DetailsStatus = "待加载详情";
        VersionOptions.Clear();
        DependencyItems.Clear();
        DownloadOptions.Clear();
        SelectedDownloadOption = string.Empty;
        OnPropertyChanged(nameof(ResourceName));
        OnPropertyChanged(nameof(ResourceNotes));
        OnPropertyChanged(nameof(ResourceSource));
        OnPropertyChanged(nameof(DetailsStatus));
        OnPropertyChanged(nameof(SelectedDownloadOption));
    }

    public async Task LoadDetailsAsync(string displayText)
    {
        DetailsStatus = "正在加载详情...";
        OnPropertyChanged(nameof(DetailsStatus));

        var details = await _catalogService.GetResourceDetailsAsync(displayText);
        ResourceName = string.IsNullOrWhiteSpace(details.Name) ? displayText : details.Name;
        ResourceSource = string.IsNullOrWhiteSpace(details.Source) ? "-" : details.Source;
        ResourceNotes = string.IsNullOrWhiteSpace(details.Summary) ? "-" : details.Summary;

        VersionOptions.Clear();
        foreach (var item in details.VersionOptions.Take(12))
        {
            VersionOptions.Add(item);
        }

        DependencyItems.Clear();
        foreach (var item in details.Dependencies.Take(12))
        {
            DependencyItems.Add(item);
        }

        DownloadOptions.Clear();
        foreach (var item in details.DownloadOptions.Take(20))
        {
            DownloadOptions.Add(item);
        }

        SelectedDownloadOption = DownloadOptions.FirstOrDefault() ?? string.Empty;
        DetailsStatus = "详情已加载";
        OnPropertyChanged(nameof(ResourceName));
        OnPropertyChanged(nameof(ResourceSource));
        OnPropertyChanged(nameof(ResourceNotes));
        OnPropertyChanged(nameof(SelectedDownloadOption));
        OnPropertyChanged(nameof(DetailsStatus));
    }

    [RelayCommand]
    private void QueueDownload()
    {
        QueueDownloadRequested?.Invoke(new ExternalDownloadRequest
        {
            ResourceName = ResourceName,
            ResourceSource = ResourceSource,
            SelectedDownloadOption = SelectedDownloadOption
        });
    }
}

public sealed partial class VersionSettingsPageViewModel : FeaturePageViewModelBase
{
    private readonly Services.AppUserSettingsStore _settingsStore;
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly Services.LocalizationService _localizationService;
    private readonly Services.ImageResourceService _imageResourceService;
    private readonly Services.DialogService _dialogService;
    private string _titleText = "版本设置";
    private string _descriptionText = "实例级配置。";

    public event Action? NavigateToSmapiDownloadRequested;
    public event Action? InstanceContextChanged;

    public override string Title => _titleText;
    public override string Description => _descriptionText;

    [ObservableProperty]
    private string _generalSectionButtonText = "版本设置";

    [ObservableProperty]
    private string _modManageSectionButtonText = "Mod管理";

    [ObservableProperty]
    private string _detectedGamePathLabelText = "探测到的游戏目录";

    [ObservableProperty]
    private string _defaultLaunchModeLabelText = "默认启动模式";

    [ObservableProperty]
    private string _safeLaunchCheckBoxText = "启用安全启动（保守参数）";

    [ObservableProperty]
    private string _refreshDetectedPathButtonText = "刷新探测";

    [ObservableProperty]
    private string _saveVersionSettingsButtonText = "保存设置";

    [ObservableProperty]
    private string _modManageTitleText = "Mod 管理";

    [ObservableProperty]
    private string _reloadModsButtonText = "刷新列表";

    [ObservableProperty]
    private string _openModsFolderButtonText = "打开 Mods 文件夹";

    [ObservableProperty]
    private string _selectedModTitleText = "当前选中 Mod";

    [ObservableProperty]
    private string _modOperationsHintText = "当前 Mod 操作（启用/禁用会直接改目录后缀）：";

    [ObservableProperty]
    private string _enableModButtonText = "启用";

    [ObservableProperty]
    private string _disableModButtonText = "禁用";

    [ObservableProperty]
    private string _uninstallModButtonText = "卸载";

    [ObservableProperty]
    private string _checkUpdateModButtonText = "检查更新";

    [ObservableProperty]
    private string _emptyModsHintText = "当前未检测到 Mod。可先在下载页安装，或通过“打开 Mods 文件夹”手动放入。";

    [ObservableProperty]
    private string _modColumnTitleText = "Mod";

    [ObservableProperty]
    private string _versionColumnTitleText = "版本";

    [ObservableProperty]
    private string _statusColumnTitleText = "状态";

    [ObservableProperty]
    private string _updateColumnTitleText = "更新";

    [ObservableProperty]
    private string _modDescriptionLabelText = "描述：";

    [ObservableProperty]
    private string _overviewVersionCardTitleText = "版本信息";

    [ObservableProperty]
    private string _overviewPersonalizationCardTitleText = "个性化";

    [ObservableProperty]
    private string _instanceNameLabelText = "版本名称";

    [ObservableProperty]
    private string _instanceDescriptionLabelText = "版本描述";

    [ObservableProperty]
    private string _favoriteInstanceLabelText = "收藏实例";

    [ObservableProperty]
    private string _savePersonalizationButtonText = "保存个性化";

    [ObservableProperty]
    private string _changeIconButtonText = "更改图标";

    [ObservableProperty]
    private string _overviewShortcutsCardTitleText = "快捷方式";

    [ObservableProperty]
    private string _openInstanceFolderButtonText = "📁 版本文件夹";

    [ObservableProperty]
    private string _openSaveFolderButtonText = "💾 存档文件夹";

    [ObservableProperty]
    private string _openModsFolderShortcutButtonText = "📦 Mod 文件夹";

    [ObservableProperty]
    private string _overviewAdvancedCardTitleText = "高级管理";

    [ObservableProperty]
    private string _advancedWarningText = "危险操作区域：以下操作不可逆，请谨慎操作";

    [ObservableProperty]
    private string _uninstallBaseSmapiButtonText = "🧹 卸载 Base SMAPI";

    [ObservableProperty]
    private string _deleteCurrentVersionButtonText = "删除当前版本";

    [ObservableProperty]
    private string _deleteCurrentVersionHintText = "Base 版本不可删除，仅可卸载其 SMAPI。";

    [ObservableProperty]
    private string _autoInstallInfoTitleText = "安装选项";

    [ObservableProperty]
    private string _changeSmapiButtonText = "安装 / 更改 SMAPI";

    [ObservableProperty]
    private string _smapiGuideText = "点击上方按钮可以选择并安装不同版本的 SMAPI。安装将覆盖当前版本。";

    [ObservableProperty]
    private string _switchToSmapiButtonText = "切换到 SMAPI";

    [ObservableProperty]
    private string _switchToSmapiTipText = "检测到该路径已安装SMAPI，切换到SMAPI版本以启用Mod管理功能。";

    [ObservableProperty]
    private string _overviewNavText = "概览";

    [ObservableProperty]
    private string _autoInstallNavText = "自动安装";

    [ObservableProperty]
    private string _modManageNavText = "Mod管理";

    [ObservableProperty]
    private string _instanceSettingsNavText = "设置";

    [ObservableProperty]
    private string _exportNavText = "导出";

    [ObservableProperty]
    private string _instanceSettingsTitleText = "实例设置";

    [ObservableProperty]
    private string _instanceName = "Default Instance";

    [ObservableProperty]
    private string _instanceDescription = string.Empty;

    [ObservableProperty]
    private string _gameWindowTitle = "<default>";

    [ObservableProperty]
    private string _instanceCustomLaunchArguments = string.Empty;

    [ObservableProperty]
    private bool _isFavoriteInstance;

    [ObservableProperty]
    private bool _overrideSteamLaunchOptions;

    [ObservableProperty]
    private string _steamLaunchOptions = string.Empty;

    [ObservableProperty]
    private string _saveInstanceSettingsButtonText = "保存实例设置";

    [ObservableProperty]
    private string _exportSectionTitleText = "导出";

    [ObservableProperty]
    private string _exportNamePrefix = "SVL-Modpack";

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    [ObservableProperty]
    private string _exportCurrentModsButtonText = "导出当前 Mods";

    [ObservableProperty]
    private string _openExportFolderButtonText = "打开导出目录";

    [ObservableProperty]
    private string _selectedSection = "Overview";

    [ObservableProperty]
    private string _detectedGamePath = "未探测到";

    [ObservableProperty]
    private string _selectedLaunchMode = "自动";

    [ObservableProperty]
    private string _instanceIconSource = "avares://SVL.Avalonia/Assets/Icons/Vanilla.png";

    [ObservableProperty]
    private string _instanceDisplayName = "未选择实例";

    [ObservableProperty]
    private string _instanceVersionText = "未知版本";

    [ObservableProperty]
    private bool _isSmapiInstance;

    [ObservableProperty]
    private bool _hasInstalledSmapi;

    [ObservableProperty]
    private string _smapiVersionText = "未安装";

    [ObservableProperty]
    private bool _enableSafeLaunch;

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private bool _isModManageSection;

    [ObservableProperty]
    private ModManageItem? _selectedMod;

    [ObservableProperty]
    private string _modManageHint = "请选择一个 Mod 进行管理";

    public ObservableCollection<ModManageItem> Mods { get; } = [];

    public bool IsGeneralSection => IsOverviewSection;

    public bool IsOverviewSection => string.Equals(SelectedSection, "Overview", StringComparison.Ordinal);

    public bool IsAutoInstallSection => string.Equals(SelectedSection, "AutoInstall", StringComparison.Ordinal);

    public bool IsSettingsSection => string.Equals(SelectedSection, "Settings", StringComparison.Ordinal);

    public bool IsExportSection => string.Equals(SelectedSection, "Export", StringComparison.Ordinal);

    public bool ShowSmapiVersion => IsSmapiInstance;

    public bool ShowNoSmapiHint => !IsSmapiInstance;

    public bool ShowBaseModeModWarning => !IsSmapiInstance && !HasInstalledSmapi;

    public bool ShowSwitchToSmapiHint => HasInstalledSmapi && !IsSmapiInstance;

    public bool CanManageMods => IsSmapiInstance;

    public bool CanChangeSmapiVersion => !ShowSwitchToSmapiHint;

    public bool HasSelectedMod => SelectedMod != null;

    public bool HasMods => Mods.Count > 0;

    public bool ShowEmptyModsHint => !HasMods;

    public bool CanOperateSelectedMod => SelectedMod != null;

    public string CurrentInstanceFolderPath => ResolveCurrentInstancePath();

    public string SaveFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StardewValley",
        "Saves");

    public string ModsFolderPath => Path.Combine(CurrentInstanceFolderPath, "Mods");

    public bool HasSaveFolder => Directory.Exists(SaveFolderPath);

    public bool HasModsFolder => Directory.Exists(ModsFolderPath);

    public bool CanDeleteCurrentVersion => TryGetVersionRootDirectory(CurrentInstanceFolderPath, out _);

    public bool CanUninstallBaseSmapi => !CanDeleteCurrentVersion && HasInstalledSmapi;

    public string ModsSummary => Mods.Count == 0
        ? "当前实例 Mods 目录为空"
        : $"共 {Mods.Count} 个 Mod：启用 {Mods.Count(item => item.IsEnabled)} 个，禁用 {Mods.Count(item => !item.IsEnabled)} 个";

    public int EnabledModsCount => Mods.Count(item => item.IsEnabled);

    public int DisabledModsCount => Mods.Count(item => !item.IsEnabled);

    public bool CanEnableSelectedMod => SelectedMod is { IsEnabled: false };

    public bool CanDisableSelectedMod => SelectedMod is { IsEnabled: true };

    public string SelectedModDetails => SelectedMod == null
        ? "当前未选中 Mod"
        : $"{SelectedMod.DisplayName} · {SelectedMod.Version} · {SelectedMod.EnableStateText} · 目录: {SelectedMod.DirectoryName}";

    public string ExportHint => string.IsNullOrWhiteSpace(LastExportPath)
        ? "尚未导出"
        : $"最近导出: {LastExportPath}";

    public ObservableCollection<string> LaunchModes { get; } = ["自动", "SMAPI", "原版"];

    public string DefaultSteamLaunchOptions => BuildDefaultSteamLaunchOptions();

    public string SteamLaunchOptionsPreview
    {
        get
        {
            var options = (SteamLaunchOptions ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(options))
            {
                options = DefaultSteamLaunchOptions;
            }

            return string.IsNullOrWhiteSpace(options)
                ? "（无法生成默认参数，请确认实例路径）"
                : options;
        }
    }

    public VersionSettingsPageViewModel(
        Services.AppUserSettingsStore settingsStore,
        IGameInstallPathLocator gameInstallPathLocator,
        Services.LocalizationService localizationService,
        Services.ImageResourceService imageResourceService,
        Services.DialogService dialogService)
    {
        _settingsStore = settingsStore;
        _gameInstallPathLocator = gameInstallPathLocator;
        _localizationService = localizationService;
        _imageResourceService = imageResourceService;
        _dialogService = dialogService;
        _localizationService.LanguageChanged += ApplyLocalizedTexts;
        _imageResourceService.ResourcesChanged += RefreshInstanceRuntimeInfo;

        ApplyLocalizedTexts();

        Mods.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(ShowEmptyModsHint));
            OnPropertyChanged(nameof(ModsSummary));
            OnPropertyChanged(nameof(EnabledModsCount));
            OnPropertyChanged(nameof(DisabledModsCount));
        };

        ReloadFromSettings();
        SelectedSection = "Overview";
    }

    public void ReloadFromSettings(bool reloadModsWhenActive = false)
    {
        var settings = _settingsStore.Load();
        SelectedLaunchMode = string.IsNullOrWhiteSpace(settings.PreferredLaunchMode)
            ? "自动"
            : settings.PreferredLaunchMode;
        EnableSafeLaunch = settings.EnableSafeLaunch;
        InstanceName = settings.InstanceName;
        InstanceDescription = settings.InstanceDescription;
        GameWindowTitle = string.IsNullOrWhiteSpace(settings.GameWindowTitle) ? "<default>" : settings.GameWindowTitle;
        InstanceCustomLaunchArguments = settings.InstanceCustomLaunchArguments;
        IsFavoriteInstance = settings.IsFavoriteInstance;
        OverrideSteamLaunchOptions = settings.OverrideSteamLaunchOptions;
        SteamLaunchOptions = settings.SteamLaunchOptions;

        RefreshDetectedPathCore(updateStatus: false);

        if (string.IsNullOrWhiteSpace(SteamLaunchOptions))
        {
            SteamLaunchOptions = DefaultSteamLaunchOptions;
        }

        OnPropertyChanged(nameof(DefaultSteamLaunchOptions));
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));

        if (reloadModsWhenActive && IsModManageSection)
        {
            ReloadMods();
        }
    }

    private void ApplyLocalizedTexts()
    {
        _titleText = L("VersionSettings.Title", "版本设置");
        _descriptionText = L("VersionSettings.Description", "实例级配置。");
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));

        GeneralSectionButtonText = L("VersionSettings.Tab.General", "版本设置");
        ModManageSectionButtonText = L("VersionSettings.Tab.ModManage", "Mod管理");
        DetectedGamePathLabelText = L("VersionSettings.General.DetectedPathLabel", "探测到的游戏目录");
        DefaultLaunchModeLabelText = L("VersionSettings.General.LaunchModeLabel", "默认启动模式");
        SafeLaunchCheckBoxText = L("VersionSettings.General.SafeLaunch", "启用安全启动（保守参数）");
        RefreshDetectedPathButtonText = L("VersionSettings.General.RefreshPath", "刷新探测");
        SaveVersionSettingsButtonText = L("VersionSettings.General.Save", "保存设置");

        ModManageTitleText = L("VersionSettings.ModManage.Title", "Mod 管理");
        ReloadModsButtonText = L("VersionSettings.ModManage.Reload", "刷新列表");
        OpenModsFolderButtonText = L("VersionSettings.ModManage.OpenFolder", "打开 Mods 文件夹");
        SelectedModTitleText = L("VersionSettings.ModManage.SelectedTitle", "当前选中 Mod");
        ModOperationsHintText = L("VersionSettings.ModManage.OperationsHint", "当前 Mod 操作（启用/禁用会直接改目录后缀）：");
        EnableModButtonText = L("VersionSettings.ModManage.Enable", "启用");
        DisableModButtonText = L("VersionSettings.ModManage.Disable", "禁用");
        UninstallModButtonText = L("VersionSettings.ModManage.Uninstall", "卸载");
        CheckUpdateModButtonText = L("VersionSettings.ModManage.CheckUpdate", "检查更新");
        EmptyModsHintText = L("VersionSettings.ModManage.EmptyHint", "当前未检测到 Mod。可先在下载页安装，或通过“打开 Mods 文件夹”手动放入。");
        ModColumnTitleText = L("VersionSettings.ModManage.Column.Mod", "Mod");
        VersionColumnTitleText = L("VersionSettings.ModManage.Column.Version", "版本");
        StatusColumnTitleText = L("VersionSettings.ModManage.Column.Status", "状态");
        UpdateColumnTitleText = L("VersionSettings.ModManage.Column.Update", "更新");
        ModDescriptionLabelText = L("VersionSettings.ModManage.DescriptionLabel", "描述：");

        OverviewNavText = L("VersionSettings.Nav.Overview", "概览");
        AutoInstallNavText = L("VersionSettings.Nav.AutoInstall", "自动安装");
        ModManageNavText = L("VersionSettings.Nav.ModManage", "Mod管理");
        InstanceSettingsNavText = L("VersionSettings.Nav.Settings", "设置");
        ExportNavText = L("VersionSettings.Nav.Export", "导出");
        OverviewVersionCardTitleText = L("VersionSettings.Overview.VersionInfoTitle", "版本信息");
        OverviewPersonalizationCardTitleText = L("VersionSettings.Overview.PersonalizationTitle", "个性化");
        InstanceNameLabelText = L("VersionSettings.Overview.InstanceNameLabel", "版本名称");
        InstanceDescriptionLabelText = L("VersionSettings.Overview.InstanceDescriptionLabel", "版本描述");
        FavoriteInstanceLabelText = L("VersionSettings.Overview.FavoriteLabel", "收藏实例");
        SavePersonalizationButtonText = L("VersionSettings.Overview.SavePersonalization", "保存个性化");
        ChangeIconButtonText = L("VersionSettings.Overview.ChangeIcon", "更改图标");
        OverviewShortcutsCardTitleText = L("VersionSettings.Overview.ShortcutsTitle", "快捷方式");
        OpenInstanceFolderButtonText = L("VersionSettings.Overview.Shortcut.InstanceFolder", "📁 版本文件夹");
        OpenSaveFolderButtonText = L("VersionSettings.Overview.Shortcut.SaveFolder", "💾 存档文件夹");
        OpenModsFolderShortcutButtonText = L("VersionSettings.Overview.Shortcut.ModsFolder", "📦 Mod 文件夹");
        OverviewAdvancedCardTitleText = L("VersionSettings.Overview.AdvancedTitle", "高级管理");
        AdvancedWarningText = L("VersionSettings.Overview.AdvancedWarning", "危险操作区域：以下操作不可逆，请谨慎操作");
        UninstallBaseSmapiButtonText = L("VersionSettings.Overview.UninstallBaseSmapi", "🧹 卸载 Base SMAPI");
        DeleteCurrentVersionButtonText = L("VersionSettings.Overview.DeleteVersion", "删除当前版本");
        DeleteCurrentVersionHintText = L("VersionSettings.Overview.DeleteVersionHint", "Base 版本不可删除，仅可卸载其 SMAPI。");
        AutoInstallInfoTitleText = L("VersionSettings.AutoInstall.OptionsTitle", "安装选项");
        ChangeSmapiButtonText = L("VersionSettings.AutoInstall.ChangeSmapi", "安装 / 更改 SMAPI");
        SmapiGuideText = L("VersionSettings.AutoInstall.Guide", "点击上方按钮可以选择并安装不同版本的 SMAPI。安装将覆盖当前版本。");
        SwitchToSmapiButtonText = L("VersionSettings.AutoInstall.SwitchToSmapi", "切换到 SMAPI");
        SwitchToSmapiTipText = L("VersionSettings.AutoInstall.SwitchTip", "检测到该路径已安装SMAPI，切换到SMAPI版本以启用Mod管理功能。");
        InstanceSettingsTitleText = L("VersionSettings.Settings.Title", "实例设置");
        SaveInstanceSettingsButtonText = L("VersionSettings.Settings.Save", "保存实例设置");
        ExportSectionTitleText = L("VersionSettings.Export.Title", "导出");
        ExportCurrentModsButtonText = L("VersionSettings.Export.CurrentMods", "导出当前 Mods");
        OpenExportFolderButtonText = L("VersionSettings.Export.OpenFolder", "打开导出目录");

        OnPropertyChanged(nameof(ModsSummary));
        OnPropertyChanged(nameof(SelectedModDetails));
        OnPropertyChanged(nameof(ExportHint));
        RefreshModManageHint();
        NotifyOverviewActionStateChanged();
    }

    private string L(string key, string fallback)
    {
        var value = _localizationService.Get(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private string F(string key, string fallback, params object[] args)
    {
        return string.Format(L(key, fallback), args);
    }

    private void NotifyInstanceContextChanged()
    {
        InstanceContextChanged?.Invoke();
    }

    public void SwitchToGeneral()
    {
        SelectedSection = "Overview";
        IsModManageSection = false;
        Status = "当前处于版本设置";
    }

    public void SwitchToOverview()
    {
        SwitchToGeneral();
    }

    public void SwitchToModManage()
    {
        SelectedSection = "ModManage";
        IsModManageSection = true;
    }

    public void SwitchToSettings()
    {
        SelectedSection = "Settings";
        IsModManageSection = false;
        Status = "当前处于实例设置";
    }

    public void SwitchToExport()
    {
        SelectedSection = "Export";
        IsModManageSection = false;
        Status = "当前处于导出页面";
    }

    public void SwitchToAutoInstall()
    {
        SelectedSection = "AutoInstall";
        IsModManageSection = false;
        Status = "自动安装页面迁移中";
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverviewSection));
        OnPropertyChanged(nameof(IsAutoInstallSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(IsExportSection));
        OnPropertyChanged(nameof(IsGeneralSection));

        if (string.Equals(value, "ModManage", StringComparison.Ordinal))
        {
            IsModManageSection = true;
            ReloadMods();
            return;
        }

        IsModManageSection = false;
    }

    partial void OnIsModManageSectionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
    }

    partial void OnSelectedLaunchModeChanged(string value)
    {
        RefreshInstanceRuntimeInfo();
        OnPropertyChanged(nameof(DefaultSteamLaunchOptions));
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));
    }

    partial void OnOverrideSteamLaunchOptionsChanged(bool value)
    {
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));
    }

    partial void OnSteamLaunchOptionsChanged(string value)
    {
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));
    }

    partial void OnInstanceCustomLaunchArgumentsChanged(string value)
    {
        OnPropertyChanged(nameof(DefaultSteamLaunchOptions));
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));
    }

    partial void OnGameWindowTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DefaultSteamLaunchOptions));
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));
    }

    partial void OnIsSmapiInstanceChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSmapiVersion));
        OnPropertyChanged(nameof(ShowNoSmapiHint));
        OnPropertyChanged(nameof(ShowBaseModeModWarning));
        OnPropertyChanged(nameof(ShowSwitchToSmapiHint));
        OnPropertyChanged(nameof(CanManageMods));
        OnPropertyChanged(nameof(CanChangeSmapiVersion));
    }

    partial void OnHasInstalledSmapiChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBaseModeModWarning));
        OnPropertyChanged(nameof(ShowSwitchToSmapiHint));
        OnPropertyChanged(nameof(CanChangeSmapiVersion));
        NotifyOverviewActionStateChanged();
    }

    private void NotifyOverviewActionStateChanged()
    {
        OnPropertyChanged(nameof(CurrentInstanceFolderPath));
        OnPropertyChanged(nameof(SaveFolderPath));
        OnPropertyChanged(nameof(ModsFolderPath));
        OnPropertyChanged(nameof(HasSaveFolder));
        OnPropertyChanged(nameof(HasModsFolder));
        OnPropertyChanged(nameof(CanDeleteCurrentVersion));
        OnPropertyChanged(nameof(CanUninstallBaseSmapi));
    }

    partial void OnSelectedModChanged(ModManageItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedMod));
        OnPropertyChanged(nameof(CanOperateSelectedMod));
        OnPropertyChanged(nameof(CanEnableSelectedMod));
        OnPropertyChanged(nameof(CanDisableSelectedMod));
        OnPropertyChanged(nameof(SelectedModDetails));
        RefreshModManageHint();
    }

    private void RefreshModManageHint(string? overrideHint = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideHint))
        {
            ModManageHint = overrideHint;
            return;
        }

        if (SelectedMod == null)
        {
            ModManageHint = ShowEmptyModsHint
                ? "未检测到 Mod，可前往下载页安装后再返回管理"
                : "请选择一个 Mod 进行启用、禁用或卸载";
            return;
        }

        ModManageHint = $"已选中：{SelectedMod.DisplayName}（{SelectedMod.EnableStateText}，{SelectedMod.UpdateStatus}）";
    }

    private void AttachModItem(ModManageItem item)
    {
        item.PropertyChanged += HandleModItemPropertyChanged;
    }

    private void DetachModItem(ModManageItem item)
    {
        item.PropertyChanged -= HandleModItemPropertyChanged;
    }

    private void HandleModItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ModManageItem.IsEnabled), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ModsSummary));
            OnPropertyChanged(nameof(EnabledModsCount));
            OnPropertyChanged(nameof(DisabledModsCount));
        }

        if (ReferenceEquals(sender, SelectedMod) &&
            (string.Equals(e.PropertyName, nameof(ModManageItem.IsEnabled), StringComparison.Ordinal) ||
             string.Equals(e.PropertyName, nameof(ModManageItem.UpdateStatus), StringComparison.Ordinal) ||
             string.Equals(e.PropertyName, nameof(ModManageItem.DisplayName), StringComparison.Ordinal) ||
             string.Equals(e.PropertyName, nameof(ModManageItem.Version), StringComparison.Ordinal) ||
             string.Equals(e.PropertyName, nameof(ModManageItem.DirectoryName), StringComparison.Ordinal)))
        {
            OnPropertyChanged(nameof(SelectedModDetails));
            OnPropertyChanged(nameof(CanEnableSelectedMod));
            OnPropertyChanged(nameof(CanDisableSelectedMod));
            RefreshModManageHint();
        }
    }

    private bool TryGetSelectedModForAction(string actionText, out ModManageItem target)
    {
        if (SelectedMod == null)
        {
            Status = $"请先选择需要{actionText}的 Mod";
            RefreshModManageHint($"请先选择 Mod，再执行{actionText}操作");
            target = null!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedMod.FullPath) || !Directory.Exists(SelectedMod.FullPath))
        {
            Status = $"{actionText}失败：目标目录不可用";
            RefreshModManageHint("目标目录已丢失，请刷新列表后重试");
            target = null!;
            return false;
        }

        target = SelectedMod;
        return true;
    }

    [RelayCommand]
    private void SwitchToGeneralSection()
    {
        SwitchToOverview();
    }

    [RelayCommand]
    private void SwitchToAutoInstallSection()
    {
        SwitchToAutoInstall();
    }

    [RelayCommand]
    private void SwitchToModManageSection()
    {
        SwitchToModManage();
    }

    [RelayCommand]
    private void SwitchToInstanceSettingsSection()
    {
        SwitchToSettings();
    }

    [RelayCommand]
    private void SwitchToExportSection()
    {
        SwitchToExport();
    }

    [RelayCommand]
    private void OpenCurrentInstanceModsFolder()
    {
        var settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.PreferredInstancePath) || !Directory.Exists(settings.PreferredInstancePath))
        {
            Status = "当前实例目录不可用，无法打开 Mods 文件夹";
            RefreshModManageHint("当前实例目录不可用，请先在版本选择页选中实例");
            return;
        }

        var modsPath = Path.Combine(settings.PreferredInstancePath, "Mods");
        Directory.CreateDirectory(modsPath);
        Status = $"Mod 文件夹: {modsPath}";
        RefreshModManageHint("已打开 Mods 文件夹，可直接拖入或整理 Mod");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = modsPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            Status = "打开 Mods 文件夹失败，请手动前往实例目录";
            RefreshModManageHint("打开文件夹失败，请检查系统权限后重试");
        }
    }

    [RelayCommand]
    private void ReloadMods()
    {
        foreach (var existingItem in Mods)
        {
            DetachModItem(existingItem);
        }

        Mods.Clear();
        SelectedMod = null;

        var settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.PreferredInstancePath) || !Directory.Exists(settings.PreferredInstancePath))
        {
            Status = "当前未选择可用实例，请先在版本选择中选中实例";
            RefreshModManageHint("未找到可用实例，无法加载 Mod 列表");
            return;
        }

        var modsPath = Path.Combine(settings.PreferredInstancePath, "Mods");
        Directory.CreateDirectory(modsPath);

        var modDirectories = Directory.GetDirectories(modsPath)
            .OrderBy(path =>
            {
                var folderName = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(folderName) &&
                       folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
            })
            .ThenBy(path =>
            {
                var folderName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    return string.Empty;
                }

                return folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? folderName[..^".disabled".Length]
                    : folderName;
            }, StringComparer.OrdinalIgnoreCase);

        foreach (var modDirectory in modDirectories)
        {
            var folderName = Path.GetFileName(modDirectory);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var isEnabled = !folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var actualName = isEnabled
                ? folderName
                : folderName[..^".disabled".Length];

            var manifestPath = Path.Combine(modDirectory, "manifest.json");
            var displayName = actualName;
            var version = "未知版本";
            var author = string.Empty;
            var description = string.Empty;

            if (File.Exists(manifestPath))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                    if (doc.RootElement.TryGetProperty("Name", out var nameElement))
                    {
                        displayName = nameElement.GetString() ?? displayName;
                    }

                    if (doc.RootElement.TryGetProperty("Version", out var versionElement))
                    {
                        version = versionElement.GetString() ?? version;
                    }
                    
                    if (doc.RootElement.TryGetProperty("Author", out var authorElement))
                    {
                        author = authorElement.GetString() ?? author;
                    }
                    
                    if (doc.RootElement.TryGetProperty("Description", out var descriptionElement))
                    {
                        description = descriptionElement.GetString() ?? description;
                    }
                }
                catch
                {
                    // keep fallback values when manifest parsing fails
                }
            }

            var item = new ModManageItem
            {
                DisplayName = displayName,
                Version = version,
                Author = author,
                Description = description,
                DirectoryName = folderName,
                FullPath = modDirectory,
                IsEnabled = isEnabled,
                UpdateStatus = "未检查"
            };
            AttachModItem(item);
            Mods.Add(item);
        }

        Status = Mods.Count == 0
            ? "当前实例 Mods 目录为空"
            : $"已加载 {Mods.Count} 个 Mod（启用 {Mods.Count(item => item.IsEnabled)} / 禁用 {Mods.Count(item => !item.IsEnabled)}）";
        RefreshModManageHint();
    }

    [RelayCommand]
    private void EnableSelectedMod()
    {
        if (!TryGetSelectedModForAction("启用", out var target))
        {
            return;
        }

        if (target.IsEnabled)
        {
            return;
        }

        if (!target.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            Status = "启用失败：当前 Mod 目录后缀异常";
            RefreshModManageHint("启用失败：目录后缀异常，请刷新列表后重试");
            return;
        }

        var newPath = target.FullPath[..^".disabled".Length];
        if (Directory.Exists(newPath))
        {
            Status = "启用失败：已存在同名启用目录";
            RefreshModManageHint("启用失败：存在同名目录，请先处理冲突后重试");
            return;
        }

        try
        {
            Directory.Move(target.FullPath, newPath);
            target.FullPath = newPath;
            target.DirectoryName = Path.GetFileName(newPath);
            target.IsEnabled = true;
            target.UpdateStatus = "已启用（本次）";
            Status = $"已启用 Mod: {target.DisplayName}";
            RefreshModManageHint($"已启用 {target.DisplayName}，可直接启动游戏验证");
            OnPropertyChanged(nameof(CanEnableSelectedMod));
            OnPropertyChanged(nameof(CanDisableSelectedMod));
            OnPropertyChanged(nameof(ModsSummary));
            OnPropertyChanged(nameof(SelectedModDetails));
        }
        catch (Exception ex)
        {
            Status = $"启用失败: {ex.Message}";
            RefreshModManageHint($"启用失败：{target.DisplayName}");
        }
    }

    [RelayCommand]
    private void DisableSelectedMod()
    {
        if (!TryGetSelectedModForAction("禁用", out var target))
        {
            return;
        }

        if (!target.IsEnabled)
        {
            return;
        }

        if (target.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            Status = "禁用失败：当前 Mod 已处于禁用目录";
            RefreshModManageHint("禁用失败：目录后缀异常，请刷新列表后重试");
            return;
        }

        var newPath = $"{target.FullPath}.disabled";
        if (Directory.Exists(newPath))
        {
            Status = "禁用失败：已存在同名禁用目录";
            RefreshModManageHint("禁用失败：存在同名目录，请先处理冲突后重试");
            return;
        }

        try
        {
            Directory.Move(target.FullPath, newPath);
            target.FullPath = newPath;
            target.DirectoryName = Path.GetFileName(newPath);
            target.IsEnabled = false;
            target.UpdateStatus = "已禁用（本次）";
            Status = $"已禁用 Mod: {target.DisplayName}";
            RefreshModManageHint($"已禁用 {target.DisplayName}，需要时可重新启用");
            OnPropertyChanged(nameof(CanEnableSelectedMod));
            OnPropertyChanged(nameof(CanDisableSelectedMod));
            OnPropertyChanged(nameof(ModsSummary));
            OnPropertyChanged(nameof(SelectedModDetails));
        }
        catch (Exception ex)
        {
            Status = $"禁用失败: {ex.Message}";
            RefreshModManageHint($"禁用失败：{target.DisplayName}");
        }
    }

    [RelayCommand]
    private void UninstallSelectedMod()
    {
        if (!TryGetSelectedModForAction("卸载", out var target))
        {
            return;
        }

        var targetIndex = Mods.IndexOf(target);
        try
        {
            Directory.Delete(target.FullPath, true);
            DetachModItem(target);
            Mods.Remove(target);
            SelectedMod = Mods.Count == 0
                ? null
                : Mods[Math.Clamp(targetIndex, 0, Mods.Count - 1)];
            target.UpdateStatus = "已卸载";
            Status = $"已卸载 Mod: {target.DisplayName}";
            RefreshModManageHint($"已卸载 {target.DisplayName}，可在下载页重新安装");
            OnPropertyChanged(nameof(ModsSummary));
        }
        catch (Exception ex)
        {
            Status = $"卸载失败: {ex.Message}";
            RefreshModManageHint($"卸载失败：{target.DisplayName}");
        }
    }

    [RelayCommand]
    private void CheckUpdateSelectedMod()
    {
        if (!TryGetSelectedModForAction("检查更新", out var target))
        {
            return;
        }

        target.UpdateStatus = $"已检查 {DateTime.Now:HH:mm}";
        Status = $"已检查更新入口: {target.DisplayName}";
        RefreshModManageHint($"已完成更新检查：{target.DisplayName}");
        OnPropertyChanged(nameof(SelectedModDetails));
    }

    [RelayCommand]
    private void SaveInstanceSettings()
    {
        var settings = _settingsStore.Load();
        settings.InstanceName = string.IsNullOrWhiteSpace(InstanceName) ? "Default Instance" : InstanceName.Trim();
        settings.InstanceDescription = InstanceDescription?.Trim() ?? string.Empty;
        settings.GameWindowTitle = string.IsNullOrWhiteSpace(GameWindowTitle) ? "<default>" : GameWindowTitle.Trim();
        settings.InstanceCustomLaunchArguments = InstanceCustomLaunchArguments?.Trim() ?? string.Empty;
        settings.IsFavoriteInstance = IsFavoriteInstance;
        settings.OverrideSteamLaunchOptions = OverrideSteamLaunchOptions;
        settings.SteamLaunchOptions = SteamLaunchOptions?.Trim() ?? string.Empty;
        _settingsStore.Save(settings);

        RefreshInstanceRuntimeInfo();
        Status = $"实例设置已保存（{DateTime.Now:HH:mm:ss}）";
        NotifyInstanceContextChanged();
    }

    [RelayCommand]
    private void ResetStartupOptions()
    {
        GameWindowTitle = "<default>";
        InstanceCustomLaunchArguments = string.Empty;

        if (string.IsNullOrWhiteSpace(SteamLaunchOptions))
        {
            SteamLaunchOptions = DefaultSteamLaunchOptions;
        }

        Status = "已重置启动选项到默认值";
    }

    [RelayCommand]
    private async Task WriteSteamLaunchOptionsAsync()
    {
        var options = (SteamLaunchOptions ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(options))
        {
            options = DefaultSteamLaunchOptions;
        }

        if (string.IsNullOrWhiteSpace(options))
        {
            Status = "写入失败：无法生成默认 Steam 启动参数";
            await _dialogService.ShowMessageAsync("写入失败", "无法生成 Steam 启动参数，请确认实例路径和启动参数配置。");
            return;
        }

        var steamWasRunning = IsSteamRunning();
        var steamClosedByLauncher = false;
        if (steamWasRunning)
        {
            var approved = await _dialogService.ShowConfirmAsync(
                "需要关闭 Steam",
                "检测到 Steam 正在运行。\n\n需要先关闭 Steam 才能可靠写入启动参数。\n\n是否允许 SVL 自动关闭 Steam，写入后再尝试重启 Steam？");
            if (!approved)
            {
                return;
            }

            var closeResult = await Task.Run(TryCloseSteam);
            if (!closeResult.Success)
            {
                Status = $"写入失败：{closeResult.ErrorMessage}";
                await _dialogService.ShowMessageAsync("写入失败", closeResult.ErrorMessage);
                return;
            }

            steamClosedByLauncher = true;
        }

        var writeResult = await Task.Run(() => TryWriteLaunchOptionsToSteamUserConfig(options));
        if (!writeResult.Success)
        {
            Status = $"写入失败：{writeResult.ErrorMessage}";
            await _dialogService.ShowMessageAsync("写入失败", writeResult.ErrorMessage);
            return;
        }

        OverrideSteamLaunchOptions = true;
        SteamLaunchOptions = options;

        var settings = _settingsStore.Load();
        settings.OverrideSteamLaunchOptions = true;
        settings.SteamLaunchOptions = options;
        _settingsStore.Save(settings);

        var restartWarning = string.Empty;
        if (steamClosedByLauncher)
        {
            var restartResult = await Task.Run(TryStartSteam);
            if (!restartResult.Success)
            {
                restartWarning = $"\n\n启动参数已写入，但自动重启 Steam 失败：{restartResult.ErrorMessage}\n请手动启动 Steam。";
            }
        }

        Status = $"Steam 启动参数已写入（修改 {writeResult.UpdatedFileCount} 个配置，匹配 {writeResult.MatchedFileCount} 个账号）";
        await _dialogService.ShowMessageAsync(
            "写入完成",
            $"已写入 Steam 启动参数（修改 {writeResult.UpdatedFileCount} 个配置，匹配 {writeResult.MatchedFileCount} 个账号）。{restartWarning}");
        NotifyInstanceContextChanged();
    }

    [RelayCommand]
    private void ChangeSmapiVersion()
    {
        if (ShowSwitchToSmapiHint)
        {
            Status = "检测到当前路径已安装 SMAPI，请先切换到 SMAPI 版本";
            return;
        }

        NavigateToSmapiDownloadRequested?.Invoke();
        Status = "已进入下载页 SMAPI 分类，请选择版本并执行安装";
    }

    [RelayCommand]
    private async Task ChangeIcon()
    {
        var selected = await _dialogService.ShowIconPickerDialogAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var instancePath = ResolveCurrentInstancePath();
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            Status = "当前实例目录不可用，无法保存图标";
            return;
        }

        var iconStorageDir = Services.InstanceIconResolver.ResolveStorageDirectory(instancePath);
        if (string.IsNullOrWhiteSpace(iconStorageDir))
        {
            Status = "图标保存路径不可用";
            return;
        }

        try
        {
            var targetPath = Path.Combine(iconStorageDir, ".svl-instance-icon.png");
            Directory.CreateDirectory(iconStorageDir);

            if (selected.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = AssetLoader.Open(new Uri(selected, UriKind.Absolute));
                using var output = File.Create(targetPath);
                stream.CopyTo(output);
            }
            else
            {
                if (!File.Exists(selected))
                {
                    Status = "所选图标文件不存在";
                    return;
                }

                File.Copy(selected, targetPath, true);
            }

            RefreshInstanceRuntimeInfo();
            Status = "图标已更新";
            NotifyInstanceContextChanged();
        }
        catch (Exception ex)
        {
            Status = $"图标保存失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Status = "目标目录不存在";
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            Status = $"已打开目录: {path}";
        }
        catch (Exception ex)
        {
            Status = $"打开目录失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteCurrentVersion()
    {
        var instancePath = ResolveCurrentInstancePath();
        if (!TryGetVersionRootDirectory(instancePath, out var versionRoot))
        {
            Status = "Base 版本不可删除，仅可卸载其 SMAPI";
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            "确认删除版本",
            $"确定要删除当前版本目录吗？\n\n{versionRoot}\n\n该操作不可撤销。");

        if (!confirmed)
        {
            return;
        }

        try
        {
            Directory.Delete(versionRoot, true);
            var settings = _settingsStore.Load();
            if (!string.IsNullOrWhiteSpace(settings.PreferredInstancePath) &&
                settings.PreferredInstancePath.StartsWith(versionRoot, StringComparison.OrdinalIgnoreCase))
            {
                settings.PreferredInstancePath = string.Empty;
                _settingsStore.Save(settings);
            }

            RefreshInstanceRuntimeInfo();
            Status = "当前版本目录已删除，请在版本选择页重新选择实例";
            NotifyOverviewActionStateChanged();
        }
        catch (Exception ex)
        {
            Status = $"删除版本失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UninstallBaseSmapi()
    {
        if (!CanUninstallBaseSmapi)
        {
            Status = "当前实例不是可卸载的 Base SMAPI 实例";
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            "确认卸载 Base SMAPI",
            "将移除当前 Base 路径下的 SMAPI 可执行文件，并保留你的 Mods。是否继续？");

        if (!confirmed)
        {
            return;
        }

        var instancePath = ResolveCurrentInstancePath();
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            Status = "当前实例目录不可用";
            return;
        }

        try
        {
            var smapiFiles = new[]
            {
                "StardewModdingAPI.exe",
                "StardewModdingAPI",
                "StardewModdingAPI.dll"
            };

            foreach (var file in smapiFiles)
            {
                var fullPath = Path.Combine(instancePath, file);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }

            HasInstalledSmapi = false;
            IsSmapiInstance = false;
            SmapiVersionText = "未安装";
            SelectedLaunchMode = "原版";
            SaveVersionSettings();
            RefreshInstanceRuntimeInfo();
            Status = "Base SMAPI 已卸载";
        }
        catch (Exception ex)
        {
            Status = $"卸载 Base SMAPI 失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SwitchToSmapiVersion()
    {
        if (!ShowSwitchToSmapiHint)
        {
            Status = "当前无需切换到 SMAPI";
            return;
        }

        SelectedLaunchMode = "SMAPI";
        SaveVersionSettings();
        Status = "已切换到 SMAPI 启动模式";
    }

    [RelayCommand]
    private void ExportCurrentMods()
    {
        var settings = _settingsStore.Load();
        var gamePath = settings.PreferredInstancePath;

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            gamePath = _gameInstallPathLocator.TryLocateSteamStardewPath() ?? _gameInstallPathLocator.TryLocateGogStardewPath();
        }

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            Status = "未探测到游戏目录，无法导出";
            return;
        }

        var modsPath = Path.Combine(gamePath, "Mods");
        if (!Directory.Exists(modsPath))
        {
            Status = "当前实例不存在 Mods 目录，无法导出";
            return;
        }

        try
        {
            var exportRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia",
                "Exports");
            Directory.CreateDirectory(exportRoot);

            var prefix = string.IsNullOrWhiteSpace(ExportNamePrefix) ? "SVL-Modpack" : ExportNamePrefix.Trim();
            var fileName = $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}.zip";
            var exportPath = Path.Combine(exportRoot, fileName);

            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            ZipFile.CreateFromDirectory(modsPath, exportPath, CompressionLevel.SmallestSize, true);
            LastExportPath = exportPath;
            OnPropertyChanged(nameof(ExportHint));
            Status = $"导出成功（{DateTime.Now:HH:mm:ss}）";
        }
        catch (Exception ex)
        {
            Status = $"导出失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        var path = LastExportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "暂无可打开的导出文件";
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                Status = "导出目录不可用";
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            Status = "已打开导出目录";
        }
        catch
        {
            Status = "打开导出目录失败";
        }
    }

    [RelayCommand]
    private void RefreshDetectedPath()
    {
        RefreshDetectedPathCore(updateStatus: true);
    }

    private void RefreshDetectedPathCore(bool updateStatus)
    {
        var gamePath = _gameInstallPathLocator.TryLocateSteamStardewPath() ?? _gameInstallPathLocator.TryLocateGogStardewPath();
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            DetectedGamePath = "未探测到";
            RefreshInstanceRuntimeInfo();
            if (updateStatus)
            {
                Status = "未找到游戏目录，请先在实例页配置路径";
            }
            return;
        }

        DetectedGamePath = gamePath;
        RefreshInstanceRuntimeInfo();
        if (updateStatus)
        {
            Status = $"已探测到路径（{DateTime.Now:HH:mm:ss}）";
        }
    }

    [RelayCommand]
    private void SaveVersionSettings()
    {
        var settings = _settingsStore.Load();
        settings.PreferredLaunchMode = SelectedLaunchMode;
        settings.EnableSafeLaunch = EnableSafeLaunch;
        _settingsStore.Save(settings);

        RefreshInstanceRuntimeInfo();
        Status = $"版本设置已保存（{DateTime.Now:HH:mm:ss}）";
        NotifyInstanceContextChanged();
    }

    private void RefreshInstanceRuntimeInfo()
    {
        var settings = _settingsStore.Load();
        var preferredPath = settings.PreferredInstancePath;
        var path = !string.IsNullOrWhiteSpace(preferredPath) && Directory.Exists(preferredPath)
            ? preferredPath
            : DetectedGamePath;
        var hasValidPath = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

        InstanceDisplayName = string.IsNullOrWhiteSpace(settings.InstanceName)
            ? ResolvePathDisplayName(path)
            : settings.InstanceName;

        InstanceDescription = settings.InstanceDescription ?? string.Empty;
        IsFavoriteInstance = settings.IsFavoriteInstance;

        InstanceVersionText = DetectGameVersion(path);
        var (hasSmapi, smapiVersion) = DetectSmapi(path);
        HasInstalledSmapi = hasSmapi;
        SmapiVersionText = hasSmapi ? smapiVersion : "未安装";

        var mode = NormalizeLaunchMode(SelectedLaunchMode);
        var preferSmapiByName = !string.IsNullOrWhiteSpace(settings.InstanceName) &&
                                settings.InstanceName.Contains("SMAPI", StringComparison.OrdinalIgnoreCase);
        IsSmapiInstance = string.Equals(mode, "smapi", StringComparison.OrdinalIgnoreCase) ||
                          (string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase) && preferSmapiByName);

        if (!hasValidPath)
        {
            SetInstanceIconSource(GetImageResource("launch.instance.none", "avares://SVL.Avalonia/Assets/Icons/Junimo.png"));
        }
        else
        {
            var customIcon = Services.InstanceIconResolver.ResolveIconPath(path);
            if (!string.IsNullOrWhiteSpace(customIcon))
            {
                SetInstanceIconSource(customIcon);
            }
            else
            {
                var iconKey = IsSmapiInstance
                    ? "launch.instance.modded"
                    : "launch.instance.vanilla";
                var fallback = IsSmapiInstance
                    ? "avares://SVL.Avalonia/Assets/Icons/Modded.png"
                    : "avares://SVL.Avalonia/Assets/Icons/Vanilla.png";
                SetInstanceIconSource(GetImageResource(iconKey, fallback));
            }
        }

        OnPropertyChanged(nameof(DefaultSteamLaunchOptions));
        OnPropertyChanged(nameof(SteamLaunchOptionsPreview));

        NotifyOverviewActionStateChanged();
    }

    private void SetInstanceIconSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var index = source.IndexOfAny(['?', '#']);
        var normalized = index < 0 ? source : source[..index];

        if (File.Exists(normalized))
        {
            InstanceIconSource = $"{normalized}?v={DateTime.UtcNow.Ticks}";
            return;
        }

        if (string.Equals(InstanceIconSource, normalized, StringComparison.OrdinalIgnoreCase))
        {
            InstanceIconSource = string.Empty;
        }

        InstanceIconSource = normalized;
    }

    private string BuildDefaultSteamLaunchOptions()
    {
        var instancePath = ResolveCurrentInstancePath();
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            return string.Empty;
        }

        var smapiCandidates = new[]
        {
            Path.Combine(instancePath, "StardewModdingAPI.exe"),
            Path.Combine(instancePath, "StardewModdingAPI"),
            Path.Combine(instancePath, "StardewModdingAPI.dll")
        };

        var smapiPath = smapiCandidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(smapiPath))
        {
            smapiPath = OperatingSystem.IsWindows()
                ? Path.Combine(instancePath, "StardewModdingAPI.exe")
                : Path.Combine(instancePath, "StardewModdingAPI");
        }

        var optionsBuilder = new StringBuilder($"\"{smapiPath}\" %command%");
        var customArgs = (InstanceCustomLaunchArguments ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(customArgs))
        {
            optionsBuilder.Append(' ');
            optionsBuilder.Append(customArgs);
        }

        return optionsBuilder.ToString();
    }

    private static bool IsSteamRunning()
    {
        try
        {
            return Process.GetProcesses().Any(IsSteamProcess);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSteamProcess(Process process)
    {
        var name = process.ProcessName;
        return string.Equals(name, "steam", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool Success, string ErrorMessage) TryCloseSteam()
    {
        try
        {
            var steamProcesses = Process.GetProcesses().Where(IsSteamProcess).ToArray();
            if (steamProcesses.Length == 0)
            {
                return (true, string.Empty);
            }

            foreach (var process in steamProcesses)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch
                {
                    // Continue handling the remaining processes.
                }
            }

            var gracefulDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < gracefulDeadline)
            {
                if (!IsSteamRunning())
                {
                    return (true, string.Empty);
                }

                System.Threading.Thread.Sleep(200);
            }

            foreach (var process in Process.GetProcesses().Where(IsSteamProcess))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Keep going; we validate again below.
                }
            }

            var forcedDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < forcedDeadline)
            {
                if (!IsSteamRunning())
                {
                    return (true, string.Empty);
                }

                System.Threading.Thread.Sleep(200);
            }

            return (false, "无法关闭 Steam，请先手动关闭后再重试。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Success, string ErrorMessage) TryStartSteam()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe")
                };

                var steamExe = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(steamExe))
                {
                    return (false, "未找到 steam.exe。");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = true
                });
                return (true, string.Empty);
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { "-a", "Steam" },
                    UseShellExecute = false
                });
                return (true, string.Empty);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "steam",
                UseShellExecute = false
            });
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private (bool Success, int UpdatedFileCount, int MatchedFileCount, string ErrorMessage) TryWriteLaunchOptionsToSteamUserConfig(string launchOptions)
    {
        try
        {
            var configFiles = GetSteamLocalConfigFilesInPriorityOrder().ToList();
            if (configFiles.Count == 0)
            {
                return (false, 0, 0, "未找到任何 Steam 账号配置（userdata 下无 localconfig.vdf）。");
            }

            var updatedCount = 0;
            var matchedCount = 0;

            foreach (var configPath in configFiles)
            {
                if (!TryUpsertLaunchOptionsInLocalConfig(configPath, "413150", launchOptions, out var changed, out var matched))
                {
                    continue;
                }

                if (matched)
                {
                    matchedCount++;
                }

                if (changed)
                {
                    updatedCount++;
                }
            }

            if (matchedCount == 0)
            {
                return (false, 0, 0, "未在 Steam 配置中找到 Stardew Valley（AppId 413150）条目。请先通过 Steam 启动一次游戏后再试。");
            }

            return (true, updatedCount, matchedCount, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, 0, 0, ex.Message);
        }
    }

    private IEnumerable<string> GetSteamLocalConfigFilesInPriorityOrder()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var userdataRoot in GetSteamUserdataRoots())
        {
            if (!Directory.Exists(userdataRoot))
            {
                continue;
            }

            var accountDirs = Directory.GetDirectories(userdataRoot)
                .Where(path => long.TryParse(Path.GetFileName(path), out _));

            foreach (var accountDir in accountDirs)
            {
                var configPath = Path.Combine(accountDir, "config", "localconfig.vdf");
                if (!File.Exists(configPath))
                {
                    continue;
                }

                if (seen.Add(configPath))
                {
                    ordered.Add(configPath);
                }
            }
        }

        return ordered;
    }

    private IEnumerable<string> GetSteamUserdataRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void TryAdd(HashSet<string> set, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!Directory.Exists(path))
            {
                return;
            }

            set.Add(path);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        TryAdd(roots, Path.Combine(home, "Library", "Application Support", "Steam", "userdata"));
        TryAdd(roots, Path.Combine(home, ".steam", "steam", "userdata"));
        TryAdd(roots, Path.Combine(home, ".local", "share", "Steam", "userdata"));
        TryAdd(roots, Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "userdata"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        TryAdd(roots, Path.Combine(programFilesX86, "Steam", "userdata"));
        TryAdd(roots, Path.Combine(programFiles, "Steam", "userdata"));

        var steamGamePath = _gameInstallPathLocator.TryLocateSteamStardewPath();
        if (!string.IsNullOrWhiteSpace(steamGamePath) && Directory.Exists(steamGamePath))
        {
            var current = new DirectoryInfo(steamGamePath);
            while (current != null)
            {
                if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    var steamRoot = current.Parent?.FullName;
                    TryAdd(roots, Path.Combine(steamRoot ?? string.Empty, "userdata"));
                    break;
                }

                current = current.Parent;
            }
        }

        return roots;
    }

    private static bool TryUpsertLaunchOptionsInLocalConfig(
        string configPath,
        string appId,
        string launchOptions,
        out bool changed,
        out bool matched)
    {
        changed = false;
        matched = false;

        var lines = File.ReadAllLines(configPath).ToList();
        if (lines.Count == 0)
        {
            return false;
        }

        var escaped = launchOptions.Replace("\\", "\\\\").Replace("\"", "\\\"");

        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.Equals(lines[i].Trim(), "\"apps\"", StringComparison.Ordinal))
            {
                continue;
            }

            var appsBraceLine = FindNextNonEmptyLine(lines, i + 1);
            if (appsBraceLine < 0 || lines[appsBraceLine].Trim() != "{")
            {
                continue;
            }

            var appsEndLine = FindMatchingBraceLine(lines, appsBraceLine);
            if (appsEndLine < 0)
            {
                continue;
            }

            var appKey = $"\"{appId}\"";
            var appLine = -1;
            for (var j = appsBraceLine + 1; j < appsEndLine; j++)
            {
                if (!string.Equals(lines[j].Trim(), appKey, StringComparison.Ordinal))
                {
                    continue;
                }

                appLine = j;
                break;
            }

            if (appLine >= 0)
            {
                matched = true;
                var appBodyStart = FindNextNonEmptyLine(lines, appLine + 1);
                if (appBodyStart < 0 || lines[appBodyStart].Trim() != "{")
                {
                    return false;
                }

                var appBodyEnd = FindMatchingBraceLine(lines, appBodyStart);
                if (appBodyEnd < 0)
                {
                    return false;
                }

                var launchLine = -1;
                for (var j = appBodyStart + 1; j < appBodyEnd; j++)
                {
                    if (!lines[j].TrimStart().StartsWith("\"LaunchOptions\"", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    launchLine = j;
                    break;
                }

                if (launchLine >= 0)
                {
                    var originalLine = lines[launchLine];
                    var keyPos = originalLine.IndexOf("\"LaunchOptions\"", StringComparison.Ordinal);
                    var valueStart = keyPos >= 0
                        ? originalLine.IndexOf('"', keyPos + "\"LaunchOptions\"".Length)
                        : -1;

                    if (valueStart < 0)
                    {
                        var prefixLen = originalLine.IndexOf('"');
                        var prefix = prefixLen >= 0 ? originalLine[..prefixLen] : string.Empty;
                        var fallbackLine = $"{prefix}\"LaunchOptions\"\t\t\"{escaped}\"";
                        if (!string.Equals(originalLine, fallbackLine, StringComparison.Ordinal))
                        {
                            lines[launchLine] = fallbackLine;
                            changed = true;
                        }
                    }
                    else
                    {
                        var valueEnd = FindNextUnescapedQuote(originalLine, valueStart + 1);
                        if (valueEnd > valueStart)
                        {
                            var rewritten = originalLine[..(valueStart + 1)]
                                            + escaped
                                            + originalLine[valueEnd..];

                            if (!string.Equals(originalLine, rewritten, StringComparison.Ordinal))
                            {
                                lines[launchLine] = rewritten;
                                changed = true;
                            }
                        }
                    }
                }
                else
                {
                    var indent = GetLineIndent(lines[appLine]) + "\t";
                    lines.Insert(appBodyEnd, $"{indent}\"LaunchOptions\"\t\t\"{escaped}\"");
                    changed = true;
                }

                break;
            }

            var appIndent = GetLineIndent(lines[appsBraceLine]) + "\t";
            var appLines = new[]
            {
                $"{appIndent}{appKey}",
                $"{appIndent}{{",
                $"{appIndent}\t\"LaunchOptions\"\t\t\"{escaped}\"",
                $"{appIndent}}}"
            };
            lines.InsertRange(appsEndLine, appLines);
            matched = true;
            changed = true;
            break;
        }

        if (changed)
        {
            File.WriteAllLines(configPath, lines, Encoding.UTF8);
        }

        return true;
    }

    private static int FindNextNonEmptyLine(List<string> lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMatchingBraceLine(List<string> lines, int openBraceLine)
    {
        var depth = 0;
        for (var i = openBraceLine; i < lines.Count; i++)
        {
            var line = lines[i];
            for (var c = 0; c < line.Length; c++)
            {
                if (line[c] == '{')
                {
                    depth++;
                }
                else if (line[c] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
        }

        return -1;
    }

    private static int FindNextUnescapedQuote(string text, int startIndex)
    {
        for (var i = startIndex; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                continue;
            }

            var slashCount = 0;
            for (var j = i - 1; j >= 0 && text[j] == '\\'; j--)
            {
                slashCount++;
            }

            if (slashCount % 2 == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetLineIndent(string line)
    {
        var idx = 0;
        while (idx < line.Length && char.IsWhiteSpace(line[idx]))
        {
            idx++;
        }

        return idx > 0 ? line[..idx] : string.Empty;
    }

    private string ResolveCurrentInstancePath()
    {
        var settings = _settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.PreferredInstancePath) && Directory.Exists(settings.PreferredInstancePath))
        {
            return settings.PreferredInstancePath;
        }

        return !string.IsNullOrWhiteSpace(DetectedGamePath) && Directory.Exists(DetectedGamePath)
            ? DetectedGamePath
            : string.Empty;
    }

    private static bool TryGetVersionRootDirectory(string instancePath, out string versionRoot)
    {
        versionRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            return false;
        }

        var current = new DirectoryInfo(instancePath);
        DirectoryInfo? child = null;

        while (current != null)
        {
            if (string.Equals(current.Name, "versions", StringComparison.OrdinalIgnoreCase))
            {
                if (child != null)
                {
                    versionRoot = child.FullName;
                    return true;
                }

                return false;
            }

            child = current;
            current = current.Parent;
        }

        return false;
    }

    private string GetImageResource(string key, string fallback)
    {
        var value = _imageResourceService.Get(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string ResolvePathDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Default Instance";
        }

        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(name) ? "Default Instance" : name;
    }

    private static string DetectGameVersion(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return "未知版本";
        }

        var depsPath = Path.Combine(gamePath, "Stardew Valley.deps.json");
        if (File.Exists(depsPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(depsPath));
                if (doc.RootElement.TryGetProperty("targets", out var targetsElement))
                {
                    foreach (var target in targetsElement.EnumerateObject())
                    {
                        foreach (var package in target.Value.EnumerateObject())
                        {
                            if (package.Name.StartsWith("Stardew Valley/", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = package.Name.Split('/');
                                if (parts.Length == 2)
                                {
                                    return parts[1];
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // fallback to file version
            }
        }

        var dllPath = Path.Combine(gamePath, "Stardew Valley.dll");
        if (File.Exists(dllPath))
        {
            try
            {
                var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
                return string.IsNullOrWhiteSpace(fileVersion) ? "未知版本" : fileVersion;
            }
            catch
            {
                return "未知版本";
            }
        }

        return "未知版本";
    }

    private static (bool HasSmapi, string Version) DetectSmapi(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return (false, "未安装");
        }

        var markers = new[]
        {
            Path.Combine(gamePath, "StardewModdingAPI.exe"),
            Path.Combine(gamePath, "StardewModdingAPI"),
            Path.Combine(gamePath, "StardewModdingAPI.dll")
        };

        var markerPath = markers.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return (false, "未安装");
        }

        try
        {
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(markerPath).FileVersion;
            return (true, string.IsNullOrWhiteSpace(version) ? "Unknown" : version);
        }
        catch
        {
            return (true, "Unknown");
        }
    }

    private static string NormalizeLaunchMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "auto";
        }

        if (string.Equals(mode, "SMAPI", StringComparison.OrdinalIgnoreCase))
        {
            return "smapi";
        }

        if (string.Equals(mode, "原版", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            return "vanilla";
        }

        return "auto";
    }
}

public partial class ModManageItem : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _version = "未知版本";

    [ObservableProperty]
    private string _directoryName = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _updateStatus = "待检查";

    public string EnableStateText => IsEnabled ? "已启用" : "已禁用";

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnableStateText));
    }
}

public sealed partial class InstanceSettingsPageViewModel : FeaturePageViewModelBase
{
    private readonly Services.AppUserSettingsStore _settingsStore;

    public override string Title => "实例设置";
    public override string Description => "对应 WPF InstanceSettingsView，承载 Steam 参数与实例元数据。";

    public string InstanceName { get; set; } = "Default Instance";

    public bool OverrideSteamLaunchOptions { get; set; }

    public string SteamLaunchOptions { get; set; } = string.Empty;

    public string Status { get; private set; } = "已加载";

    public InstanceSettingsPageViewModel(Services.AppUserSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Load();
        InstanceName = settings.InstanceName;
        OverrideSteamLaunchOptions = settings.OverrideSteamLaunchOptions;
        SteamLaunchOptions = settings.SteamLaunchOptions;
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsStore.Load();
        settings.InstanceName = InstanceName;
        settings.OverrideSteamLaunchOptions = OverrideSteamLaunchOptions;
        settings.SteamLaunchOptions = SteamLaunchOptions;
        _settingsStore.Save(settings);

        Status = $"实例设置已保存（{DateTime.Now:HH:mm:ss}）";
        OnPropertyChanged(nameof(Status));
    }
}

public sealed partial class ExportPageViewModel : FeaturePageViewModelBase
{
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly IExternalProcessService _externalProcessService;
    private readonly Services.AppUserSettingsStore _settingsStore;

    public override string Title => "导出";
    public override string Description => "对应 WPF ExportView，承载 Modpack 导出链路。";

    [ObservableProperty]
    private string _exportNamePrefix = "SVL-Modpack";

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    [ObservableProperty]
    private string _status = "就绪";

    public ExportPageViewModel(
        IGameInstallPathLocator gameInstallPathLocator,
        IExternalProcessService externalProcessService,
        Services.AppUserSettingsStore settingsStore)
    {
        _gameInstallPathLocator = gameInstallPathLocator;
        _externalProcessService = externalProcessService;
        _settingsStore = settingsStore;
    }

    [RelayCommand]
    private void ExportCurrentMods()
    {
        var gamePath = _gameInstallPathLocator.TryLocateSteamStardewPath() ?? _gameInstallPathLocator.TryLocateGogStardewPath();
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            Status = "未探测到游戏目录，无法导出";
            return;
        }

        var modsPath = Path.Combine(gamePath, "Mods");
        if (!Directory.Exists(modsPath))
        {
            Status = "当前实例不存在 Mods 目录，无法导出";
            return;
        }

        try
        {
            var exportRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia",
                "Exports");
            Directory.CreateDirectory(exportRoot);

            var prefix = string.IsNullOrWhiteSpace(ExportNamePrefix) ? "SVL-Modpack" : ExportNamePrefix.Trim();
            var fileName = $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}.zip";
            var exportPath = Path.Combine(exportRoot, fileName);

            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            ZipFile.CreateFromDirectory(modsPath, exportPath, CompressionLevel.SmallestSize, true);

            var settings = _settingsStore.Load();
            var metadataPath = Path.ChangeExtension(exportPath, ".metadata.json");
            var metadata = new
            {
                ExportName = fileName,
                ExportTime = DateTimeOffset.Now,
                GamePath = gamePath,
                ModsPath = modsPath,
                InstanceName = settings.InstanceName,
                PreferredLaunchMode = settings.PreferredLaunchMode,
                EnableSafeLaunch = settings.EnableSafeLaunch,
                Source = "SVL.Avalonia"
            };
            File.WriteAllText(metadataPath, System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));

            LastExportPath = exportPath;
            Status = $"导出成功（{DateTime.Now:HH:mm:ss}），元数据: {Path.GetFileName(metadataPath)}";
        }
        catch (Exception ex)
        {
            Status = $"导出失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        var path = LastExportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "暂无可打开的导出文件";
            return;
        }

        var opened = _externalProcessService.TryOpenPath(path);
        Status = opened ? "已打开导出文件" : "打开导出文件失败";
    }
}
