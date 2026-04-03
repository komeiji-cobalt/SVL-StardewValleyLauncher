using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SVL.Avalonia.ViewModels;

public partial class DownloadPageViewModel : ObservableObject
{
    private const string SmapiDefaultName = "SMAPI - Stardew Modding API";
    private const string SmapiDefaultSummary = "适用于星露谷物语的Mod加载器";
    private const string DescriptionModeLocalized = "社区汉化";
    private const string DescriptionModeSource = "源站英文";
    private static readonly object IconHttpClientLock = new();
    private static HttpClient? _smapiIconHttpClient;
    private static string _smapiIconProxySignature = string.Empty;
    private const int ModPageSize = 10;
    private const int ModpackPageSize = 10;
    private static readonly TimeSpan ModSearchCacheTtl = TimeSpan.FromMinutes(5);

    private readonly LocalizationService _localizationService;
    private readonly ImageResourceService _imageResourceService;
    private readonly INxmLinkParser _nxmLinkParser;
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly AppUserSettingsStore _settingsStore;
    private readonly HttpDownloadService _httpDownloadService;
    private readonly NexusModDownloadResolverService _nexusModDownloadResolverService;
    private readonly DownloadInstallService _downloadInstallService;
    private readonly SmapiInstallService _smapiInstallService;
    private readonly RemoteCatalogService _remoteCatalogService;
    private readonly DownloadTaskStateStore _taskStateStore;
    private readonly RetryDiffReportService _retryDiffReportService;
    private readonly DialogService _dialogService;
    private readonly string _downloadRootPath;
    private readonly string _taskStatePath;
    private readonly string _smapiIconCachePath;
    private readonly Dictionary<string, string> _smapiIconDiskCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DownloadTaskItem, CancellationTokenSource> _runningTaskCancellationSources = [];
    private readonly SemaphoreSlim _queueWorkerGate = new(1, 1);
    private bool _isQueueWorkerRunning;
    private int _catalogLoadToken;
    private bool _forceHotModsLoad;
    private readonly List<string> _modAllResults = [];
    private readonly List<string> _modpackAllResults = [];
    private bool _modHasMore;
    private bool _modpackHasMore;
    private readonly Dictionary<string, (DateTime CreatedAt, List<string> Results)> _modResultsCache = new(StringComparer.Ordinal);

    public event Action<DownloadTaskItem>? TaskSelected;

    public event Action<DownloadTaskItem>? TaskStateChanged;

    public event Action<string>? TaskLogGenerated;

    public event Action? NavigateToTaskStatusRequested;

    public event Action? NavigateToModSearchRequested;

    public event Action? NavigateToModpackSearchRequested;

    public event Action? NavigateToInstancesRequested;

    public event Action<string>? OpenDetailsRequested;

    [ObservableProperty]
    private DownloadCategory _selectedCategory = DownloadCategory.Smapi;

    [ObservableProperty]
    private string _title = "下载中心";

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private string _smapiSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedSmapiSource = "全部";

    [ObservableProperty]
    private string _selectedModSource = "全部";

    [ObservableProperty]
    private string _selectedModGameVersion = "全部";

    [ObservableProperty]
    private string _selectedModType = "全部";

    [ObservableProperty]
    private string _selectedModDescriptionMode = DescriptionModeLocalized;

    [ObservableProperty]
    private string _modSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedTaskHint = "未选择任务";

    [ObservableProperty]
    private bool _showGamePathWarning = true;

    [ObservableProperty]
    private string _nxmLinkInput = string.Empty;

    [ObservableProperty]
    private string _nxmImportStatus = "可粘贴 NXM 链接（nxm://...）快速入队";

    [ObservableProperty]
    private string _downloadUrlInput = string.Empty;

    [ObservableProperty]
    private string _downloadFileNameInput = string.Empty;

    [ObservableProperty]
    private string _urlDownloadStatus = "可输入 HTTP/HTTPS 直链进行真实下载";

    [ObservableProperty]
    private string _modpackUrlInput = string.Empty;

    [ObservableProperty]
    private string _modpackFileNameInput = string.Empty;

    [ObservableProperty]
    private string _modpackImportStatus = "支持 URL 导入 Modpack 并进入真实下载队列";

    [ObservableProperty]
    private string _gamePathHint = "未探测到游戏目录";

    [ObservableProperty]
    private bool _hasNoTasks;

    [ObservableProperty]
    private string _downloadCategoryTitleText = "下载类别";

    [ObservableProperty]
    private string _categorySmapiText = "SMAPI";

    [ObservableProperty]
    private string _categorySmapiSubText = "模组启动器";

    [ObservableProperty]
    private string _categoryModsText = "Mod";

    [ObservableProperty]
    private string _categoryModsSubText = "单个模组";

    [ObservableProperty]
    private string _categoryModpacksText = "Modpack";

    [ObservableProperty]
    private string _categoryModpacksSubText = "整合包";

    [ObservableProperty]
    private string _activeTasksTitleText = "进行中的任务";

    [ObservableProperty]
    private string _noActiveTasksText = "当前无进行中任务";

    [ObservableProperty]
    private string _historyTasksTitleText = "历史任务";

    [ObservableProperty]
    private string _noHistoryTasksText = "当前无历史任务";

    [ObservableProperty]
    private string _taskCancelButtonText = "取消";

    [ObservableProperty]
    private string _taskRetryButtonText = "重试";

    [ObservableProperty]
    private string _taskOpenReportButtonText = "打开报告";

    [ObservableProperty]
    private string _taskOpenBackupButtonText = "打开备份";

    [ObservableProperty]
    private string _taskCopyFailedButtonText = "复制失败明细";

    [ObservableProperty]
    private string _taskOpenRetryReportButtonText = "打开重试报告";

    [ObservableProperty]
    private string _statusPrefixText = "状态: ";

    [ObservableProperty]
    private string _nxmCardTitleText = "NXM 链接导入";

    [ObservableProperty]
    private string _nxmInputWatermarkText = "粘贴 nxm://stardewvalley/mods/.../files/...";

    [ObservableProperty]
    private string _nxmImportButtonText = "导入 NXM";

    [ObservableProperty]
    private string _urlCardTitleText = "URL 直链下载（真实网络）";

    [ObservableProperty]
    private string _urlInputWatermarkText = "https://example.com/file.zip";

    [ObservableProperty]
    private string _urlFileNameWatermarkText = "可选：自定义文件名（不填则自动推断）";

    [ObservableProperty]
    private string _urlQueueButtonText = "加入真实下载队列";

    [ObservableProperty]
    private string _gamePathWarningTitleText = "未设置游戏安装目录";

    [ObservableProperty]
    private string _gamePathWarningDescriptionText = "当前需要先配置实例中的游戏目录，下载与安装流程会使用该目录。";

    [ObservableProperty]
    private string _gamePathHintPrefixText = "探测结果: ";

    [ObservableProperty]
    private string _goInstanceButtonText = "前往实例管理";

    [ObservableProperty]
    private string _smapiSearchTitleText = "搜索 SMAPI";

    [ObservableProperty]
    private string _smapiSearchWatermarkText = "输入关键词";

    [ObservableProperty]
    private string _searchButtonText = "搜索";

    [ObservableProperty]
    private string _selectFirstResultButtonText = "选择首条结果";

    [ObservableProperty]
    private string _modSearchTitleText = "搜索 Mod";

    [ObservableProperty]
    private string _modSearchWatermarkText = "输入 Mod 关键词";

    [ObservableProperty]
    private string _openModSearchButtonText = "进入 Mod 搜索页";

    [ObservableProperty]
    private string _modpackImportTitleText = "Modpack 导入";

    [ObservableProperty]
    private string _modpackImportDescriptionText = "可通过搜索页选择整合包，或直接输入 URL 进入真实下载队列。";

    [ObservableProperty]
    private string _modpackUrlWatermarkText = "https://example.com/modpack.zip";

    [ObservableProperty]
    private string _modpackFileNameWatermarkText = "可选：自定义文件名（不填则自动推断）";

    [ObservableProperty]
    private string _modpackImportButtonText = "导入 Modpack URL";

    [ObservableProperty]
    private string _openModpackSearchButtonText = "进入 Modpack 搜索页";

    [ObservableProperty]
    private string _categorySmapiIconSource = "avares://SVL.Avalonia/Assets/Icons/Modded.png";

    [ObservableProperty]
    private string _categoryModsIconSource = "avares://SVL.Avalonia/Assets/Icons/Junimo.png";

    [ObservableProperty]
    private string _categoryModpacksIconSource = "avares://SVL.Avalonia/Assets/Icons/icon.png";

    [ObservableProperty]
    private string _modpackSearchText = string.Empty;

    [ObservableProperty]
    private bool _isCatalogLoading;

    [ObservableProperty]
    private string _catalogListTitleText = "资源列表";

    [ObservableProperty]
    private string _catalogNoItemsText = "暂无资源，可尝试搜索关键词";

    [ObservableProperty]
    private int _currentModPage = 1;

    [ObservableProperty]
    private int _totalModPages = 1;

    [ObservableProperty]
    private int _currentModpackPage = 1;

    [ObservableProperty]
    private int _totalModpackPages = 1;

    public ObservableCollection<string> SmapiSources { get; } = ["全部", "GitHub", "NexusMods", "Curseforge"];

    public ObservableCollection<string> ModSources { get; } = ["全部", "Curseforge", "NexusMods"];

    public ObservableCollection<string> ModGameVersions { get; } = ["全部", "1.6", "1.5", "1.4"];

    public ObservableCollection<string> ModTypes { get; } = ["全部", "功能扩展", "界面美化", "游戏内容", "工具类", "音效材质", "作弊类"];

    public ObservableCollection<string> ModDescriptionModes { get; } = [DescriptionModeLocalized, DescriptionModeSource];

    public ObservableCollection<DownloadTaskItem> DownloadTasks { get; } = [];

    public ObservableCollection<DownloadTaskItem> ActiveTasks { get; } = [];

    public ObservableCollection<DownloadTaskItem> FinishedTasks { get; } = [];

    public ObservableCollection<string> SearchResults { get; } = [];

    public ObservableCollection<DownloadCatalogItem> CategoryItems { get; } = [];

    public ObservableCollection<DownloadCatalogItem> SmapiGithubItems { get; } = [];

    public ObservableCollection<DownloadCatalogItem> SmapiNexusModsItems { get; } = [];

    public ObservableCollection<DownloadCatalogItem> SmapiCurseforgeItems { get; } = [];

    public bool IsSmapiCategory => SelectedCategory == DownloadCategory.Smapi;

    public bool IsModsCategory => SelectedCategory == DownloadCategory.Mods;

    public bool IsModpacksCategory => SelectedCategory == DownloadCategory.Modpacks;

    public bool IsNonSmapiCategory => !IsSmapiCategory;

    public bool HasActiveTasks => ActiveTasks.Count > 0;

    public bool HasFinishedTasks => FinishedTasks.Count > 0;

    public bool HasNoActiveTasks => !HasActiveTasks;

    public bool HasNoFinishedTasks => !HasFinishedTasks;

    public bool HasNoCategoryItems => !IsCatalogLoading && CategoryItems.Count == 0;

    public bool HasCategoryItems => !HasNoCategoryItems;

    public bool HasSmapiGithubItems => SmapiGithubItems.Count > 0;

    public bool HasSmapiNexusModsItems => SmapiNexusModsItems.Count > 0;

    public bool HasSmapiCurseforgeItems => SmapiCurseforgeItems.Count > 0;

    public bool HasNoSmapiItems =>
        !IsCatalogLoading &&
        !HasSmapiGithubItems &&
        !HasSmapiNexusModsItems &&
        !HasSmapiCurseforgeItems;

    public bool UseLocalizedModDescription =>
        string.Equals(SelectedModDescriptionMode, DescriptionModeLocalized, StringComparison.Ordinal);

    public bool IsModsPageable => IsModsCategory;

    public bool CanGoToPreviousModPage => CurrentModPage > 1;

    public bool CanGoToNextModPage => CurrentModPage < TotalModPages;

    public string ModPageInfoText => $"第 {CurrentModPage}/{TotalModPages} 页";

    public bool IsModpacksPageable => IsModpacksCategory;

    public bool CanGoToPreviousModpackPage => CurrentModpackPage > 1;

    public bool CanGoToNextModpackPage => CurrentModpackPage < TotalModpackPages;

    public string ModpackPageInfoText => $"第 {CurrentModpackPage}/{TotalModpackPages} 页";

    public string SelectedModSourceDescription => SelectedModSource switch
    {
        "NexusMods" => "来源：仅 NexusMods",
        "Curseforge" => "来源：仅 Curseforge",
        _ => "来源：全部（每个源最多展示 10 条）"
    };

    public string SelectedModGameVersionDescription => SelectedModGameVersion switch
    {
        "全部" => "版本：不过滤",
        _ => $"版本：兼容 {SelectedModGameVersion}"
    };

    public string SelectedModTypeDescription => SelectedModType switch
    {
        "全部" => "类型：不过滤",
        _ => $"类型：{SelectedModType}"
    };

