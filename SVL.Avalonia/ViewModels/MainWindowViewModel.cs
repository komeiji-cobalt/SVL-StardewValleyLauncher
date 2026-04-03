using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using SVL.Core.Platform.Services;

namespace SVL.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IPlatformInfoService _platformInfoService;
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly AppUserSettingsStore _settingsStore;
    private readonly LocalizationService _localizationService;
    private readonly ImageResourceService _imageResourceService;
    private readonly Stack<(string Page, ObservableObject ViewModel)> _backStack = new();
    private Models.DownloadTaskItem? _currentDownloadTask;

    public LaunchPageViewModel LaunchPage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public InstancesPageViewModel InstancesPage { get; }

    public TaskStatusPageViewModel TaskStatusPage { get; }

    public ModSearchPageViewModel ModSearchPage { get; }

    public ModpackSearchPageViewModel ModpackSearchPage { get; }

    public ModDetailsPageViewModel ModDetailsPage { get; }

    public VersionSettingsPageViewModel VersionSettingsPage { get; }

    public InstanceSettingsPageViewModel InstanceSettingsPage { get; }

    public ExportPageViewModel ExportPage { get; }

    [ObservableProperty]
    private string _currentPage = "启动";

    [ObservableProperty]
    private ObservableObject? _currentPageViewModel;

    [ObservableProperty]
    private string _windowTitle = "Stardew Valley Launcher";

    [ObservableProperty]
    private string _navLaunchText = "启动";

    [ObservableProperty]
    private string _navDownloadText = "下载";

    [ObservableProperty]
    private string _navTasksText = "任务";

    [ObservableProperty]
    private string _navSettingsText = "设置";

    [ObservableProperty]
    private string _navLaunchIconSource = string.Empty;

    [ObservableProperty]
    private string _navDownloadIconSource = string.Empty;

    [ObservableProperty]
    private string _navTasksIconSource = string.Empty;

    [ObservableProperty]
    private string _navSettingsIconSource = string.Empty;

    [ObservableProperty]
    private string _brandJunimoIconSource = string.Empty;

    [ObservableProperty]
    private bool _showTaskNavNotification;

    [ObservableProperty]
    private bool _showTaskNavSoftHint;

    [ObservableProperty]
    private bool _showDownloadFloatingTaskButton;

    [ObservableProperty]
    private int _floatingTaskBadgeCount;

    [ObservableProperty]
    private string _launcherAppNameText = "SVL";

    [ObservableProperty]
    private string _resourceDetailsHeaderTitle = "资源下载";

    [ObservableProperty]
    private string _sidebarCurrentPageText = "当前页面";

    [ObservableProperty]
    private string _sidebarMigrationStatusText = "迁移状态";

    [ObservableProperty]
    private string _sidebarMigrationPoint1 = "- 保留全部导航结构";

    [ObservableProperty]
    private string _sidebarMigrationPoint2 = "- 保留全部功能域（启动/下载/任务/设置）";

    [ObservableProperty]
    private string _sidebarMigrationPoint3 = "- 正在逐页迁移原 WPF 视图";

    [ObservableProperty]
    private string _sidebarPathDetectText = "路径探测";

    public string PlatformText => _platformInfoService.GetPlatformDisplayName();

    public string SteamPathPreview => _gameInstallPathLocator.TryLocateSteamStardewPath() ?? "未探测到（可手动选择）";

    public string GogPathPreview => _gameInstallPathLocator.TryLocateGogStardewPath() ?? "未探测到（可手动选择）";

    public bool IsLaunchPage => string.Equals(CurrentPage, "启动", StringComparison.Ordinal);

    public bool IsDownloadPage => string.Equals(CurrentPage, "下载", StringComparison.Ordinal);

    public bool IsTasksPage => string.Equals(CurrentPage, "任务", StringComparison.Ordinal);

    public bool IsSettingsPage => string.Equals(CurrentPage, "设置", StringComparison.Ordinal);

    public bool ShowWindowControlButtons => OperatingSystem.IsWindows();

    public bool ShowBackButton => IsBackPage(CurrentPage) && _backStack.Count > 0;

    public bool ShowResourceDetailHeaderTitle => IsResourceDetailsPage;

    public bool ShowBrandIdentity => !ShowBackButton && !ShowResourceDetailHeaderTitle;

    private bool IsResourceDetailsPage => string.Equals(CurrentPage, "资源详情", StringComparison.Ordinal);

    public MainWindowViewModel()
    {
        _platformInfoService = new PlatformInfoService();
        _gameInstallPathLocator = new GameInstallPathLocator();
        var externalProcessService = new ExternalProcessService();

        _settingsStore = new AppUserSettingsStore();
        _localizationService = new LocalizationService(_settingsStore);
        _imageResourceService = new ImageResourceService(_localizationService);
        _localizationService.LanguageChanged += ApplyLocalizedTexts;
        _imageResourceService.ResourcesChanged += ApplyImageResources;
        var initialSettings = _settingsStore.Load();
        LauncherAppNameText = string.IsNullOrWhiteSpace(initialSettings.LauncherAppName) ? "SVL" : initialSettings.LauncherAppName;
        ApplyLocalizedTexts();
        ApplyImageResources();

        var dialogService = new DialogService();
        var nexusAuthService = new NexusAuthService();
        var nexusOAuthService = new NexusOAuthService();
        var httpDownloadService = new HttpDownloadService();
        var nexusModDownloadResolverService = new NexusModDownloadResolverService();
        var downloadInstallService = new DownloadInstallService(_gameInstallPathLocator);
        var smapiInstallService = new SmapiInstallService();
        var downloadTaskStateStore = new DownloadTaskStateStore();
        var retryDiffReportService = new RetryDiffReportService();
        var instanceRegistryStore = new InstanceRegistryStore();
        var nxmLinkParser = new NxmLinkParser();
        var nxmProtocolRegistrationService = new NxmProtocolRegistrationService();
        var remoteCatalogService = new RemoteCatalogService(_settingsStore);
        var launcherUpdateService = new LauncherUpdateService();
        LaunchPage = new LaunchPageViewModel(_gameInstallPathLocator, externalProcessService, _settingsStore, _localizationService, _imageResourceService);
        DownloadPage = new DownloadPageViewModel(
            _localizationService,
            _imageResourceService,
            nxmLinkParser,
            _gameInstallPathLocator,
            _settingsStore,
            dialogService,
            httpDownloadService,
            nexusModDownloadResolverService,
            downloadInstallService,
            smapiInstallService,
            remoteCatalogService,
            downloadTaskStateStore,
            retryDiffReportService);
        SettingsPage = new SettingsPageViewModel(_settingsStore, dialogService, nexusAuthService, nexusOAuthService, launcherUpdateService, externalProcessService, nxmProtocolRegistrationService, _localizationService, _imageResourceService);
        InstancesPage = new InstancesPageViewModel(_gameInstallPathLocator, dialogService, instanceRegistryStore, _settingsStore, _imageResourceService, _localizationService);
        TaskStatusPage = new TaskStatusPageViewModel();
        ModSearchPage = new ModSearchPageViewModel(remoteCatalogService);
        ModpackSearchPage = new ModpackSearchPageViewModel(remoteCatalogService);
        ModDetailsPage = new ModDetailsPageViewModel(remoteCatalogService);
        ModDetailsPage.QueueDownloadRequested += HandleQueueDownload;
        VersionSettingsPage = new VersionSettingsPageViewModel(_settingsStore, _gameInstallPathLocator, _localizationService, _imageResourceService, dialogService);
        InstanceSettingsPage = new InstanceSettingsPageViewModel(_settingsStore);
        ExportPage = new ExportPageViewModel(_gameInstallPathLocator, externalProcessService, _settingsStore);
        LaunchPage.NavigateToInstancesRequested += HandleNavigateToInstances;
        LaunchPage.NavigateToVersionSettingsRequested += HandleNavigateToVersionSettings;
        LaunchPage.NavigateToModManageRequested += HandleNavigateToModManage;
        VersionSettingsPage.NavigateToSmapiDownloadRequested += HandleNavigateToSmapiDownload;
        VersionSettingsPage.InstanceContextChanged += HandleInstanceContextChanged;
        InstancesPage.InstanceActivated += HandleInstanceActivated;
        InstancesPage.InstanceSettingsRequested += HandleInstanceSettingsRequested;
        InstancesPage.ModpackImportRequested += HandleModpackImportRequested;
        DownloadPage.TaskSelected += HandleTaskSelected;
        DownloadPage.TaskStateChanged += HandleTaskStateChanged;
        DownloadPage.TaskLogGenerated += HandleTaskLogGenerated;
        DownloadPage.NavigateToTaskStatusRequested += HandleNavigateToTaskStatus;
        DownloadPage.NavigateToInstancesRequested += HandleNavigateToInstances;
        DownloadPage.NavigateToModSearchRequested += HandleNavigateToModSearch;
        DownloadPage.NavigateToModpackSearchRequested += HandleNavigateToModpackSearch;
        DownloadPage.OpenDetailsRequested += HandleOpenDetails;
        TaskStatusPage.RetryFailedItemsRequested += HandleRetryFailedItemsRequested;
        TaskStatusPage.NavigateToDownloadRequested += HandleNavigateToDownload;
        ModSearchPage.OpenDetailsRequested += HandleOpenDetails;
        ModpackSearchPage.OpenDetailsRequested += HandleOpenDetails;
        SettingsPage.PropertyChanged += HandleSettingsPropertyChanged;

        CurrentPageViewModel = LaunchPage;
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowResourceDetailHeaderTitle));
        OnPropertyChanged(nameof(ShowBrandIdentity));
        RefreshFloatingTaskButtonState();
    }

    private void ApplyLocalizedTexts()
    {
        WindowTitle = _localizationService.Get("Window.Title");
        NavLaunchText = _localizationService.Get("Nav.Launch");
        NavDownloadText = _localizationService.Get("Nav.Download");
        NavTasksText = _localizationService.Get("Nav.Tasks");
        NavSettingsText = _localizationService.Get("Nav.Settings");
        SidebarCurrentPageText = _localizationService.Get("Sidebar.CurrentPage");
        SidebarMigrationStatusText = _localizationService.Get("Sidebar.MigrationStatus");
        SidebarMigrationPoint1 = _localizationService.Get("Sidebar.MigrationPoint1");
        SidebarMigrationPoint2 = _localizationService.Get("Sidebar.MigrationPoint2");
        SidebarMigrationPoint3 = _localizationService.Get("Sidebar.MigrationPoint3");
        SidebarPathDetectText = _localizationService.Get("Sidebar.PathDetect");
    }

    private void ApplyImageResources()
    {
        BrandJunimoIconSource = _imageResourceService.Get("header.brand.junimo");
        NavLaunchIconSource = _imageResourceService.Get("nav.launch");
        NavDownloadIconSource = _imageResourceService.Get("nav.download");
        NavTasksIconSource = _imageResourceService.Get("nav.tasks");
        NavSettingsIconSource = _imageResourceService.Get("nav.settings");
    }

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsLaunchPage));
        OnPropertyChanged(nameof(IsDownloadPage));
        OnPropertyChanged(nameof(IsTasksPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowResourceDetailHeaderTitle));
        OnPropertyChanged(nameof(ShowBrandIdentity));
        RefreshTaskNavNotification();
        RefreshFloatingTaskButtonState();
    }

    private void HandleNavigateToInstances()
    {
        NavigateToPage("实例", InstancesPage, pushCurrentToBackStack: true);
    }

    private void HandleNavigateToVersionSettings()
    {
        VersionSettingsPage.ReloadFromSettings();
        VersionSettingsPage.SwitchToGeneral();
        NavigateToPage("版本设置", VersionSettingsPage, pushCurrentToBackStack: true);
    }

    private void HandleNavigateToModManage()
    {
        VersionSettingsPage.ReloadFromSettings(reloadModsWhenActive: true);
        VersionSettingsPage.SwitchToModManage();
        NavigateToPage("版本设置", VersionSettingsPage, pushCurrentToBackStack: true);
    }

    private void HandleNavigateToSmapiDownload()
    {
        DownloadPage.SelectCategoryCommand.Execute(Models.DownloadCategory.Smapi);
        NavigateToPage("下载", DownloadPage, pushCurrentToBackStack: true);
    }

    private void HandleInstanceContextChanged()
    {
        LaunchPage.RefreshFromSettingsAndEnvironment();
        InstancesPage.RefreshFromSettingsChange();
    }

    private void HandleInstanceActivated(InstanceItem _)
    {
        LaunchPage.RefreshFromSettingsAndEnvironment();
        NavigateToPage("启动", LaunchPage, clearBackStack: true);
    }

    private void HandleInstanceSettingsRequested(InstanceItem _)
    {
        VersionSettingsPage.ReloadFromSettings();
        VersionSettingsPage.SwitchToOverview();
        NavigateToPage("版本设置", VersionSettingsPage, pushCurrentToBackStack: true);
    }

    private void HandleModpackImportRequested()
    {
        DownloadPage.OpenModpackSearchPageCommand.Execute(null);
    }

    private void HandleTaskSelected(Models.DownloadTaskItem task)
    {
        _currentDownloadTask = task;
        TaskStatusPage.SetCurrentTask(task.Name, task.Status);
        TaskStatusPage.SetConflictPreview(task.ConflictPreviewItems);
        TaskStatusPage.SetCanRetryFailedItems(task.CanRetry);
        UpdateTaskStatusOverview();
        RefreshTaskNavNotification();
    }

    private void HandleTaskStateChanged(Models.DownloadTaskItem task)
    {
        _currentDownloadTask = task;
        TaskStatusPage.SetCurrentTask(task.Name, task.Status);
        TaskStatusPage.SetConflictPreview(task.ConflictPreviewItems);
        TaskStatusPage.SetCanRetryFailedItems(task.CanRetry);
        UpdateTaskStatusOverview();
        RefreshTaskNavNotification();
    }

    private void HandleRetryFailedItemsRequested()
    {
        if (_currentDownloadTask == null)
        {
            TaskStatusPage.AddLog("没有可重试的当前任务");
            return;
        }

        DownloadPage.RetryTaskCommand.Execute(_currentDownloadTask);
    }

    private void HandleTaskLogGenerated(string message)
    {
        TaskStatusPage.AddLog(message);

        const string retryPrefix = "重试对比报告: ";
        if (message.StartsWith(retryPrefix, StringComparison.Ordinal))
        {
            var reportPath = message.Substring(retryPrefix.Length).Trim();
            TaskStatusPage.AddRetryReport(reportPath);
        }

        UpdateTaskStatusOverview();

        RefreshTaskNavNotification();
    }

    private void HandleNavigateToTaskStatus()
    {
        UpdateTaskStatusOverview();
        NavigateToPage("任务", TaskStatusPage);
    }

    private void HandleNavigateToDownload()
    {
        NavigateToPage("下载", DownloadPage, clearBackStack: true);
    }

    private void HandleNavigateToModSearch()
    {
        NavigateToPage("Mod搜索", ModSearchPage);
    }

    private void HandleNavigateToModpackSearch()
    {
        NavigateToPage("Modpack搜索", ModpackSearchPage);
    }

    private void HandleOpenDetails(string details)
    {
        ModDetailsPage.SetResource(details, "由下载页搜索结果触发的详情上下文");
        _ = ModDetailsPage.LoadDetailsAsync(details);
        NavigateToPage("资源详情", ModDetailsPage, pushCurrentToBackStack: true);
    }

    private async void HandleQueueDownload(Models.ExternalDownloadRequest request)
    {
        var queued = await DownloadPage.AddTaskFromExternalAsync(request);
        if (!queued)
        {
            return;
        }

        TaskStatusPage.SetCurrentTask(request.ToTaskDisplayName(), "已加入队列");
        NavigateToPage("任务", TaskStatusPage);
    }

    [RelayCommand]
    private void NavigateToLaunch()
    {
        LaunchPage.RefreshFromSettingsAndEnvironment();
        NavigateToPage("启动", LaunchPage, clearBackStack: true);
    }

    [RelayCommand]
    private void NavigateToDownload()
    {
        NavigateToPage("下载", DownloadPage, clearBackStack: true);
    }

    [RelayCommand]
    private void NavigateToTasks()
    {
        NavigateToPage("任务", TaskStatusPage, clearBackStack: true);
    }

    [RelayCommand]
    private void OpenFloatingTaskManager()
    {
        NavigateToTasks();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateToPage("设置", SettingsPage, clearBackStack: true);
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (_backStack.Count <= 0)
        {
            return;
        }

        var previous = _backStack.Pop();
        NavigateToPage(previous.Page, previous.ViewModel);
    }

    private void NavigateToPage(string page, ObservableObject viewModel, bool pushCurrentToBackStack = false, bool clearBackStack = false)
    {
        if (clearBackStack)
        {
            _backStack.Clear();
        }

        if (pushCurrentToBackStack && CurrentPageViewModel != null)
        {
            _backStack.Push((CurrentPage, CurrentPageViewModel));
        }

        CurrentPage = page;
        CurrentPageViewModel = viewModel;
        RefreshTaskNavNotification();
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowBrandIdentity));
    }

    private static bool IsBackPage(string page)
    {
        return string.Equals(page, "实例", StringComparison.Ordinal) ||
               string.Equals(page, "版本设置", StringComparison.Ordinal);
    }

    private void RefreshTaskNavNotification()
    {
        var hasFailedTasks = DownloadPage.DownloadTasks.Any(task => task.IsFailed || task.CanRetry);
        var hasRunningTasks = DownloadPage.DownloadTasks.Any(task => task.IsRunning);

        ShowTaskNavNotification = !IsTasksPage && hasFailedTasks;
        ShowTaskNavSoftHint = !IsTasksPage && !hasFailedTasks && hasRunningTasks;
        UpdateTaskStatusOverview();
    }

    private void UpdateTaskStatusOverview()
    {
        TaskStatusPage.UpdateTaskOverview(
            DownloadPage.ActiveTasks.Count,
            DownloadPage.FinishedTasks.Count,
            DownloadPage.SelectedTaskHint);
        RefreshFloatingTaskButtonState();
    }

    private void RefreshFloatingTaskButtonState()
    {
        var totalTasks = DownloadPage.ActiveTasks.Count + DownloadPage.FinishedTasks.Count;
        FloatingTaskBadgeCount = totalTasks;

        var enabled = SettingsPage.EnableDownloadFloatingTaskButton;
        ShowDownloadFloatingTaskButton = enabled && totalTasks > 0 && !IsTasksPage;
    }

    private void HandleSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SettingsPageViewModel.LauncherAppName), StringComparison.Ordinal))
        {
            LauncherAppNameText = string.IsNullOrWhiteSpace(SettingsPage.LauncherAppName)
                ? "SVL"
                : SettingsPage.LauncherAppName;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(SettingsPageViewModel.EnableDownloadFloatingTaskButton), StringComparison.Ordinal))
        {
            RefreshFloatingTaskButtonState();
        }
    }
}
