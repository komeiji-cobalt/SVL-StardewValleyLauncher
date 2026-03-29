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

namespace SVL.Avalonia.ViewModels;

public partial class DownloadPageViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private readonly ImageResourceService _imageResourceService;
    private readonly INxmLinkParser _nxmLinkParser;
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly AppUserSettingsStore _settingsStore;
    private readonly HttpDownloadService _httpDownloadService;
    private readonly NexusModDownloadResolverService _nexusModDownloadResolverService;
    private readonly DownloadInstallService _downloadInstallService;
    private readonly RemoteCatalogService _remoteCatalogService;
    private readonly DownloadTaskStateStore _taskStateStore;
    private readonly RetryDiffReportService _retryDiffReportService;
    private readonly DialogService _dialogService;
    private readonly string _downloadRootPath;
    private readonly string _taskStatePath;
    private readonly Dictionary<DownloadTaskItem, CancellationTokenSource> _runningTaskCancellationSources = [];
    private readonly SemaphoreSlim _queueWorkerGate = new(1, 1);
    private bool _isQueueWorkerRunning;

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

    public ObservableCollection<string> SmapiSources { get; } = ["全部", "Curseforge", "NexusMods"];

    public ObservableCollection<string> ModSources { get; } = ["全部", "Curseforge", "NexusMods"];

    public ObservableCollection<DownloadTaskItem> DownloadTasks { get; } = [];

    public ObservableCollection<DownloadTaskItem> ActiveTasks { get; } = [];

    public ObservableCollection<DownloadTaskItem> FinishedTasks { get; } = [];

    public ObservableCollection<string> SearchResults { get; } = [];

    public bool IsSmapiCategory => SelectedCategory == DownloadCategory.Smapi;

    public bool IsModsCategory => SelectedCategory == DownloadCategory.Mods;

    public bool IsModpacksCategory => SelectedCategory == DownloadCategory.Modpacks;

    public bool HasActiveTasks => ActiveTasks.Count > 0;

    public bool HasFinishedTasks => FinishedTasks.Count > 0;

    public bool HasNoActiveTasks => !HasActiveTasks;

    public bool HasNoFinishedTasks => !HasFinishedTasks;

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
        _remoteCatalogService = remoteCatalogService;
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

        Directory.CreateDirectory(_downloadRootPath);
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

        OnPropertyChanged(nameof(IsSmapiCategory));
        OnPropertyChanged(nameof(IsModsCategory));
        OnPropertyChanged(nameof(IsModpacksCategory));
    }

    [RelayCommand]
    private void SelectCategory(DownloadCategory category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task SearchSmapi()
    {
        SearchResults.Clear();

        if (string.IsNullOrWhiteSpace(SmapiSearchText))
        {
            Status = "请输入关键词";
            return;
        }

        try
        {
            Status = $"正在搜索 SMAPI: {SmapiSearchText}";
            var source = string.Equals(SelectedSmapiSource, "GitHub", StringComparison.Ordinal)
                ? "全部"
                : SelectedSmapiSource;
            var results = await _remoteCatalogService.SearchModsAsync($"SMAPI {SmapiSearchText}", source);
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            Status = SearchResults.Count == 0
                ? "未找到匹配结果"
                : $"已找到 {SearchResults.Count} 条结果";
        }
        catch (Exception ex)
        {
            Status = $"搜索失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SearchMods()
    {
        SearchResults.Clear();

        if (string.IsNullOrWhiteSpace(ModSearchText))
        {
            Status = "请输入 Mod 关键词";
            return;
        }

        try
        {
            Status = $"正在搜索 Mod: {ModSearchText}";
            var results = await _remoteCatalogService.SearchModsAsync(ModSearchText, SelectedModSource);
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            Status = SearchResults.Count == 0
                ? "未找到匹配结果"
                : $"已找到 {SearchResults.Count} 条 Mod 结果";
        }
        catch (Exception ex)
        {
            Status = $"搜索失败: {ex.Message}";
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
        if (request == null || string.IsNullOrWhiteSpace(request.ResourceName))
        {
            return;
        }

        var taskName = request.ToTaskDisplayName();

        DownloadTasks.Insert(0, new DownloadTaskItem
        {
            Name = taskName,
            Status = "已加入队列",
            Progress = 0,
            TaskKind = DownloadTaskKind.Generic,
            SourceUrl = request.SelectedDownloadOption,
            CanCancel = false,
            CanRetry = false
        });
        DownloadTasks[0].StatusIconSource = ResolveTaskStatusIcon(DownloadTasks[0]);

        Status = $"已加入下载队列: {taskName}";
        SaveTaskState();
        _ = ProcessQueueAsync();
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

            DownloadTasks.Clear();
            foreach (var record in records)
            {
                DownloadTasks.Add(new DownloadTaskItem
                {
                    Name = record.Name,
                    Status = record.Status,
                    Progress = record.Progress,
                    CanRetry = record.CanRetry,
                    CanCancel = record.CanCancel,
                    TaskKind = record.TaskKind,
                    SourceUrl = record.SourceUrl,
                    OutputFilePath = record.OutputFilePath,
                    InstalledPath = record.InstalledPath,
                    ReportPath = record.ReportPath,
                    BackupPath = record.BackupPath,
                    FailedDetails = record.FailedDetails,
                    RetryReportPath = record.RetryReportPath,
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