    public DownloadPageViewModel(
        LocalizationService localizationService,
        ImageResourceService imageResourceService,
        INxmLinkParser nxmLinkParser,
        IGameInstallPathLocator gameInstallPathLocator,
        AppUserSettingsStore settingsStore,
        DialogService dialogService,
        HttpDownloadService httpDownloadService,
        NexusModDownloadResolverService nexusModDownloadResolverService,
        DownloadInstallService downloadInstallService,
        SmapiInstallService smapiInstallService,
        RemoteCatalogService remoteCatalogService,
        DownloadTaskStateStore taskStateStore,
        RetryDiffReportService retryDiffReportService)
    {
        _localizationService = localizationService;
        _imageResourceService = imageResourceService;
        _nxmLinkParser = nxmLinkParser;
        _gameInstallPathLocator = gameInstallPathLocator;
        _settingsStore = settingsStore;
        _dialogService = dialogService;
        _httpDownloadService = httpDownloadService;
        _nexusModDownloadResolverService = nexusModDownloadResolverService;
        _downloadInstallService = downloadInstallService;
        _smapiInstallService = smapiInstallService;
        _remoteCatalogService = remoteCatalogService;
        _remoteCatalogService.DebugLogger = message => EmitLog($"[Catalog] {message}");
        _taskStateStore = taskStateStore;
        _retryDiffReportService = retryDiffReportService;
        _localizationService.LanguageChanged += ApplyLocalizedTexts;
        _imageResourceService.ResourcesChanged += ApplyImageResources;
        ApplyLocalizedTexts();
        ApplyImageResources();

        _downloadRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SVL",
            "Avalonia",
            "Downloads");
        _taskStatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SVL",
            "Avalonia",
            "download-tasks-state.json");
        _smapiIconCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SVL",
            "Avalonia",
            "smapi-icon-cache");

        DownloadTasks.CollectionChanged += (_, args) =>
        {
            HasNoTasks = DownloadTasks.Count == 0;
            if (args.OldItems != null)
            {
                foreach (var oldItem in args.OldItems.OfType<DownloadTaskItem>())
                {
                    oldItem.PropertyChanged -= OnTaskPropertyChanged;
                }
            }

            if (args.NewItems != null)
            {
                foreach (var newItem in args.NewItems.OfType<DownloadTaskItem>())
                {
                    newItem.PropertyChanged += OnTaskPropertyChanged;
                }
            }

            RefreshTaskBuckets();
        };

        CategoryItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoCategoryItems));
            OnPropertyChanged(nameof(HasCategoryItems));
        };

        SmapiGithubItems.CollectionChanged += (_, _) => RaiseSmapiSourceState();
        SmapiNexusModsItems.CollectionChanged += (_, _) => RaiseSmapiSourceState();
        SmapiCurseforgeItems.CollectionChanged += (_, _) => RaiseSmapiSourceState();

        Directory.CreateDirectory(_downloadRootPath);
        Directory.CreateDirectory(_smapiIconCachePath);
        TryLoadTaskState();
        RefreshGamePathState();

        HasNoTasks = DownloadTasks.Count == 0;
        foreach (var task in DownloadTasks)
        {
            task.PropertyChanged += OnTaskPropertyChanged;
        }

        RefreshTaskBuckets();
        RefreshTaskStatusIcons();
        if (HasNoTasks)
        {
            Status = "暂无下载任务，可通过搜索或链接导入添加";
        }

        _ = LoadCategoryItemsForCurrentCategoryAsync(initialLoad: true);
    }

    private void ApplyImageResources()
    {
        CategorySmapiIconSource = _imageResourceService.Get("download.category.smapi");
        CategoryModsIconSource = _imageResourceService.Get("download.category.mods");
        CategoryModpacksIconSource = _imageResourceService.Get("download.category.modpacks");
        RefreshTaskStatusIcons();
    }

    private void RefreshTaskStatusIcons()
    {
        foreach (var task in DownloadTasks)
        {
            task.StatusIconSource = ResolveTaskStatusIcon(task);
        }
    }

    private string ResolveTaskStatusIcon(DownloadTaskItem task)
    {
        if (task.IsFailed || task.Status.Contains("取消", StringComparison.Ordinal))
        {
            return _imageResourceService.Get("download.task.failed");
        }

        if (task.Status.Contains("完成", StringComparison.Ordinal))
        {
            return _imageResourceService.Get("download.task.completed");
        }

        if (task.IsRunning)
        {
            return _imageResourceService.Get("download.task.running");
        }

        return _imageResourceService.Get("download.task.pending");
    }

    private void ApplyLocalizedTexts()
    {
        DownloadCategoryTitleText = _localizationService.Get("Download.CategoryTitle");
        CategorySmapiText = _localizationService.Get("Download.Category.Smapi");
        CategorySmapiSubText = _localizationService.Get("Download.Category.SmapiSub");
        CategoryModsText = _localizationService.Get("Download.Category.Mods");
        CategoryModsSubText = _localizationService.Get("Download.Category.ModsSub");
        CategoryModpacksText = _localizationService.Get("Download.Category.Modpacks");
        CategoryModpacksSubText = _localizationService.Get("Download.Category.ModpacksSub");
        ActiveTasksTitleText = _localizationService.Get("Download.ActiveTasks");
        NoActiveTasksText = _localizationService.Get("Download.NoActiveTasks");
        HistoryTasksTitleText = _localizationService.Get("Download.HistoryTasks");
        NoHistoryTasksText = _localizationService.Get("Download.NoHistoryTasks");
        TaskCancelButtonText = _localizationService.Get("Download.Task.Cancel");
        TaskRetryButtonText = _localizationService.Get("Download.Task.Retry");
        TaskOpenReportButtonText = _localizationService.Get("Download.Task.OpenReport");
        TaskOpenBackupButtonText = _localizationService.Get("Download.Task.OpenBackup");
        TaskCopyFailedButtonText = _localizationService.Get("Download.Task.CopyFailed");
        TaskOpenRetryReportButtonText = _localizationService.Get("Download.Task.OpenRetryReport");
        StatusPrefixText = _localizationService.Get("Download.StatusPrefix");
        NxmCardTitleText = _localizationService.Get("Download.Nxm.Title");
        NxmInputWatermarkText = _localizationService.Get("Download.Nxm.Watermark");
        NxmImportButtonText = _localizationService.Get("Download.Nxm.Import");
        UrlCardTitleText = _localizationService.Get("Download.Url.Title");
        UrlInputWatermarkText = _localizationService.Get("Download.Url.Watermark");
        UrlFileNameWatermarkText = _localizationService.Get("Download.Url.FileNameWatermark");
        UrlQueueButtonText = _localizationService.Get("Download.Url.Queue");
        GamePathWarningTitleText = _localizationService.Get("Download.Path.WarningTitle");
        GamePathWarningDescriptionText = _localizationService.Get("Download.Path.WarningDescription");
        GamePathHintPrefixText = _localizationService.Get("Download.Path.HintPrefix");
        GoInstanceButtonText = _localizationService.Get("Download.Path.GoInstance");
        SmapiSearchTitleText = _localizationService.Get("Download.Search.SmapiTitle");
        SmapiSearchWatermarkText = _localizationService.Get("Download.Search.SmapiWatermark");
        SearchButtonText = _localizationService.Get("Download.Search.Button");
        SelectFirstResultButtonText = _localizationService.Get("Download.Search.SelectFirst");
        ModSearchTitleText = _localizationService.Get("Download.Search.ModTitle");
        ModSearchWatermarkText = _localizationService.Get("Download.Search.ModWatermark");
        OpenModSearchButtonText = _localizationService.Get("Download.Search.OpenMod");
        ModpackImportTitleText = _localizationService.Get("Download.Modpack.Title");
        ModpackImportDescriptionText = _localizationService.Get("Download.Modpack.Description");
        ModpackUrlWatermarkText = _localizationService.Get("Download.Modpack.UrlWatermark");
        ModpackFileNameWatermarkText = _localizationService.Get("Download.Modpack.FileNameWatermark");
        ModpackImportButtonText = _localizationService.Get("Download.Modpack.Import");
        OpenModpackSearchButtonText = _localizationService.Get("Download.Modpack.OpenSearch");
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadTaskItem.Status) ||
            e.PropertyName == nameof(DownloadTaskItem.Progress) ||
            e.PropertyName == nameof(DownloadTaskItem.CanRetry) ||
            e.PropertyName == nameof(DownloadTaskItem.CanCancel))
        {
            if (sender is DownloadTaskItem task)
            {
                task.StatusIconSource = ResolveTaskStatusIcon(task);
            }

            RefreshTaskBuckets();
        }
    }

    private void RefreshTaskBuckets()
    {
        var active = DownloadTasks.Where(task => !task.IsFinished).ToList();
        var finished = DownloadTasks.Where(task => task.IsFinished).ToList();

        ActiveTasks.Clear();
        foreach (var task in active)
        {
            ActiveTasks.Add(task);
        }

        FinishedTasks.Clear();
        foreach (var task in finished)
        {
            FinishedTasks.Add(task);
        }

        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(HasFinishedTasks));
        OnPropertyChanged(nameof(HasNoActiveTasks));
        OnPropertyChanged(nameof(HasNoFinishedTasks));
    }

    partial void OnSelectedCategoryChanged(DownloadCategory value)
    {
        Title = value switch
        {
            DownloadCategory.Smapi => "SMAPI 下载",
            DownloadCategory.Mods => "Mod 下载",
            DownloadCategory.Modpacks => "Modpack 下载",
            _ => "下载中心"
        };

        Status = value switch
        {
            DownloadCategory.Smapi => "可搜索并安装 SMAPI",
            DownloadCategory.Mods => "可搜索并安装 Mod",
            DownloadCategory.Modpacks => "可导入或下载 Modpack",
            _ => "就绪"
        };

        CatalogListTitleText = value switch
        {
            DownloadCategory.Smapi => "SMAPI 资源列表",
            DownloadCategory.Mods => "Mod 资源列表",
            DownloadCategory.Modpacks => "Modpack 资源列表",
            _ => "资源列表"
        };

        _ = LoadCategoryItemsForCurrentCategoryAsync(initialLoad: true);

        OnPropertyChanged(nameof(IsSmapiCategory));
        OnPropertyChanged(nameof(IsModsCategory));
        OnPropertyChanged(nameof(IsModpacksCategory));
        OnPropertyChanged(nameof(IsNonSmapiCategory));
        OnPropertyChanged(nameof(IsModsPageable));
        OnPropertyChanged(nameof(IsModpacksPageable));
        OnPropertyChanged(nameof(CanGoToPreviousModPage));
        OnPropertyChanged(nameof(CanGoToNextModPage));
        OnPropertyChanged(nameof(CanGoToPreviousModpackPage));
        OnPropertyChanged(nameof(CanGoToNextModpackPage));
        OnPropertyChanged(nameof(ModPageInfoText));
        OnPropertyChanged(nameof(ModpackPageInfoText));
        RaiseSmapiSourceState();
    }

    partial void OnSelectedModDescriptionModeChanged(string value)
    {
        OnPropertyChanged(nameof(UseLocalizedModDescription));
        ApplyModLocalizationPreferenceToCategoryItems();

        if (IsModsCategory)
        {
            _ = LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
        }
    }

    partial void OnSelectedModSourceChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedModSourceDescription));
    }

    partial void OnSelectedModGameVersionChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedModGameVersionDescription));
    }

    partial void OnSelectedModTypeChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedModTypeDescription));
    }

    partial void OnCurrentModPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoToPreviousModPage));
        OnPropertyChanged(nameof(CanGoToNextModPage));
        OnPropertyChanged(nameof(ModPageInfoText));
    }

    partial void OnTotalModPagesChanged(int value)
    {
        OnPropertyChanged(nameof(IsModsPageable));
        OnPropertyChanged(nameof(CanGoToPreviousModPage));
        OnPropertyChanged(nameof(CanGoToNextModPage));
        OnPropertyChanged(nameof(ModPageInfoText));
    }

    partial void OnCurrentModpackPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoToPreviousModpackPage));
        OnPropertyChanged(nameof(CanGoToNextModpackPage));
        OnPropertyChanged(nameof(ModpackPageInfoText));
    }

    partial void OnTotalModpackPagesChanged(int value)
    {
        OnPropertyChanged(nameof(IsModpacksPageable));
        OnPropertyChanged(nameof(CanGoToPreviousModpackPage));
        OnPropertyChanged(nameof(CanGoToNextModpackPage));
        OnPropertyChanged(nameof(ModpackPageInfoText));
    }

    [RelayCommand]
    private void ShowLocalizedName(DownloadCatalogItem? item)
    {
        if (item == null)
        {
            return;
        }

        item.UseLocalizedName = true;
    }

    [RelayCommand]
    private void ShowSourceName(DownloadCatalogItem? item)
    {
        if (item == null)
        {
            return;
        }

        item.UseLocalizedName = false;
    }

    [RelayCommand]
    private void ShowLocalizedSummary(DownloadCatalogItem? item)
    {
        if (item == null)
        {
            return;
        }

        item.UseLocalizedSummary = true;
    }

    [RelayCommand]
    private void ShowSourceSummary(DownloadCatalogItem? item)
    {
        if (item == null)
        {
            return;
        }

        item.UseLocalizedSummary = false;
    }

    [RelayCommand]
    private void ToggleLocalizedDisplay(DownloadCatalogItem? item)
    {
        if (item == null)
        {
            return;
        }

        var useLocalized = !(item.UseLocalizedName && item.UseLocalizedSummary);
        item.UseLocalizedName = useLocalized;
        item.UseLocalizedSummary = useLocalized;
    }

    [RelayCommand]
    private void SelectCategory(DownloadCategory category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task SearchSmapi()
    {
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task SearchMods()
    {
        CurrentModPage = 1;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task SearchModpacks()
    {
        CurrentModpackPage = 1;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task LoadHotModsAsync()
    {
        ModSearchText = string.Empty;
        CurrentModPage = 1;
        _forceHotModsLoad = true;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task ResetModFiltersAsync()
    {
        ModSearchText = string.Empty;
        SelectedModSource = "全部";
        SelectedModGameVersion = "全部";
        SelectedModType = "全部";
        SelectedModDescriptionMode = DescriptionModeLocalized;
        CurrentModPage = 1;
        _forceHotModsLoad = true;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task GoToPreviousModPage()
    {
        if (!CanGoToPreviousModPage)
        {
            return;
        }

        CurrentModPage--;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task GoToNextModPage()
    {
        if (!CanGoToNextModPage)
        {
            return;
        }

        CurrentModPage++;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task GoToPreviousModpackPage()
    {
        if (!CanGoToPreviousModpackPage)
        {
            return;
        }

        CurrentModpackPage--;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task GoToNextModpackPage()
    {
        if (!CanGoToNextModpackPage)
        {
            return;
        }

        CurrentModpackPage++;
        await LoadCategoryItemsForCurrentCategoryAsync(initialLoad: false);
    }

    [RelayCommand]
    private async Task OpenCatalogItemDetails(DownloadCatalogItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.DisplayText))
        {
            return;
        }

        await PromoteCatalogItemIconToFullAsync(item);

        OpenDetailsRequested?.Invoke(item.DisplayText);
    }

    private async Task PromoteCatalogItemIconToFullAsync(DownloadCatalogItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FullIconSource))
        {
            return;
        }

        if (string.Equals(item.IconSource, item.FullIconSource, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        item.IconSource = item.FullIconSource;
        await ResolveRemoteIconToLocalAsync(item, Volatile.Read(ref _catalogLoadToken), ResolveCategoryFallbackIcon(item.SourceKey));
    }

    [RelayCommand]
    private void OpenSearchResultDetails(string? item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        OpenDetailsRequested?.Invoke(item);
    }

    [RelayCommand]
    private async Task ToggleCatalogItemExpandedAsync(DownloadCatalogItem? item)
    {
        if (item == null)
        {
            return;
        }

        item.IsExpanded = !item.IsExpanded;
        if (!item.IsExpanded || item.HasLoadedDetails || item.IsLoadingDetails)
        {
            return;
        }

        item.IsLoadingDetails = true;
        try
        {
            var details = await _remoteCatalogService.GetResourceDetailsAsync(item.DisplayText);
            if (!string.IsNullOrWhiteSpace(details.Source))
            {
                item.SourceTag = details.Source;
            }

            if (!string.IsNullOrWhiteSpace(details.Summary))
            {
                item.Summary = details.Summary;
            }

            ReplaceStringCollection(item.VersionOptions, details.VersionOptions);
            ReplaceStringCollection(item.DependencyOptions, details.Dependencies);
            ReplaceStringCollection(item.DownloadOptions, details.DownloadOptions);
            item.HasLoadedDetails = true;
        }
        catch (Exception ex)
        {
            item.Summary = $"加载详情失败: {ex.Message}";
        }
        finally
        {
            item.IsLoadingDetails = false;
        }
    }

    [RelayCommand]
    private void SelectTask(DownloadTaskItem? task)
    {
        if (task == null)
        {
            return;
        }

        SelectedTaskHint = $"已选择任务: {task.Name} ({task.Status})";
        TaskSelected?.Invoke(task);
        NavigateToTaskStatusRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SelectGamePath()
    {
        RefreshGamePathState();
        if (ShowGamePathWarning)
        {
            var configured = await EnsureGamePathConfiguredAsync();
            if (configured)
            {
                Status = $"已配置游戏目录: {GamePathHint}";
                return;
            }

            Status = "未探测到游戏目录，已跳转到实例页进行配置";
            NavigateToInstancesRequested?.Invoke();
            return;
        }

        Status = $"已探测到游戏目录: {GamePathHint}";
    }

    [RelayCommand]
    private async Task QueueModpackUrlDownload()
    {
        if (!await EnsureGamePathConfiguredAsync())
        {
            ModpackImportStatus = "请先配置有效的游戏目录";
            Status = "入队失败：未配置游戏目录";
            return;
        }

        var rawUrl = ModpackUrlInput?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ModpackImportStatus = "Modpack 地址无效，请输入 HTTP/HTTPS 链接";
            Status = "入队失败：Modpack URL 无效";
            return;
        }

        var fileName = ResolveDownloadFileName(uri, ModpackFileNameInput);
        var targetFilePath = Path.Combine(_downloadRootPath, fileName);

        DownloadTasks.Insert(0, new DownloadTaskItem
        {
            Name = fileName,
            Status = "已加入队列（Modpack URL）",
            Progress = 0,
            TaskKind = DownloadTaskKind.Generic,
            SourceUrl = uri.ToString(),
            OutputFilePath = targetFilePath,
            CanCancel = false,
            CanRetry = false
        });
        DownloadTasks[0].StatusIconSource = ResolveTaskStatusIcon(DownloadTasks[0]);

        ModpackImportStatus = $"已入队：{fileName}";
        Status = "Modpack 下载任务已加入队列";
        EmitLog($"Modpack URL 入队: {fileName} -> {targetFilePath}");
        SaveTaskState();
        _ = ProcessQueueAsync();
        NavigateToTaskStatusRequested?.Invoke();
    }

    [RelayCommand]
    private void SelectFirstSearchResult()
    {
        if (!SearchResults.Any())
        {
            Status = "暂无可选搜索结果";
            return;
        }

        var selected = SearchResults[0];
        Status = $"已选择: {selected}";
        OpenDetailsRequested?.Invoke(selected);
    }

    [RelayCommand]
    private void OpenModSearchPage()
    {
        NavigateToModSearchRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenModpackSearchPage()
    {
        NavigateToModpackSearchRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ImportNxmLinkAsync()
    {
        if (!await EnsureGamePathConfiguredAsync())
        {
            NxmImportStatus = "请先配置有效的游戏目录";
            Status = "导入失败：未配置游戏目录";
            return;
        }

        var settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.NexusApiKey) && string.IsNullOrWhiteSpace(settings.NexusOAuthAccessToken))
        {
            NxmImportStatus = "请先在设置页完成 Nexus 登录，再导入 NXM 链接";
            Status = "导入失败：Nexus 未登录";
            return;
        }

        if (!_nxmLinkParser.TryParse(NxmLinkInput, out var parsed, out var errorMessage))
        {
            NxmImportStatus = errorMessage;
            Status = "导入失败：链接格式不正确";
            return;
        }

        var taskName = parsed.ResourceType == NxmResourceType.Collection
            ? $"Nexus Collection {parsed.CollectionSlug} Rev {(parsed.RevisionNumber < 0 ? "latest" : parsed.RevisionNumber.ToString())}"
            : $"Nexus Mod {parsed.ModId} File {parsed.FileId}";

        var taskStatus = parsed.ResourceType == NxmResourceType.Collection
            ? "已加入队列（NXM Collection）"
            : "已加入队列（NXM Mod）";

        string sourceUrl = string.Empty;
        string outputFilePath = string.Empty;
        List<string> dependencyUrls = [];

        if (parsed.ResourceType == NxmResourceType.ModFile)
        {
            NxmImportStatus = "正在通过 Nexus API 解析真实下载地址...";
            var resolved = await _nexusModDownloadResolverService.ResolveDownloadUrlAsync(
                parsed,
                settings.NexusApiKey,
                settings.NexusOAuthAccessToken);

            if (!resolved.IsSuccess)
            {
                NxmImportStatus = resolved.Message;
                Status = "导入失败：无法解析真实下载地址";
                EmitLog($"NXM Mod 地址解析失败: {resolved.Message}");
                await _dialogService.ShowBrowserDownloadGuideDialogAsync(
                    BuildNexusWebUrl(parsed),
                    "浏览器下载指引",
                    "该资源可能需要在浏览器登录后下载。请先在浏览器打开链接并完成授权。");
                return;
            }

            var resolvedFileName = ResolveDownloadFileName(new Uri(resolved.DownloadUrl), resolved.FileName);
            sourceUrl = resolved.DownloadUrl;
            outputFilePath = Path.Combine(_downloadRootPath, resolvedFileName);
            taskName = resolvedFileName;
            taskStatus = "已加入队列（NXM Mod 实下载）";
        }
        else
        {
            NxmImportStatus = "正在通过 Nexus API 解析 Collection 下载地址...";
            var resolved = await _nexusModDownloadResolverService.ResolveCollectionDownloadUrlAsync(
                parsed,
                settings.NexusApiKey,
                settings.NexusOAuthAccessToken);

            if (!resolved.IsSuccess)
            {
                NxmImportStatus = resolved.Message;
                Status = "导入失败：无法解析 Collection 下载地址";
                EmitLog($"NXM Collection 地址解析失败: {resolved.Message}");
                await _dialogService.ShowBrowserDownloadGuideDialogAsync(
                    BuildNexusWebUrl(parsed),
                    "浏览器下载指引",
                    "Collection 资源可能需要在浏览器端确认。请先打开链接并完成操作后重试。"
                );
                return;
            }

            var resolvedFileName = ResolveDownloadFileName(new Uri(resolved.DownloadUrl), resolved.FileName);
            sourceUrl = resolved.DownloadUrl;
            outputFilePath = Path.Combine(_downloadRootPath, resolvedFileName);
            taskName = resolvedFileName;
            taskStatus = "已加入队列（NXM Collection 实下载）";
            dependencyUrls = resolved.DownloadUrls
                .Where(url => !string.Equals(url, sourceUrl, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        DownloadTasks.Insert(0, new DownloadTaskItem
        {
            Name = taskName,
            Status = taskStatus,
            Progress = 0,
            TaskKind = parsed.ResourceType == NxmResourceType.Collection
                ? DownloadTaskKind.NxmCollection
                : DownloadTaskKind.NxmMod,
            SourceUrl = sourceUrl,
            OutputFilePath = outputFilePath,
            DependencyUrls = dependencyUrls,
            CanCancel = false,
            CanRetry = false
        });
        DownloadTasks[0].StatusIconSource = ResolveTaskStatusIcon(DownloadTasks[0]);

        NxmImportStatus = $"已解析并入队：{parsed}";
        Status = "NXM 链接已加入下载队列";
        SaveTaskState();
        _ = ProcessQueueAsync();
        NavigateToTaskStatusRequested?.Invoke();
    }

    [RelayCommand]
    private async Task QueueUrlDownload()
    {
        if (!await EnsureGamePathConfiguredAsync())
        {
            UrlDownloadStatus = "请先配置有效的游戏目录";
            Status = "入队失败：未配置游戏目录";
            return;
        }

        var rawUrl = DownloadUrlInput?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            UrlDownloadStatus = "下载地址无效，请输入 HTTP/HTTPS 链接";
            Status = "入队失败：URL 无效";
            return;
        }

        var fileName = ResolveDownloadFileName(uri, DownloadFileNameInput);
        var targetFilePath = Path.Combine(_downloadRootPath, fileName);

        DownloadTasks.Insert(0, new DownloadTaskItem
        {
            Name = fileName,
            Status = "已加入队列（URL 下载）",
            Progress = 0,
            TaskKind = DownloadTaskKind.Generic,
            SourceUrl = uri.ToString(),
            OutputFilePath = targetFilePath,
            CanCancel = false,
            CanRetry = false
        });
        DownloadTasks[0].StatusIconSource = ResolveTaskStatusIcon(DownloadTasks[0]);

        UrlDownloadStatus = $"已入队：{fileName}";
        Status = "URL 下载任务已加入队列";
        EmitLog($"URL 下载入队: {fileName} -> {targetFilePath}");
        SaveTaskState();
        _ = ProcessQueueAsync();
        NavigateToTaskStatusRequested?.Invoke();
    }

    [RelayCommand]
    private void RetryTask(DownloadTaskItem? task)
    {
        if (task == null || !task.CanRetry)
        {
            return;
        }

        if (task.TaskKind == DownloadTaskKind.NxmCollection && task.FailedDownloadUrls.Count > 0)
        {
            var retryUrls = task.FailedDownloadUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (retryUrls.Count > 0)
            {
                task.SourceUrl = retryUrls[0];
                task.DependencyUrls = retryUrls.Skip(1).ToList();
                EmitLog($"Collection 失败项重试: 本次仅重试 {retryUrls.Count} 个文件");
            }
        }

        task.FailedDetails = string.Empty;
        task.CanRetry = false;
        task.CanCancel = false;
        task.Progress = 0;
        task.Status = "已加入队列（重试）";
        Status = $"任务已重新入队: {task.Name}";
        EmitLog($"任务重试入队: {task.Name}");
        SaveTaskState();
        _ = ProcessQueueAsync();
    }

    [RelayCommand]
    private void CancelTask(DownloadTaskItem? task)
    {
        if (task == null || !task.CanCancel)
        {
            return;
        }

        if (_runningTaskCancellationSources.TryGetValue(task, out var cts))
        {
            cts.Cancel();
            return;
        }

        task.CanCancel = false;
        task.Status = "已取消";
        TaskStateChanged?.Invoke(task);
        EmitLog($"任务取消: {task.Name}");
        SaveTaskState();
    }

    [RelayCommand]
    private void OpenTaskReport(DownloadTaskItem? task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.ReportPath))
        {
            Status = "未找到可打开的安装报告";
            return;
        }

        if (!File.Exists(task.ReportPath) && !Directory.Exists(task.ReportPath))
        {
            Status = "安装报告路径不存在";
            return;
        }

        TryOpenPath(task.ReportPath);
        EmitLog($"打开安装报告: {task.ReportPath}");
    }

    [RelayCommand]
    private void OpenTaskBackup(DownloadTaskItem? task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.BackupPath))
        {
            Status = "未找到可打开的备份目录";
            return;
        }

        if (!Directory.Exists(task.BackupPath))
        {
            Status = "备份目录不存在";
            return;
        }

        TryOpenPath(task.BackupPath);
        EmitLog($"打开备份目录: {task.BackupPath}");
    }

    [RelayCommand]
    private void OpenTaskRetryReport(DownloadTaskItem? task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.RetryReportPath))
        {
            Status = "未找到可打开的重试报告";
            return;
        }

        if (!File.Exists(task.RetryReportPath))
        {
            Status = "重试报告路径不存在";
            return;
        }

        TryOpenPath(task.RetryReportPath);
        EmitLog($"打开重试报告: {task.RetryReportPath}");
    }

    [RelayCommand]
    private async Task CopyTaskFailedDetailsAsync(DownloadTaskItem? task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.FailedDetails))
        {
            Status = "当前任务没有可复制的失败明细";
            return;
        }

        var clipboard = GetClipboard();
        if (clipboard == null)
        {
            Status = "当前环境不支持剪贴板";
            return;
        }

        await clipboard.SetTextAsync(task.FailedDetails);
        Status = "失败明细已复制到剪贴板";
    }

    public void AddTaskFromExternal(ExternalDownloadRequest request)
    {
        _ = AddTaskFromExternalAsync(request);
    }

    public async Task<bool> AddTaskFromExternalAsync(ExternalDownloadRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ResourceName))
        {
            return false;
        }

        if (request.Action == ExternalDownloadAction.SaveAs)
        {
            return await QueueSaveOnlyTaskFromExternalAsync(request);
        }

        if (IsSmapiExternalRequest(request))
        {
            return await QueueSmapiInstallTaskFromExternalAsync(request);
        }

        return QueueGenericInstallTaskFromExternal(request);
    }

    private bool QueueGenericInstallTaskFromExternal(ExternalDownloadRequest request)
    {
        var taskName = request.ToTaskDisplayName();
        var sourceUrl = TryResolveDirectDownloadUrl(request.SelectedDownloadOption);
        var outputPath = string.Empty;

        if (!string.IsNullOrWhiteSpace(sourceUrl) &&
            Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var fileName = ResolveDownloadFileName(uri, request.ResolveSuggestedFileName());
            outputPath = Path.Combine(_downloadRootPath, fileName);
            taskName = fileName;
        }

        var task = new DownloadTaskItem
        {
            Name = taskName,
            Status = string.IsNullOrWhiteSpace(outputPath) ? "已加入队列" : "已加入队列（真实下载）",
            Progress = 0,
            TaskKind = DownloadTaskKind.Generic,
            TaskAction = DownloadTaskAction.InstallMod,
            SourceUrl = sourceUrl,
            OutputFilePath = outputPath,
            CanCancel = false,
            CanRetry = false
        };

        EnqueueExternalTask(task, $"已加入下载队列: {taskName}");
        return true;
    }

    private async Task<bool> QueueSaveOnlyTaskFromExternalAsync(ExternalDownloadRequest request)
    {
        var resolved = await ResolveExternalDownloadTargetAsync(request);
        if (!resolved.IsSuccess)
        {
            Status = resolved.Message;
            if (!string.IsNullOrWhiteSpace(resolved.BrowserGuideUrl))
            {
                await _dialogService.ShowBrowserDownloadGuideDialogAsync(
                    resolved.BrowserGuideUrl,
                    "浏览器下载指引",
                    "该资源当前无法直接解析下载地址，请在浏览器完成下载后再返回。"
                );
            }

            return false;
        }

        var suggestedFileName = CreateSafeFileName(resolved.FileName);
        var savePath = await _dialogService.SaveFilePathAsync(
            "另存为",
            suggestedFileName,
            BuildSaveFileTypes(suggestedFileName));
        if (string.IsNullOrWhiteSpace(savePath))
        {
            Status = "已取消另存为";
            return false;
        }

        var task = new DownloadTaskItem
        {
            Name = Path.GetFileName(savePath),
            Status = "已加入队列（另存为）",
            Progress = 0,
            TaskKind = DownloadTaskKind.Generic,
            TaskAction = DownloadTaskAction.SaveOnly,
            SourceUrl = resolved.DownloadUrl,
            OutputFilePath = savePath,
            CanCancel = false,
            CanRetry = false
        };

        EnqueueExternalTask(task, $"已加入另存为队列: {task.Name}");
        return true;
    }

    private async Task<bool> QueueSmapiInstallTaskFromExternalAsync(ExternalDownloadRequest request)
    {
        if (!await EnsureGamePathConfiguredAsync())
        {
            Status = "SMAPI 安装失败：未配置有效游戏目录";
            return false;
        }

        var gameBasePath = ResolveCurrentGamePath();
        if (string.IsNullOrWhiteSpace(gameBasePath) || !Directory.Exists(gameBasePath))
        {
            Status = "SMAPI 安装失败：游戏路径不可用";
            return false;
        }

        var confirmed = await _dialogService.ShowGamePathConfirmDialogAsync(
            gameBasePath,
            "确认 SMAPI 基础路径",
            "SMAPI 将基于该目录创建新的版本实例，请确认路径正确。"
        );
        if (!confirmed)
        {
            Status = "已取消 SMAPI 安装";
            return false;
        }

        var defaultName = BuildSmapiDefaultInstanceName(request);
        var rawInstanceName = await _dialogService.ShowInstanceNameDialogAsync("输入 SMAPI 实例名称", defaultName);
        if (string.IsNullOrWhiteSpace(rawInstanceName))
        {
            Status = "已取消 SMAPI 安装";
            return false;
        }

        var instanceName = CreateSafeFileName(rawInstanceName);
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            Status = "实例名称无效";
            return false;
        }

        var versionRoot = Path.Combine(gameBasePath, "versions", instanceName);
        if (Directory.Exists(versionRoot))
        {
            Status = $"实例名称已存在: {instanceName}";
            return false;
        }

        var resolved = await ResolveExternalDownloadTargetAsync(request);
        if (!resolved.IsSuccess)
        {
            Status = resolved.Message;
            if (!string.IsNullOrWhiteSpace(resolved.BrowserGuideUrl))
            {
                await _dialogService.ShowBrowserDownloadGuideDialogAsync(
                    resolved.BrowserGuideUrl,
                    "浏览器下载指引",
                    "该 SMAPI 资源需要在浏览器完成下载授权，请完成后重试。"
                );
            }

            return false;
        }

        var safeResolvedFileName = CreateSafeFileName(resolved.FileName);
        var outputPath = Path.Combine(_downloadRootPath, safeResolvedFileName);
        var task = new DownloadTaskItem
        {
            Name = $"SMAPI 安装 - {instanceName}",
            Status = "已加入队列（SMAPI 安装）",
            Progress = 0,
            TaskKind = DownloadTaskKind.Generic,
            TaskAction = DownloadTaskAction.InstallSmapi,
            SourceUrl = resolved.DownloadUrl,
            OutputFilePath = outputPath,
            TargetGamePath = gameBasePath,
            TargetInstanceName = instanceName,
            CanCancel = false,
            CanRetry = false
        };

        EnqueueExternalTask(task, $"已加入 SMAPI 安装队列: {instanceName}");
        return true;
    }

    private void EnqueueExternalTask(DownloadTaskItem task, string statusText)
    {
        DownloadTasks.Insert(0, task);
        DownloadTasks[0].StatusIconSource = ResolveTaskStatusIcon(DownloadTasks[0]);
        Status = statusText;
        SaveTaskState();
        _ = ProcessQueueAsync();
        NavigateToTaskStatusRequested?.Invoke();
    }

    private async Task<ResolvedExternalDownloadTarget> ResolveExternalDownloadTargetAsync(ExternalDownloadRequest request)
    {
        var sourceToken = NormalizeSourceToken(request);
        var directUrl = TryResolveDirectDownloadUrl(request.SelectedDownloadOption);
        var fallbackGuideUrl = BuildFallbackGuideUrl(request);

        if (sourceToken == "nexusmods" &&
            TryExtractPositiveLong(request.ResourceId, out var modId) &&
            TryExtractFileIdFromOption(request.SelectedDownloadOption, out var fileId))
        {
            var settings = _settingsStore.Load();
            if (string.IsNullOrWhiteSpace(settings.NexusApiKey) && string.IsNullOrWhiteSpace(settings.NexusOAuthAccessToken))
            {
                return ResolvedExternalDownloadTarget.Fail("请先在设置页完成 Nexus 登录后再下载", fallbackGuideUrl);
            }

            var info = new NxmLinkInfo
            {
                ResourceType = NxmResourceType.ModFile,
                GameDomain = "stardewvalley",
                ModId = modId,
                FileId = fileId
            };

            var resolved = await _nexusModDownloadResolverService.ResolveDownloadUrlAsync(
                info,
                settings.NexusApiKey,
                settings.NexusOAuthAccessToken);
            if (resolved.IsSuccess &&
                Uri.TryCreate(resolved.DownloadUrl, UriKind.Absolute, out var resolvedUri))
            {
                var fileName = ResolveDownloadFileName(resolvedUri, resolved.FileName);
                return ResolvedExternalDownloadTarget.Success(resolved.DownloadUrl, fileName);
            }

            if (!string.IsNullOrWhiteSpace(directUrl) &&
                Uri.TryCreate(directUrl, UriKind.Absolute, out var directUri))
            {
                var fallbackName = ResolveDownloadFileName(directUri, request.ResolveSuggestedFileName());
                return ResolvedExternalDownloadTarget.Success(directUrl, fallbackName);
            }

            return ResolvedExternalDownloadTarget.Fail(resolved.Message, fallbackGuideUrl);
        }

        if (sourceToken == "curseforge" &&
            TryExtractPositiveLong(request.ResourceId, out var curseforgeModId) &&
            TryExtractFileIdFromOption(request.SelectedDownloadOption, out var curseforgeFileId))
        {
            var resolvedUrl = await _remoteCatalogService.ResolveCurseforgeFileDownloadUrlAsync(
                curseforgeModId,
                curseforgeFileId,
                directUrl);
            if (!string.IsNullOrWhiteSpace(resolvedUrl) &&
                Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var curseUri))
            {
                var fileName = ResolveDownloadFileName(curseUri, request.ResolveSuggestedFileName());
                return ResolvedExternalDownloadTarget.Success(resolvedUrl, fileName);
            }
        }

        if (!string.IsNullOrWhiteSpace(directUrl) &&
            Uri.TryCreate(directUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var fileName = ResolveDownloadFileName(uri, request.ResolveSuggestedFileName());
            return ResolvedExternalDownloadTarget.Success(directUrl, fileName);
        }

        return ResolvedExternalDownloadTarget.Fail("未解析到可用下载地址", fallbackGuideUrl);
    }

    private string ResolveCurrentGamePath()
    {
        var settings = _settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.PreferredInstancePath) && Directory.Exists(settings.PreferredInstancePath))
        {
            return settings.PreferredInstancePath;
        }

        if (!string.IsNullOrWhiteSpace(GamePathHint) && Directory.Exists(GamePathHint))
        {
            return GamePathHint;
        }

        return string.Empty;
    }

    private static bool IsSmapiExternalRequest(ExternalDownloadRequest request)
    {
        if (request.IsSmapiResource)
        {
            return true;
        }

        var combined = string.Join('|',
            request.ResourceName ?? string.Empty,
            request.ResourceSource ?? string.Empty,
            request.SourceToken ?? string.Empty,
            request.SourcePageUrl ?? string.Empty,
            request.ResourceId ?? string.Empty,
            request.SelectedDownloadOption ?? string.Empty);

        if (combined.Contains("smapi", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals((request.ResourceId ?? string.Empty).Trim(), "2400", StringComparison.OrdinalIgnoreCase) ||
               string.Equals((request.ResourceId ?? string.Empty).Trim(), "898372", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSourceToken(ExternalDownloadRequest request)
    {
        var raw = string.Join('|',
            request.SourceToken ?? string.Empty,
            request.ResourceSource ?? string.Empty)
            .ToLowerInvariant();

        if (raw.Contains("nexus"))
        {
            return "nexusmods";
        }

        if (raw.Contains("curse"))
        {
            return "curseforge";
        }

        if (raw.Contains("github"))
        {
            return "github";
        }

        return string.Empty;
    }

    private static string TryResolveDirectDownloadUrl(string option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return string.Empty;
        }

        var trimmed = option.Trim();
        var markerIndex = trimmed.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            markerIndex = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        }

        if (markerIndex >= 0)
        {
            var candidate = trimmed[markerIndex..].Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var byMarker) &&
                (byMarker.Scheme == Uri.UriSchemeHttp || byMarker.Scheme == Uri.UriSchemeHttps))
            {
                return byMarker.ToString();
            }
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            return parsed.ToString();
        }

        return string.Empty;
    }

    private static bool TryExtractFileIdFromOption(string option, out long fileId)
    {
        fileId = 0;
        if (string.IsNullOrWhiteSpace(option))
        {
            return false;
        }

        var match = Regex.Match(option, "(?:File\\s+)?(?<id>\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        return long.TryParse(match.Groups["id"].Value, out fileId) && fileId > 0;
    }

    private static bool TryExtractPositiveLong(string raw, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (long.TryParse(raw.Trim(), out var parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }

        var match = Regex.Match(raw, "(?<id>\\d+)", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        return long.TryParse(match.Groups["id"].Value, out value) && value > 0;
    }

    private static string BuildFallbackGuideUrl(ExternalDownloadRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePageUrl))
        {
            return request.SourcePageUrl;
        }

        var sourceToken = NormalizeSourceToken(request);
        if (sourceToken == "nexusmods")
        {
            if (TryExtractPositiveLong(request.ResourceId, out var nexusModId))
            {
                return $"https://www.nexusmods.com/stardewvalley/mods/{nexusModId}";
            }

            return "https://www.nexusmods.com/stardewvalley/mods";
        }

        if (sourceToken == "curseforge")
        {
            if (TryExtractPositiveLong(request.ResourceId, out var curseId))
            {
                return $"https://www.curseforge.com/projects/{curseId}";
            }

            return "https://www.curseforge.com/stardewvalley/mods";
        }

        return "https://github.com/Pathoschild/SMAPI/releases";
    }

    private static string BuildSmapiDefaultInstanceName(ExternalDownloadRequest request)
    {
        var text = string.Join(' ', request.ResourceName, request.SelectedDownloadOption);
        var match = Regex.Match(text, "(?<version>\\d+\\.\\d+(?:\\.\\d+)*)", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            return $"SMAPI {match.Groups["version"].Value}";
        }

        return "SMAPI";
    }

    private static IReadOnlyList<global::Avalonia.Platform.Storage.FilePickerFileType> BuildSaveFileTypes(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ext))
        {
            return [new global::Avalonia.Platform.Storage.FilePickerFileType("所有文件") { Patterns = ["*.*"] }];
        }

        var normalized = ext.StartsWith('.') ? ext : "." + ext;
        var label = normalized.ToLowerInvariant() switch
        {
            ".zip" => "ZIP 压缩包",
            ".7z" => "7z 压缩包",
            ".rar" => "RAR 压缩包",
            _ => $"{normalized.ToUpperInvariant()} 文件"
        };

        return
        [
            new global::Avalonia.Platform.Storage.FilePickerFileType(label)
            {
                Patterns = [$"*{normalized}"]
            },
            new global::Avalonia.Platform.Storage.FilePickerFileType("所有文件")
            {
                Patterns = ["*.*"]
            }
        ];
    }

    private sealed class ResolvedExternalDownloadTarget
    {
        public bool IsSuccess { get; init; }

        public string DownloadUrl { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string BrowserGuideUrl { get; init; } = string.Empty;

        public static ResolvedExternalDownloadTarget Success(string downloadUrl, string fileName)
        {
            return new ResolvedExternalDownloadTarget
            {
                IsSuccess = true,
                DownloadUrl = downloadUrl,
                FileName = fileName,
                Message = "下载地址解析成功"
            };
        }

        public static ResolvedExternalDownloadTarget Fail(string message, string browserGuideUrl)
        {
            return new ResolvedExternalDownloadTarget
            {
                IsSuccess = false,
                Message = string.IsNullOrWhiteSpace(message) ? "未解析到可用下载地址" : message,
                BrowserGuideUrl = browserGuideUrl
            };
        }
    }

    private async Task LoadCategoryItemsForCurrentCategoryAsync(bool initialLoad)
    {
        var loadToken = Interlocked.Increment(ref _catalogLoadToken);
        var isHotModsLoad = false;
        var isAutoHotCollectionsLoad = false;
        IsCatalogLoading = true;
        // Clear previous category list immediately to avoid stale list flash during category switch.
        CategoryItems.Clear();
        ClearSmapiSourceItems();
        EmitLog($"[Catalog] Begin load token={loadToken}, category={SelectedCategory}, initialLoad={initialLoad}");
        OnPropertyChanged(nameof(HasNoCategoryItems));
        OnPropertyChanged(nameof(HasCategoryItems));

        try
        {
            List<string> results;
            string queryHint;

            switch (SelectedCategory)
            {
                case DownloadCategory.Smapi:
                    queryHint = string.IsNullOrWhiteSpace(SmapiSearchText) ? "SMAPI" : $"SMAPI {SmapiSearchText.Trim()}";
                    EmitLog($"[Catalog] SMAPI query='{queryHint}', source='{SelectedSmapiSource}'");
                    results = await _remoteCatalogService.SearchSmapiAsync(queryHint, SelectedSmapiSource);
                    break;

                case DownloadCategory.Mods:
                    isHotModsLoad = _forceHotModsLoad || initialLoad || string.IsNullOrWhiteSpace(ModSearchText);
                    _forceHotModsLoad = false;
                    queryHint = isHotModsLoad ? string.Empty : ModSearchText.Trim();
                    var modSource = SelectedModSource;
                    EmitLog($"[Catalog] MOD query='{queryHint}', hotOnly={isHotModsLoad}, source='{modSource}', version='{SelectedModGameVersion}', type='{SelectedModType}', mode='{SelectedModDescriptionMode}'");
                    var paged = await _remoteCatalogService.SearchModsAdvancedPagedAsync(
                        queryHint,
                        modSource,
                        SelectedModGameVersion,
                        SelectedModType,
                        UseLocalizedModDescription,
                        isHotModsLoad,
                        CurrentModPage,
                        ModPageSize);
                    _modHasMore = paged.HasMore;
                    results = paged.Items;

                    TotalModPages = _modHasMore ? CurrentModPage + 1 : CurrentModPage;
                    break;

                case DownloadCategory.Modpacks:
                    isAutoHotCollectionsLoad = initialLoad && string.IsNullOrWhiteSpace(ModpackSearchText);
                    queryHint = string.IsNullOrWhiteSpace(ModpackSearchText) ? string.Empty : ModpackSearchText.Trim();
                    var modpackSource = isAutoHotCollectionsLoad ? "NexusMods" : "全部";
                    EmitLog($"[Catalog] Modpack query='{queryHint}', source='{modpackSource}', autoHot={isAutoHotCollectionsLoad}");
                    var modpackPaged = await _remoteCatalogService.SearchModpacksPagedAsync(queryHint, modpackSource, CurrentModpackPage, ModpackPageSize);
                    _modpackHasMore = modpackPaged.HasMore;
                    results = modpackPaged.Items;
                    TotalModpackPages = _modpackHasMore ? CurrentModpackPage + 1 : CurrentModpackPage;
                    break;

                default:
                    results = [];
                    break;
            }

            if (loadToken != Volatile.Read(ref _catalogLoadToken))
            {
                EmitLog($"[Catalog] Skip outdated token={loadToken}");
                return;
            }

            EmitLog($"[Catalog] Loaded raw results={results.Count}, category={SelectedCategory}");

            ReplaceStringCollection(SearchResults, results);
            if (SelectedCategory == DownloadCategory.Smapi)
            {
                CategoryItems.Clear();
                SyncSmapiSourceItems(results);
                _ = ResolveSmapiCardIconsAsync(loadToken);
            }
            else
            {
                ClearSmapiSourceItems();
                if (SelectedCategory == DownloadCategory.Mods)
                {
                    SyncModCategoryItems(results);
                }
                else if (SelectedCategory == DownloadCategory.Modpacks)
                {
                    SyncModpackCategoryItems(results);
                }
                else
                {
                    SyncCategoryItems(results);
                }
                _ = ResolveCategoryCardIconsAsync(loadToken);
            }

            if (SelectedCategory == DownloadCategory.Smapi)
            {
                var cardCount = SmapiGithubItems.Count + SmapiNexusModsItems.Count + SmapiCurseforgeItems.Count;
                Status = cardCount > 0
                    ? $"已准备 {cardCount} 个来源卡片"
                    : "SMAPI 来源卡片加载失败";
            }
            else if (SelectedCategory == DownloadCategory.Mods)
            {
                if (results.Count == 0)
                {
                    Status = isHotModsLoad ? "未获取到热门 Mod，请调整来源后重试" : "未找到匹配 Mod";
                    EmitLog("[Catalog] Mod list is empty after search/filter.");
                }
                else
                {
                    Status = isHotModsLoad
                        ? $"已加载 {results.Count} 条热门 Mod"
                        : $"已筛选得到 {results.Count} 条 Mod";
                    EmitLog($"[Catalog] Mod cards ready count={results.Count}");
                }
            }
            else if (SelectedCategory == DownloadCategory.Modpacks)
            {
                if (results.Count == 0)
                {
                    Status = isAutoHotCollectionsLoad
                        ? "未获取到 Nexus 热门 Collection，请稍后重试"
                        : (initialLoad ? "已加载，暂无可展示资源" : "未找到匹配资源");
                }
                else
                {
                    Status = isAutoHotCollectionsLoad
                        ? $"已加载 {results.Count} 条 Nexus 热门 Collection"
                        : $"第 {CurrentModpackPage}/{TotalModpackPages} 页，共 {results.Count} 条资源";
                }
            }
            else if (results.Count == 0)
            {
                Status = initialLoad ? "已加载，暂无可展示资源" : "未找到匹配资源";
            }
            else
            {
                Status = SelectedCategory == DownloadCategory.Modpacks
                    ? $"第 {CurrentModpackPage}/{TotalModpackPages} 页，共 {results.Count} 条资源"
                    : (initialLoad ? $"已加载 {results.Count} 条资源" : $"筛选得到 {results.Count} 条资源");
            }
        }
        catch (Exception ex)
        {
            if (loadToken != Volatile.Read(ref _catalogLoadToken))
            {
                return;
            }

            var message = ex.Message;
            if (message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("handshake", StringComparison.OrdinalIgnoreCase))
            {
                Status = "资源加载失败：SSL/TLS 连接异常。请到“设置 -> 下载设置”启用代理并填写代理地址后重试。";
            }
            else
            {
                Status = $"资源加载失败: {message}";
            }
            EmitLog($"[Catalog] Load failed token={loadToken}, error={ex.Message}");
            CategoryItems.Clear();
            SearchResults.Clear();
            ClearSmapiSourceItems();
        }
        finally
        {
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                IsCatalogLoading = false;
                OnPropertyChanged(nameof(HasNoCategoryItems));
                OnPropertyChanged(nameof(HasCategoryItems));
                EmitLog($"[Catalog] End load token={loadToken}, hasItems={CategoryItems.Count > 0 || SmapiGithubItems.Count + SmapiNexusModsItems.Count + SmapiCurseforgeItems.Count > 0}");
            }
        }
    }

    private void SyncCategoryItems(IEnumerable<string> results)
    {
        CategoryItems.Clear();
        foreach (var result in results)
        {
            var item = ParseCatalogItem(result);
            if (SelectedCategory == DownloadCategory.Mods)
            {
                ApplyModLocalizationPreferenceToItem(item);
            }

            CategoryItems.Add(item);
        }

        ApplyModLocalizationPreferenceToCategoryItems();

        OnPropertyChanged(nameof(HasNoCategoryItems));
        OnPropertyChanged(nameof(HasCategoryItems));
    }

    private void SyncModCategoryItems(IEnumerable<string> results)
    {
        CategoryItems.Clear();
        foreach (var result in results)
        {
            var item = ParseCatalogItem(result);
            ApplyModLocalizationPreferenceToItem(item);
            CategoryItems.Add(item);
        }

        ApplyModLocalizationPreferenceToCategoryItems();
        OnPropertyChanged(nameof(HasNoCategoryItems));
        OnPropertyChanged(nameof(HasCategoryItems));
        OnPropertyChanged(nameof(ModPageInfoText));
        OnPropertyChanged(nameof(IsModsPageable));
    }

    private void SyncModpackCategoryItems(IEnumerable<string> results)
    {
        CategoryItems.Clear();
        foreach (var result in results)
        {
            CategoryItems.Add(ParseCatalogItem(result));
        }

        OnPropertyChanged(nameof(HasNoCategoryItems));
        OnPropertyChanged(nameof(HasCategoryItems));
        OnPropertyChanged(nameof(ModpackPageInfoText));
        OnPropertyChanged(nameof(IsModpacksPageable));
    }

    private void ApplyCurrentModPageItems()
    {
        CategoryItems.Clear();
        if (_modAllResults.Count == 0)
        {
            return;
        }

        var skip = (CurrentModPage - 1) * ModPageSize;
        var pageItems = _modAllResults.Skip(skip).Take(ModPageSize);
        foreach (var result in pageItems)
        {
            var item = ParseCatalogItem(result);
            ApplyModLocalizationPreferenceToItem(item);
            CategoryItems.Add(item);
        }

        OnPropertyChanged(nameof(HasNoCategoryItems));
        OnPropertyChanged(nameof(HasCategoryItems));
        OnPropertyChanged(nameof(ModPageInfoText));
    }

    private void ApplyCurrentModpackPageItems()
    {
        CategoryItems.Clear();
        if (_modpackAllResults.Count == 0)
        {
            return;
        }

        var skip = (CurrentModpackPage - 1) * ModpackPageSize;
        var pageItems = _modpackAllResults.Skip(skip).Take(ModpackPageSize);
        foreach (var result in pageItems)
        {
            CategoryItems.Add(ParseCatalogItem(result));
        }

        OnPropertyChanged(nameof(HasNoCategoryItems));
        OnPropertyChanged(nameof(HasCategoryItems));
        OnPropertyChanged(nameof(ModpackPageInfoText));
    }

    private string BuildModCacheKey(string queryHint, bool isHotModsLoad)
    {
        return string.Join("|", [
            "mods",
            SelectedModSource,
            SelectedModGameVersion,
            SelectedModType,
            SelectedModDescriptionMode,
            isHotModsLoad ? "hot" : "search",
            queryHint
        ]);
    }

    private bool TryGetCachedModResults(string key, out List<string> results)
    {
        results = [];
        if (!_modResultsCache.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (DateTime.Now - entry.CreatedAt > ModSearchCacheTtl)
        {
            _modResultsCache.Remove(key);
            return false;
        }

        results = [..entry.Results];
        return true;
    }

    private void SetCachedModResults(string key, IEnumerable<string> results)
    {
        _modResultsCache[key] = (DateTime.Now, [..results]);
    }

    private void ApplyModLocalizationPreferenceToCategoryItems()
    {
        if (SelectedCategory != DownloadCategory.Mods)
        {
            return;
        }

        foreach (var item in CategoryItems)
        {
            ApplyModLocalizationPreferenceToItem(item);
        }
    }

    private void ApplyModLocalizationPreferenceToItem(DownloadCatalogItem item)
    {
        if (item == null)
        {
            return;
        }

        var useLocalized = UseLocalizedModDescription;
        item.UseLocalizedText = useLocalized;
        item.UseLocalizedName = useLocalized;
        item.UseLocalizedSummary = useLocalized;
    }

    private void SyncSmapiSourceItems(IEnumerable<string> results)
    {
        ClearSmapiSourceItems();

        var parsedItems = results
            .Select(ParseCatalogItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceKey))
            .ToList();

        var sourceKeys = GetRequestedSmapiSourceKeys();
        foreach (var sourceKey in sourceKeys)
        {
            var bestItem = SelectBestSmapiSourceItem(parsedItems, sourceKey) ?? BuildSmapiPlaceholderItem(sourceKey);
            bestItem = NormalizeSmapiCardPresentation(bestItem, sourceKey);
            switch (sourceKey)
            {
                case "github":
                    SmapiGithubItems.Add(bestItem);
                    break;
                case "nexusmods":
                    SmapiNexusModsItems.Add(bestItem);
                    break;
                case "curseforge":
                    SmapiCurseforgeItems.Add(bestItem);
                    break;
                default:
                    break;
            }
        }

        RaiseSmapiSourceState();
    }

    private List<string> GetRequestedSmapiSourceKeys()
    {
        return SelectedSmapiSource switch
        {
            "GitHub" => ["github"],
            "NexusMods" => ["nexusmods"],
            "Curseforge" => ["curseforge"],
            _ => ["github", "nexusmods", "curseforge"]
        };
    }

    private static DownloadCatalogItem? SelectBestSmapiSourceItem(IEnumerable<DownloadCatalogItem> items, string sourceKey)
    {
        return items
            .Where(item => string.Equals(item.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => ComputeSmapiItemScore(item))
            .FirstOrDefault();
    }

    private static int ComputeSmapiItemScore(DownloadCatalogItem item)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(item.Name) &&
            item.Name.Contains("smapi", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(item.Summary) &&
            item.Summary.Contains("smapi", StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        if (!string.IsNullOrWhiteSpace(item.Stat))
        {
            score += 2;
        }

        return score;
    }

    private static DownloadCatalogItem BuildSmapiPlaceholderItem(string sourceKey)
    {
        var sourceLabel = ResolveSourceLabel(sourceKey, sourceKey);
        var sourceId = sourceKey switch
        {
            "nexusmods" => "2400",
            "curseforge" => "898372",
            _ => "0"
        };

        var displayText = $"[{sourceLabel}#{sourceId}] {SmapiDefaultName} | metric= | time= | icon=avares://SVL.Avalonia/Assets/Icons/Modded.png | {SmapiDefaultSummary}";
        return new DownloadCatalogItem
        {
            DisplayText = displayText,
            Name = SmapiDefaultName,
            SourceTag = sourceLabel,
            SourceKey = sourceKey,
            Stat = string.Empty,
            MetricTag = string.Empty,
            TimeTag = string.Empty,
            Summary = SmapiDefaultSummary,
            IconSource = "avares://SVL.Avalonia/Assets/Icons/Modded.png"
        };
    }

    private static DownloadCatalogItem NormalizeSmapiCardPresentation(DownloadCatalogItem item, string sourceKey)
    {
        item.SourceKey = sourceKey;
        item.SourceTag = ResolveSourceLabel(sourceKey, item.SourceTag);
        item.Name = SmapiDefaultName;
        item.Summary = SmapiDefaultSummary;
        if (string.IsNullOrWhiteSpace(item.MetricTag) && !string.IsNullOrWhiteSpace(item.Stat))
        {
            item.MetricTag = item.Stat;
        }

        if (string.IsNullOrWhiteSpace(item.IconSource))
        {
            item.IconSource = ResolveSmapiIconSource(sourceKey);
        }

        return item;
    }

    private HttpClient GetIconHttpClient()
    {
        var settings = _settingsStore.Load();
        var signature = BuildIconProxySignature(settings);

        lock (IconHttpClientLock)
        {
            if (_smapiIconHttpClient != null && string.Equals(signature, _smapiIconProxySignature, StringComparison.Ordinal))
            {
                return _smapiIconHttpClient;
            }

            _smapiIconHttpClient?.Dispose();
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (settings.EnableDownloadProxy &&
                TryResolveIconProxyUri(settings.DownloadProxyUrl, out var proxyUri))
            {
                var proxy = new WebProxy(proxyUri);
                if (!string.IsNullOrWhiteSpace(settings.DownloadProxyUserName))
                {
                    proxy.Credentials = new NetworkCredential(
                        settings.DownloadProxyUserName.Trim(),
                        settings.DownloadProxyPassword ?? string.Empty);
                }

                handler.UseProxy = true;
                handler.Proxy = proxy;
            }

            _smapiIconHttpClient = new HttpClient(handler, disposeHandler: true);
            _smapiIconHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SVL-Avalonia-IconFetcher");
            _smapiIconProxySignature = signature;
            return _smapiIconHttpClient;
        }
    }

    private static string BuildIconProxySignature(AppUserSettings settings)
    {
        if (!settings.EnableDownloadProxy)
        {
            return "disabled";
        }

        return string.Join('|',
            "enabled",
            settings.DownloadProxyUrl?.Trim() ?? string.Empty,
            settings.DownloadProxyUserName?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(settings.DownloadProxyUserName)
                ? "anonymous"
                : (string.IsNullOrEmpty(settings.DownloadProxyPassword) ? "user-np" : "user-p"));
    }

    private static bool TryResolveIconProxyUri(string? rawProxyUrl, out Uri proxyUri)
    {
        proxyUri = default!;
        if (string.IsNullOrWhiteSpace(rawProxyUrl))
        {
            return false;
        }

        var trimmed = rawProxyUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsedProxyUri) && parsedProxyUri != null)
        {
            proxyUri = parsedProxyUri;
            return true;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate($"http://{trimmed}", UriKind.Absolute, out parsedProxyUri) &&
            parsedProxyUri != null)
        {
            proxyUri = parsedProxyUri;
            return true;
        }

        return false;
    }

    private static string ResolveSmapiIconSource(string sourceKey)
    {
        return sourceKey switch
        {
            "github" => "avares://SVL.Avalonia/Assets/Icons/Modded.png",
            "nexusmods" => "avares://SVL.Avalonia/Assets/Icons/Junimo.png",
            "curseforge" => "avares://SVL.Avalonia/Assets/Icons/Junimo.png",
            _ => "avares://SVL.Avalonia/Assets/Icons/Modded.png"
        };
    }

    private async Task ResolveSmapiCardIconsAsync(int loadToken)
    {
        var items = SmapiGithubItems
            .Concat(SmapiNexusModsItems)
            .Concat(SmapiCurseforgeItems)
            .ToList();

        foreach (var item in items)
        {
            await ResolveSmapiCardIconAsync(item, loadToken);
        }
    }

    private async Task ResolveSmapiCardIconAsync(DownloadCatalogItem item, int loadToken)
    {
        if (item == null || loadToken != Volatile.Read(ref _catalogLoadToken))
        {
            return;
        }

        var iconSource = item.IconSource?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(iconSource, UriKind.Absolute, out var iconUri) ||
            (iconUri.Scheme != Uri.UriSchemeHttp && iconUri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var remoteUrl = iconUri.ToString();
        var fallback = ResolveSmapiIconSource(item.SourceKey);
        item.IconSource = fallback;

        if (_smapiIconDiskCache.TryGetValue(remoteUrl, out var cachedPath) && File.Exists(cachedPath))
        {
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                item.IconSource = cachedPath;
            }

            return;
        }

        var iconPath = BuildSmapiIconCachePath(remoteUrl, iconUri);
        if (File.Exists(iconPath))
        {
            _smapiIconDiskCache[remoteUrl] = iconPath;
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                item.IconSource = iconPath;
            }

            return;
        }

        try
        {
            using var response = await GetIconHttpClient().GetAsync(iconUri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return;
            }

            await File.WriteAllBytesAsync(iconPath, bytes);
            _smapiIconDiskCache[remoteUrl] = iconPath;
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                item.IconSource = iconPath;
            }
        }
        catch
        {
            // Keep fallback icon when remote icon download fails.
        }
    }

    private async Task ResolveCategoryCardIconsAsync(int loadToken)
    {
        var items = CategoryItems.ToList();
        foreach (var item in items)
        {
            await ResolveRemoteIconToLocalAsync(item, loadToken, ResolveCategoryFallbackIcon(item.SourceKey));
        }
    }

    private static string ResolveCategoryFallbackIcon(string sourceKey)
    {
        return sourceKey switch
        {
            "curseforge" => "avares://SVL.Avalonia/Assets/Icons/Junimo.png",
            "nexusmods" => "avares://SVL.Avalonia/Assets/Icons/Junimo.png",
            _ => "avares://SVL.Avalonia/Assets/Icons/Modded.png"
        };
    }

    private async Task ResolveRemoteIconToLocalAsync(DownloadCatalogItem item, int loadToken, string fallback)
    {
        if (item == null || loadToken != Volatile.Read(ref _catalogLoadToken))
        {
            return;
        }

        var iconSource = item.IconSource?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(iconSource, UriKind.Absolute, out var iconUri) ||
            (iconUri.Scheme != Uri.UriSchemeHttp && iconUri.Scheme != Uri.UriSchemeHttps))
        {
            if (string.IsNullOrWhiteSpace(item.IconSource))
            {
                item.IconSource = fallback;
            }

            return;
        }

        var remoteUrl = iconUri.ToString();
        item.IconSource = fallback;

        if (_smapiIconDiskCache.TryGetValue(remoteUrl, out var cachedPath) && File.Exists(cachedPath))
        {
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                item.IconSource = cachedPath;
            }

            return;
        }

        var iconPath = BuildSmapiIconCachePath(remoteUrl, iconUri);
        if (File.Exists(iconPath))
        {
            _smapiIconDiskCache[remoteUrl] = iconPath;
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                item.IconSource = iconPath;
            }

            return;
        }

        try
        {
            using var response = await GetIconHttpClient().GetAsync(iconUri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return;
            }

            await File.WriteAllBytesAsync(iconPath, bytes);
            _smapiIconDiskCache[remoteUrl] = iconPath;
            if (loadToken == Volatile.Read(ref _catalogLoadToken))
            {
                item.IconSource = iconPath;
            }
        }
        catch
        {
            // Keep fallback icon when remote icon download fails.
        }
    }

    private string BuildSmapiIconCachePath(string remoteUrl, Uri iconUri)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(remoteUrl));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var extension = Path.GetExtension(iconUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".img";
        }

        return Path.Combine(_smapiIconCachePath, hash + extension);
    }

    private void ClearSmapiSourceItems()
    {
        SmapiGithubItems.Clear();
        SmapiNexusModsItems.Clear();
        SmapiCurseforgeItems.Clear();
        RaiseSmapiSourceState();
    }

    private void RaiseSmapiSourceState()
    {
        OnPropertyChanged(nameof(HasSmapiGithubItems));
        OnPropertyChanged(nameof(HasSmapiNexusModsItems));
        OnPropertyChanged(nameof(HasSmapiCurseforgeItems));
        OnPropertyChanged(nameof(HasNoSmapiItems));
    }

    private static DownloadCatalogItem ParseCatalogItem(string result)
    {
        var parts = result.Split('|', StringSplitOptions.TrimEntries);
        var header = parts.Length > 0 ? parts[0] : result;
        var stat = string.Empty;
        var metricTag = string.Empty;
        var timeTag = string.Empty;
        var iconSource = string.Empty;
        var fullIconSource = string.Empty;
        var summary = string.Empty;
        var sourceName = string.Empty;
        var sourceSummary = string.Empty;
        var localizedName = string.Empty;
        var localizedSummary = string.Empty;
        var modTypeTag = string.Empty;
        var gameVersionTag = string.Empty;

        for (var index = 1; index < parts.Length; index++)
        {
            var segment = parts[index].Trim();
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            if (segment.StartsWith("metric=", StringComparison.OrdinalIgnoreCase))
            {
                metricTag = segment[7..].Trim();
                if (string.IsNullOrWhiteSpace(stat))
                {
                    stat = metricTag;
                }

                continue;
            }

            if (segment.StartsWith("time=", StringComparison.OrdinalIgnoreCase))
            {
                timeTag = segment[5..].Trim();
                continue;
            }

            if (segment.StartsWith("icon=", StringComparison.OrdinalIgnoreCase))
            {
                iconSource = segment[5..].Trim();
                continue;
            }

            if (segment.StartsWith("fullIcon=", StringComparison.OrdinalIgnoreCase))
            {
                fullIconSource = segment[9..].Trim();
                continue;
            }

            if (segment.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
            {
                modTypeTag = segment[5..].Trim();
                continue;
            }

            if (segment.StartsWith("compat=", StringComparison.OrdinalIgnoreCase))
            {
                gameVersionTag = segment[7..].Trim();
                continue;
            }

            if (segment.StartsWith("srcName=", StringComparison.OrdinalIgnoreCase))
            {
                sourceName = segment[8..].Trim();
                continue;
            }

            if (segment.StartsWith("srcSummary=", StringComparison.OrdinalIgnoreCase))
            {
                sourceSummary = segment[11..].Trim();
                continue;
            }

            if (segment.StartsWith("zhName=", StringComparison.OrdinalIgnoreCase))
            {
                localizedName = segment[7..].Trim();
                continue;
            }

            if (segment.StartsWith("zhSummary=", StringComparison.OrdinalIgnoreCase))
            {
                localizedSummary = segment[10..].Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(stat))
            {
                stat = segment;
                metricTag = segment;
            }
            else if (string.IsNullOrWhiteSpace(summary))
            {
                summary = segment;
            }
            else
            {
                summary = string.Concat(summary, " | ", segment);
            }
        }

        var sourceTag = string.Empty;
        var name = header;

        if (header.StartsWith("[", StringComparison.Ordinal))
        {
            var index = header.IndexOf(']');
            if (index > 1)
            {
                sourceTag = header[1..index].Trim();
                name = header[(index + 1)..].Trim();
            }
        }

        var sourceHead = sourceTag;
        var sourceSplitIndex = sourceTag.IndexOf('#');
        if (sourceSplitIndex > 0)
        {
            sourceHead = sourceTag[..sourceSplitIndex];
        }

        var sourceKey = ResolveSourceKey(sourceHead);
        var sourceLabel = ResolveSourceLabel(sourceKey, sourceHead);

        return new DownloadCatalogItem
        {
            DisplayText = result,
            Name = string.IsNullOrWhiteSpace(name) ? result : name,
            SourceTag = sourceLabel,
            SourceKey = sourceKey,
            Stat = stat,
            MetricTag = metricTag,
            TimeTag = timeTag,
            IconSource = iconSource,
            FullIconSource = fullIconSource,
            Summary = string.IsNullOrWhiteSpace(sourceSummary) ? summary : sourceSummary,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? name : sourceName,
            SourceSummary = string.IsNullOrWhiteSpace(sourceSummary) ? summary : sourceSummary,
            LocalizedName = localizedName,
            LocalizedSummary = localizedSummary,
            ModTypeTag = modTypeTag,
            GameVersionTag = gameVersionTag
        };
    }

    private static string ResolveSourceKey(string sourceText)
    {
        if (sourceText.Contains("github", StringComparison.OrdinalIgnoreCase))
        {
            return "github";
        }

        if (sourceText.Contains("nexus", StringComparison.OrdinalIgnoreCase))
        {
            return "nexusmods";
        }

        if (sourceText.Contains("curse", StringComparison.OrdinalIgnoreCase))
        {
            return "curseforge";
        }

        return "unknown";
    }

    private static string ResolveSourceLabel(string sourceKey, string fallback)
    {
        return sourceKey switch
        {
            "github" => "GitHub",
            "nexusmods" => "NexusMods",
            "curseforge" => "Curseforge",
            _ => string.IsNullOrWhiteSpace(fallback) ? "未知来源" : fallback
        };
    }

    private static void ReplaceStringCollection(ObservableCollection<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            target.Add(item.Trim());
        }
    }

    private async Task ProcessQueueAsync()
    {
        await _queueWorkerGate.WaitAsync();
        try
        {
            if (_isQueueWorkerRunning)
            {
                return;
            }

            _isQueueWorkerRunning = true;
        }
        finally
        {
            _queueWorkerGate.Release();
        }

        try
        {
            while (true)
            {
                var nextTask = DownloadTasks.FirstOrDefault(IsPendingTask);
                if (nextTask == null)
                {
                    break;
                }

                await ExecuteTaskAsync(nextTask);
            }
        }
        finally
        {
            _isQueueWorkerRunning = false;
        }
    }

    private static bool IsPendingTask(DownloadTaskItem task)
    {
        return task.Status.Contains("等待下载", StringComparison.Ordinal) ||
               task.Status.Contains("已加入队列", StringComparison.Ordinal);
    }

    private async Task ExecuteTaskAsync(DownloadTaskItem task)
    {
        if (task.TaskKind == DownloadTaskKind.NxmCollection && HasRealDownloadSource(task))
        {
            await ExecuteCollectionRealDownloadTaskAsync(task);
            return;
        }

        if (task.TaskKind == DownloadTaskKind.NxmCollection)
        {
            await ExecuteCollectionTaskAsync(task);
            return;
        }

        if (HasRealDownloadSource(task))
        {
            await ExecuteRealDownloadTaskAsync(task);
            return;
        }

        task.CanRetry = false;
        task.CanCancel = false;
        task.Status = "下载中";
        task.Progress = 0;
        Status = $"正在执行任务: {task.Name}";
        TaskStateChanged?.Invoke(task);
        EmitLog($"开始执行任务: {task.Name}");

        for (var progress = 0; progress <= 100; progress += 20)
        {
            task.Progress = progress;
            TaskStateChanged?.Invoke(task);
            await Task.Delay(250);
        }

        if (task.Name.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            task.Status = "下载失败（可重试）";
            task.CanRetry = true;
            Status = $"任务失败: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"任务失败，可重试: {task.Name}");
            return;
        }

        task.Status = "安装中";
        TaskStateChanged?.Invoke(task);
        EmitLog($"开始安装任务: {task.Name}");
        await Task.Delay(300);
        task.Status = "已完成";
        task.Progress = 100;
        Status = $"任务完成: {task.Name}";
        TaskStateChanged?.Invoke(task);
        SaveTaskState();
        EmitLog($"任务完成: {task.Name}");
    }

    private async Task ExecuteRealDownloadTaskAsync(DownloadTaskItem task)
    {
        task.CanRetry = false;
        task.CanCancel = true;
        task.Status = "下载中";
        task.Progress = 0;
        Status = $"正在下载: {task.Name}";
        TaskStateChanged?.Invoke(task);
        EmitLog($"开始真实下载: {task.SourceUrl}");

        var cts = new CancellationTokenSource();
        _runningTaskCancellationSources[task] = cts;

        try
        {
            await _httpDownloadService.DownloadAsync(
                task.SourceUrl,
                task.OutputFilePath,
                snapshot =>
                {
                    task.Progress = (int)Math.Round(snapshot.Percent);
                    var downloadedMb = snapshot.DownloadedBytes / 1024d / 1024d;
                    var totalMb = snapshot.TotalBytes / 1024d / 1024d;
                    var speedMb = snapshot.BytesPerSecond / 1024d / 1024d;

                    task.Status = snapshot.TotalBytes > 0
                        ? $"下载中 {snapshot.Percent:F1}% ({downloadedMb:F1}/{totalMb:F1} MB, {speedMb:F1} MB/s)"
                        : $"下载中 ({downloadedMb:F1} MB, {speedMb:F1} MB/s)";

                    TaskStateChanged?.Invoke(task);
                },
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            task.Status = "已取消";
            task.CanCancel = false;
            task.CanRetry = true;
            Status = $"任务已取消: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"任务取消: {task.Name}");
            return;
        }
        catch (Exception ex)
        {
            task.Status = "下载失败（可重试）";
            task.CanRetry = true;
            task.CanCancel = false;
            Status = $"任务失败: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"真实下载失败: {task.Name}, 错误: {ex.Message}");
            return;
        }
        finally
        {
            cts.Dispose();
            _runningTaskCancellationSources.Remove(task);
        }

        task.Progress = 100;
        task.CanCancel = false;

        if (task.TaskAction == DownloadTaskAction.SaveOnly)
        {
            task.InstalledPath = task.OutputFilePath;
            task.Status = "已完成（另存为）";
            TaskStateChanged?.Invoke(task);
            Status = $"另存为完成: {task.Name}";
            SaveTaskState();
            EmitLog($"另存为完成: {task.OutputFilePath}");
            return;
        }

        if (task.TaskAction == DownloadTaskAction.InstallSmapi)
        {
            task.Status = "安装中（SMAPI）";
            TaskStateChanged?.Invoke(task);
            EmitLog($"下载完成，开始安装 SMAPI: {task.OutputFilePath}");

            var smapiResult = await _smapiInstallService.InstallFromZipAsync(
                task.OutputFilePath,
                task.TargetGamePath,
                task.TargetInstanceName);
            if (!smapiResult.IsSuccess)
            {
                task.Status = smapiResult.IsCancelled ? "安装已取消" : "安装失败（可重试）";
                task.CanRetry = true;
                Status = $"任务失败: {task.Name}";
                TaskStateChanged?.Invoke(task);
                SaveTaskState();
                EmitLog($"SMAPI 安装失败: {task.Name}, 错误: {smapiResult.Message}");
                return;
            }

            task.InstalledPath = smapiResult.RuntimePath;
            task.Status = "已完成（SMAPI）";
            TaskStateChanged?.Invoke(task);
            Status = $"SMAPI 安装完成: {task.TargetInstanceName}";

            var settings = _settingsStore.Load();
            settings.PreferredInstancePath = smapiResult.RuntimePath;
            settings.InstanceName = task.TargetInstanceName;
            settings.PreferredLaunchMode = "SMAPI";
            _settingsStore.Save(settings);
            RefreshGamePathState();

            SaveTaskState();
            EmitLog($"SMAPI 安装完成: 实例={task.TargetInstanceName}, 路径={task.InstalledPath}");
            return;
        }

        task.Status = "安装中";
        TaskStateChanged?.Invoke(task);
        EmitLog($"下载完成，进入安装阶段: {task.OutputFilePath}");

        var installResult = await _downloadInstallService.InstallAsync(task.OutputFilePath, task.Name);
        if (!installResult.IsSuccess)
        {
            task.Status = installResult.IsCancelled ? "安装已取消" : "安装失败（可重试）";
            task.CanRetry = true;
            Status = $"任务失败: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"安装失败: {task.Name}, 错误: {installResult.Message}");
            return;
        }

        task.InstalledPath = installResult.InstalledPath;
        task.Status = "已完成";
        TaskStateChanged?.Invoke(task);
        Status = $"任务完成: {task.Name}";
        SaveTaskState();
        EmitLog($"任务完成: {task.Name}，安装目录: {task.InstalledPath}");
    }

    private async Task ExecuteCollectionTaskAsync(DownloadTaskItem task)
    {
        task.CanRetry = false;
        task.CanCancel = false;
        task.Progress = 0;
        task.FailedDetails = string.Empty;

        task.Status = "获取 Collection 清单";
        Status = $"正在获取清单: {task.Name}";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 获取清单: {task.Name}");
        await Task.Delay(300);
        task.Progress = 15;
        TaskStateChanged?.Invoke(task);

        task.Status = "解析 Collection 依赖";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 解析依赖: {task.Name}");
        await Task.Delay(350);
        task.Progress = 35;
        TaskStateChanged?.Invoke(task);

        task.Status = "下载 Collection 资源包";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 下载资源: {task.Name}");
        for (var progress = 35; progress <= 80; progress += 15)
        {
            task.Progress = progress;
            TaskStateChanged?.Invoke(task);
            await Task.Delay(250);
        }

        task.Status = "安装 Collection 条目";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 安装条目: {task.Name}");
        await Task.Delay(350);
        task.Progress = 95;
        TaskStateChanged?.Invoke(task);

        task.Status = "Collection 安装完成";
        task.Progress = 100;
        Status = $"Collection 任务完成: {task.Name}";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 任务完成: {task.Name}");
    }

    private async Task ExecuteCollectionRealDownloadTaskAsync(DownloadTaskItem task)
    {
        task.CanRetry = false;
        task.CanCancel = false;
        task.Progress = 0;

        task.Status = "获取 Collection 清单";
        Status = $"正在获取 Collection 清单: {task.Name}";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 获取清单: {task.Name}");
        await Task.Delay(200);
        task.Progress = 10;
        TaskStateChanged?.Invoke(task);

        task.Status = "解析 Collection 资源";
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 解析资源: {task.Name}");
        await Task.Delay(200);
        task.Progress = 20;
        TaskStateChanged?.Invoke(task);

        task.CanCancel = true;
        task.Status = "下载 Collection 资源包";
        Status = $"正在下载 Collection: {task.Name}";
        TaskStateChanged?.Invoke(task);
        var allUrls = BuildCollectionDownloadUrls(task);
        var retryFailedBefore = task.FailedDownloadUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        task.FailedDownloadUrls.Clear();
        EmitLog($"Collection 开始真实下载，共 {allUrls.Count} 个文件");

        var cts = new CancellationTokenSource();
        _runningTaskCancellationSources[task] = cts;
        var downloadedFiles = new List<string>();
        try
        {
            var settings = _settingsStore.Load();
            var configuredParallel = Math.Clamp(settings.CollectionDownloadParallelism, 1, 8);
            var targetParallel = Math.Min(configuredParallel, allUrls.Count);
            var currentParallel = retryFailedBefore.Count > 0
                ? Math.Max(1, targetParallel / 2)
                : targetParallel;

            EmitLog($"Collection 下载并发上限: {targetParallel}");
            if (currentParallel < targetParallel)
            {
                EmitLog($"检测到失败重试场景，初始并发自动降级为: {currentParallel}");
            }

            var fileProgress = new double[allUrls.Count];
            var progressLock = new object();
            var failures = new List<string>();
            var failedUrls = new List<string>();

            var cursor = 0;
            while (cursor < allUrls.Count)
            {
                var batch = allUrls
                    .Select((url, index) => (url, index))
                    .Skip(cursor)
                    .Take(currentParallel)
                    .ToList();

                var failedInBatch = 0;
                var downloadTasks = batch.Select(item => Task.Run(async () =>
                {
                    var url = item.url;
                    var index = item.index;

                    try
                    {
                        var target = ResolveCollectionPartPath(task, url, index + 1);
                        lock (downloadedFiles)
                        {
                            downloadedFiles.Add(target);
                        }

                        await _httpDownloadService.DownloadAsync(
                            url,
                            target,
                            snapshot =>
                            {
                                lock (progressLock)
                                {
                                    fileProgress[index] = Math.Clamp(snapshot.Percent, 0, 100);
                                    var overallPercent = fileProgress.Average();
                                    var mapped = 20 + overallPercent * 0.65;
                                    task.Progress = (int)Math.Round(Math.Min(85, mapped));

                                    var downloadedMb = snapshot.DownloadedBytes / 1024d / 1024d;
                                    var totalMb = snapshot.TotalBytes / 1024d / 1024d;
                                    var speedMb = snapshot.BytesPerSecond / 1024d / 1024d;

                                    task.Status = snapshot.TotalBytes > 0
                                        ? $"并发下载 {index + 1}/{allUrls.Count} {snapshot.Percent:F1}% ({downloadedMb:F1}/{totalMb:F1} MB, {speedMb:F1} MB/s)"
                                        : $"并发下载 {index + 1}/{allUrls.Count} ({downloadedMb:F1} MB, {speedMb:F1} MB/s)";

                                    TaskStateChanged?.Invoke(task);
                                }
                            },
                            cts.Token);

                        EmitLog($"Collection 文件下载完成: {index + 1}/{allUrls.Count}");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lock (failures)
                        {
                            failures.Add($"{index + 1}/{allUrls.Count}: {ex.Message}");
                            failedUrls.Add(url);
                            failedInBatch++;
                        }
                    }
                }, cts.Token)).ToList();

                await Task.WhenAll(downloadTasks);

                var batchFailureRate = batch.Count == 0
                    ? 0
                    : failedInBatch / (double)batch.Count;

                if (batchFailureRate >= 0.34 && currentParallel > 1)
                {
                    currentParallel = Math.Max(1, currentParallel - 1);
                    EmitLog($"失败率 {batchFailureRate:P0}，自动降低并发至 {currentParallel}");
                }
                else if (failedInBatch == 0 && currentParallel < targetParallel)
                {
                    currentParallel = Math.Min(targetParallel, currentParallel + 1);
                    EmitLog($"批次稳定，自动恢复并发至 {currentParallel}");
                }

                cursor += batch.Count;
            }

            if (failures.Count > 0)
            {
                task.FailedDownloadUrls = failedUrls
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var failedPreview = task.FailedDownloadUrls
                    .Take(5)
                    .Select((url, idx) => $"{idx + 1}. {url}");
                var omittedText = task.FailedDownloadUrls.Count > 5
                    ? $"\n... 其余 {task.FailedDownloadUrls.Count - 5} 项已省略"
                    : string.Empty;
                task.FailedDetails = $"失败资源 {task.FailedDownloadUrls.Count} 项:\n{string.Join("\n", failedPreview)}{omittedText}";

                var reason = string.Join("; ", failures.Take(3));
                throw new Exception($"部分文件下载失败: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            task.Status = "已取消";
            task.CanCancel = false;
            task.CanRetry = true;
            Status = $"任务已取消: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"Collection 任务取消: {task.Name}");
            return;
        }
        catch (Exception ex)
        {
            var retryReport = _retryDiffReportService.Write(_downloadRootPath, task.Name, retryFailedBefore, task.FailedDownloadUrls);
            if (!string.IsNullOrWhiteSpace(retryReport))
            {
                task.RetryReportPath = retryReport;
                EmitLog($"重试对比报告: {retryReport}");
            }

            task.Status = "下载失败（可重试）";
            task.CanRetry = true;
            task.CanCancel = false;
            Status = $"任务失败: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"Collection 下载失败: {task.Name}, 错误: {ex.Message}");

            var action = await _dialogService.ShowModpackFailureDialogAsync(
                ex.Message,
                task.RetryReportPath ?? string.Empty,
                "Collection 下载失败");
            if (action == ModpackFailureDialogAction.Retry)
            {
                RetryTask(task);
            }

            return;
        }
        finally
        {
            cts.Dispose();
            _runningTaskCancellationSources.Remove(task);
        }

        task.CanCancel = false;
        task.Status = "安装 Collection 条目";
        task.Progress = 90;
        TaskStateChanged?.Invoke(task);
        EmitLog($"Collection 安装开始，文件数: {downloadedFiles.Count}");

        var settingsForConflict = _settingsStore.Load();
        var conflictStrategy = CollectionInstallConflictStrategyExtensions.Parse(settingsForConflict.CollectionInstallConflictStrategy);

        var strategyOverride = await _dialogService.ShowInputAsync(
            "安装策略（仅本次）",
            "可输入覆盖/跳过/仅备份以临时覆盖本次策略；留空则沿用当前设置",
            conflictStrategy.ToDisplayName());
        if (!string.IsNullOrWhiteSpace(strategyOverride))
        {
            var parsedOverride = CollectionInstallConflictStrategyExtensions.Parse(strategyOverride.Trim());
            if (parsedOverride != conflictStrategy)
            {
                conflictStrategy = parsedOverride;
                EmitLog($"本次安装策略已临时覆盖为: {conflictStrategy.ToDisplayName()}");
            }
        }

        EmitLog($"Collection 冲突策略: {conflictStrategy.ToDisplayName()}");

        var previewItems = await _downloadInstallService.PreviewCollectionConflictsAsync(downloadedFiles, conflictStrategy);
        task.ConflictPreviewItems = previewItems
            .Select(item => $"{item.ModName} => {item.PlannedAction}")
            .ToList();
        TaskStateChanged?.Invoke(task);

        if (previewItems.Count == 0)
        {
            EmitLog("Collection 冲突预览: 未识别到可安装 Mod 条目");
        }
        else
        {
            EmitLog($"Collection 冲突预览: 共 {previewItems.Count} 个条目");
            foreach (var item in previewItems.Take(12))
            {
                EmitLog($"冲突预览: {item.ModName} => {item.PlannedAction}");
            }

            if (previewItems.Count > 12)
            {
                EmitLog($"冲突预览: 其余 {previewItems.Count - 12} 个条目已省略");
            }
        }

        var previewSummary = BuildInstallPreviewSummary(previewItems, conflictStrategy);
        var userConfirmed = await _dialogService.ShowConfirmAsync(
            "安装前确认",
            $"检测到 {previewItems.Count} 个安装条目，是否继续？\n\n{previewSummary}");
        if (!userConfirmed)
        {
            task.Status = "安装已取消";
            task.CanRetry = true;
            task.CanCancel = false;
            Status = $"任务已取消: {task.Name}";
            TaskStateChanged?.Invoke(task);
            EmitLog("用户取消了 Collection 安装");
            SaveTaskState();
            return;
        }

        var installResult = await _downloadInstallService.InstallCollectionAsync(
            downloadedFiles,
            task.Name,
            conflictStrategy,
            previewItems);
        if (!installResult.IsSuccess)
        {
            task.Status = installResult.IsCancelled ? "安装已取消" : "安装失败（可重试）";
            task.CanRetry = true;
            Status = $"任务失败: {task.Name}";
            TaskStateChanged?.Invoke(task);
            SaveTaskState();
            EmitLog($"Collection 安装失败: {task.Name}, 错误: {installResult.Message}");

            var action = await _dialogService.ShowModpackFailureDialogAsync(
                installResult.Message,
                installResult.ReportPath ?? task.ReportPath ?? task.RetryReportPath ?? string.Empty,
                "Collection 安装失败");
            if (action == ModpackFailureDialogAction.Retry)
            {
                RetryTask(task);
            }

            return;
        }

        task.InstalledPath = installResult.InstalledPath;
        task.FailedDownloadUrls.Clear();
        task.FailedDetails = string.Empty;
        task.ReportPath = installResult.ReportPath;
        task.BackupPath = installResult.BackupPath;
        task.Status = "Collection 安装完成";
        task.Progress = 100;
        Status = $"Collection 任务完成: {task.Name}";
        TaskStateChanged?.Invoke(task);
        SaveTaskState();
        var installedListText = installResult.InstalledItems.Count == 0
            ? "无可识别 Mod 条目"
            : string.Join(", ", installResult.InstalledItems.Take(8));

        EmitLog($"Collection 任务完成: {task.Name}，安装目录: {task.InstalledPath}");
        EmitLog($"Collection 安装条目: {installedListText}");
        if (!string.IsNullOrWhiteSpace(installResult.BackupPath))
        {
            EmitLog($"Collection 冲突备份目录: {installResult.BackupPath}");
        }

        EmitLog($"Collection 安装校验: {(installResult.ValidationPassed ? "通过" : "存在问题")}");
        if (!installResult.ValidationPassed)
        {
            foreach (var error in installResult.ValidationErrors.Take(10))
            {
                EmitLog($"Collection 校验问题: {error}");
            }
        }

        if (!string.IsNullOrWhiteSpace(installResult.ReportPath))
        {
            EmitLog($"Collection 安装报告: {installResult.ReportPath}");
        }

        var retrySuccessReport = _retryDiffReportService.Write(_downloadRootPath, task.Name, retryFailedBefore, task.FailedDownloadUrls);
        if (!string.IsNullOrWhiteSpace(retrySuccessReport))
        {
            task.RetryReportPath = retrySuccessReport;
            EmitLog($"重试对比报告: {retrySuccessReport}");
        }
    }

    private static List<string> BuildCollectionDownloadUrls(DownloadTaskItem task)
    {
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.SourceUrl))
        {
            urls.Add(task.SourceUrl);
        }

        foreach (var url in task.DependencyUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (urls.Any(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            urls.Add(url);
        }

        return urls;
    }

    private string ResolveCollectionPartPath(DownloadTaskItem task, string url, int index)
    {
        var fallbackName = $"collection-part-{index}.bin";
        var fileName = fallbackName;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            fileName = ResolveDownloadFileName(uri, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fallbackName;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var finalName = $"{CreateSafeFileName(task.Name)}-part-{index}-{baseName}{ext}";
        return Path.Combine(_downloadRootPath, finalName);
    }

    private static string CreateSafeFileName(string name)
    {
        var fallback = "collection";
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var cleaned = string.Concat(name.Trim().Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private void EmitLog(string message)
    {
        TaskLogGenerated?.Invoke(message);
    }

    private void TryOpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status = $"打开路径失败: {ex.Message}";
        }
    }

    private static bool HasRealDownloadSource(DownloadTaskItem task)
    {
        return !string.IsNullOrWhiteSpace(task.SourceUrl) &&
               !string.IsNullOrWhiteSpace(task.OutputFilePath);
    }

    private void RefreshGamePathState()
    {
        var settings = _settingsStore.Load();
        var preferred = settings.PreferredInstancePath?.Trim();

        var gamePath = !string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred)
            ? preferred
            : _gameInstallPathLocator.TryLocateSteamStardewPath() ?? _gameInstallPathLocator.TryLocateGogStardewPath();

        var hasValidPath = !string.IsNullOrWhiteSpace(gamePath) && Directory.Exists(gamePath);
        ShowGamePathWarning = !hasValidPath;
        GamePathHint = hasValidPath ? gamePath! : "未探测到游戏目录";
    }

    private async Task<bool> EnsureGamePathConfiguredAsync()
    {
        RefreshGamePathState();
        if (!ShowGamePathWarning)
        {
            return true;
        }

        var selectedPath = await _dialogService.ShowGamePathSelectionDialogAsync(
            string.Empty,
            "选择游戏路径",
            "下载与安装需要有效的 Stardew Valley 目录。请先选择游戏目录。");

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return false;
        }

        var normalized = selectedPath.Trim();
        if (!TryNormalizeGameRootPath(normalized, out var gameRootPath))
        {
            Status = "选择的目录无效：未检测到游戏核心文件";
            return false;
        }

        var confirmed = await _dialogService.ShowGamePathConfirmDialogAsync(
            gameRootPath,
            "确认游戏路径",
            "请确认此目录为 Stardew Valley 安装目录。确认后将作为下载与安装目标路径。");

        if (!confirmed)
        {
            return false;
        }

        var settings = _settingsStore.Load();
        settings.PreferredInstancePath = gameRootPath;
        if (string.IsNullOrWhiteSpace(settings.InstanceName))
        {
            settings.InstanceName = Path.GetFileName(gameRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        _settingsStore.Save(settings);
        RefreshGamePathState();
        return !ShowGamePathWarning;
    }

    private static string BuildNexusWebUrl(NxmLinkInfo parsed)
    {
        return parsed.ResourceType == NxmResourceType.Collection
            ? $"https://next.nexusmods.com/stardewvalley/collections/{parsed.CollectionSlug}"
            : $"https://www.nexusmods.com/stardewvalley/mods/{parsed.ModId}";
    }

    private static bool TryNormalizeGameRootPath(string inputPath, out string gameRoot)
    {
        gameRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
        {
            return false;
        }

        var candidates = new[]
        {
            inputPath,
            Path.Combine(inputPath, "Stardew Valley.app", "Contents", "MacOS"),
            Path.Combine(inputPath, "Stardew Valley")
        };

        foreach (var candidate in candidates.Where(Directory.Exists))
        {
            var markers = new[]
            {
                "Stardew Valley.dll",
                "Stardew Valley.deps.json",
                "Stardew Valley.exe",
                "StardewValley.exe",
                "StardewValley",
                "StardewModdingAPI.exe",
                "StardewModdingAPI"
            };

            if (markers.Any(marker => File.Exists(Path.Combine(candidate, marker))))
            {
                gameRoot = candidate;
                return true;
            }
        }

        return false;
    }

    private static string BuildInstallPreviewSummary(
        IReadOnlyList<CollectionConflictPreviewItem> previewItems,
        CollectionInstallConflictStrategy conflictStrategy)
    {
        if (previewItems.Count == 0)
        {
            return "未识别到可安装条目，仍将继续安装原始内容。";
        }

        var visible = previewItems.Take(12).ToList();
        var lines = new List<string>
        {
            $"当前策略: {conflictStrategy.ToDisplayName()}",
            $"预览条目: {previewItems.Count}（展示前 {visible.Count} 项）",
            string.Empty
        };

        lines.AddRange(visible.Select((item, idx) => $"{idx + 1}. {item.ModName} -> {item.PlannedAction}"));
        if (previewItems.Count > visible.Count)
        {
            lines.Add($"... 其余 {previewItems.Count - visible.Count} 项已省略");
        }

        return string.Join("\n", lines);
    }

    private static string ResolveDownloadFileName(Uri uri, string manualName)
    {
        if (!string.IsNullOrWhiteSpace(manualName))
        {
            var cleaned = string.Concat(manualName.Trim().Split(Path.GetInvalidFileNameChars()));
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        var pathName = Path.GetFileName(uri.LocalPath);
        if (!string.IsNullOrWhiteSpace(pathName))
        {
            return pathName;
        }

        return $"download-{DateTime.Now:yyyyMMddHHmmss}.bin";
    }

    private static global::Avalonia.Input.Platform.IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }

    private void SaveTaskState()
    {
        try
        {
            _taskStateStore.Save(_taskStatePath, DownloadTasks.ToList());
        }
        catch
        {
            // Keep persistence as best-effort to avoid breaking download workflow.
        }
    }

    private void TryLoadTaskState()
    {
        try
        {
            var records = _taskStateStore.Load(_taskStatePath, out var brokenPath);
            if (!string.IsNullOrWhiteSpace(brokenPath))
            {
                EmitLog($"检测到损坏任务状态文件，已备份到: {brokenPath}");
            }

            if (records == null || records.Count == 0)
            {
                return;
            }

            var filteredRecords = records
                .Where(record => !IsSmokeTestTaskRecord(record))
                .ToList();

            if (filteredRecords.Count != records.Count)
            {
                EmitLog($"已过滤 {records.Count - filteredRecords.Count} 条测试任务记录");
            }

            if (filteredRecords.Count == 0)
            {
                return;
            }

            DownloadTasks.Clear();
            foreach (var record in filteredRecords)
            {
                DownloadTasks.Add(new DownloadTaskItem
                {
                    Name = record.Name,
                    Status = record.Status,
                    Progress = record.Progress,
                    CanRetry = record.CanRetry,
                    CanCancel = record.CanCancel,
                    TaskKind = record.TaskKind,
                    TaskAction = record.TaskAction,
                    SourceUrl = record.SourceUrl,
                    OutputFilePath = record.OutputFilePath,
                    InstalledPath = record.InstalledPath,
                    ReportPath = record.ReportPath,
                    BackupPath = record.BackupPath,
                    FailedDetails = record.FailedDetails,
                    RetryReportPath = record.RetryReportPath,
                    TargetGamePath = record.TargetGamePath,
                    TargetInstanceName = record.TargetInstanceName,
                    StatusIconSource = string.Empty,
                    DependencyUrls = record.DependencyUrls,
                    FailedDownloadUrls = record.FailedDownloadUrls,
                    ConflictPreviewItems = record.ConflictPreviewItems
                });

                NormalizeRecoveredTaskState(DownloadTasks[^1]);
                DownloadTasks[^1].StatusIconSource = ResolveTaskStatusIcon(DownloadTasks[^1]);
            }
        }
        catch
        {
            // Ignore broken persisted state and keep in-memory defaults.
        }
    }

    private static bool IsSmokeTestTaskRecord(DownloadTaskStateRecord record)
    {
        if (record == null)
        {
            return false;
        }

        var hasSmokeName = !string.IsNullOrWhiteSpace(record.Name) &&
                           record.Name.Contains("smoke", StringComparison.OrdinalIgnoreCase);
        var hasSmokeSource = !string.IsNullOrWhiteSpace(record.SourceUrl) &&
                             record.SourceUrl.Contains("smoke", StringComparison.OrdinalIgnoreCase);
        var hasSmokeOutputPath = !string.IsNullOrWhiteSpace(record.OutputFilePath) &&
                                 record.OutputFilePath.Contains("svl-smoke-instance", StringComparison.OrdinalIgnoreCase);

        return hasSmokeName || hasSmokeSource || hasSmokeOutputPath;
    }

    private static void NormalizeRecoveredTaskState(DownloadTaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.Status))
        {
            task.Status = "等待下载";
            task.CanRetry = false;
            task.CanCancel = false;
            return;
        }

        if (task.Status.Contains("下载中", StringComparison.Ordinal) ||
            task.Status.Contains("安装中", StringComparison.Ordinal) ||
            task.Status.Contains("并发下载", StringComparison.Ordinal) ||
            task.Status.Contains("获取 Collection", StringComparison.Ordinal) ||
            task.Status.Contains("解析 Collection", StringComparison.Ordinal))
        {
            task.Status = "上次运行中断（可重试）";
            task.CanRetry = true;
            task.CanCancel = false;
        }
    }

}

