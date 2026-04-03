using SVL.Avalonia.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SVL.Avalonia.Services;

public sealed class RemoteCatalogService
{
    // Follow legacy desktop implementation: Stardew Valley gameId on curse.tools is 669.
    private const int StardewCurseforgeGameId = 669;
    private const string NexusGameDomain = "stardewvalley";
    private readonly AppUserSettingsStore _settingsStore;
    private readonly Dictionary<string, CatalogResourceIdentity> _resourceIdentityMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object HttpClientLock = new();
    private static HttpClient? _httpClient;
    private static string _httpClientProxySignature = string.Empty;

    public Action<string>? DebugLogger { get; set; }

    public RemoteCatalogService(AppUserSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    private HttpClient GetHttpClient(AppUserSettings? settings = null)
    {
        settings ??= _settingsStore.Load();
        var signature = BuildProxySignature(settings);

        lock (HttpClientLock)
        {
            if (_httpClient != null && string.Equals(signature, _httpClientProxySignature, StringComparison.Ordinal))
            {
                return _httpClient;
            }

            _httpClient?.Dispose();
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (settings.EnableDownloadProxy &&
                TryResolveProxyUri(settings.DownloadProxyUrl, out var proxyUri))
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

            _httpClient = new HttpClient(handler, disposeHandler: true);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SVL-Avalonia-Migration");
            _httpClientProxySignature = signature;
            return _httpClient;
        }
    }

    private static string BuildProxySignature(AppUserSettings settings)
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

    private static bool TryResolveProxyUri(string? rawProxyUrl, out Uri proxyUri)
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

    public async Task<List<string>> SearchModsAsync(string keyword, string source = "全部")
    {
        var settings = _settingsStore.Load();
        var includeNexus = string.Equals(source, "全部", StringComparison.Ordinal) ||
                           string.Equals(source, "NexusMods", StringComparison.Ordinal);
        var includeCurseforge = string.Equals(source, "全部", StringComparison.Ordinal) ||
                                string.Equals(source, "Curseforge", StringComparison.Ordinal);

        var results = new List<string>();

        if (includeNexus)
        {
            var nexusResults = await SearchNexusModsAsync(keyword, settings);
            results.AddRange(nexusResults.Select(item =>
            {
                var text = FormatResult($"NexusMod#{item.ResourceId}", item.Name, item.Stat, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.NexusMods, false);
                return text;
            }));
        }

        if (includeCurseforge)
        {
            var curseforgeResults = await SearchCurseforgeModsAsync(keyword);
            results.AddRange(curseforgeResults.Select(item =>
            {
                var text = FormatResult($"CurseforgeMod#{item.ResourceId}", item.Name, item.Stat, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.Curseforge, false);
                return text;
            }));
        }

        return Deduplicate(results);
    }

    public async Task<List<string>> SearchModsAdvancedAsync(
        string keyword,
        string source = "全部",
        string gameVersion = "全部",
        string modType = "全部",
        bool useCommunityLocalization = true,
        bool hotOnly = false)
    {
        var settings = _settingsStore.Load();
        var includeNexus = string.Equals(source, "全部", StringComparison.Ordinal) ||
                           string.Equals(source, "NexusMods", StringComparison.Ordinal);
        var includeCurseforge = string.Equals(source, "全部", StringComparison.Ordinal) ||
                                string.Equals(source, "Curseforge", StringComparison.Ordinal);

        var normalizedKeyword = hotOnly ? string.Empty : keyword?.Trim() ?? string.Empty;
        var normalizedVersionFilter = NormalizeFilterToken(gameVersion);
        var normalizedModTypeFilter = NormalizeFilterToken(modType);
        var fetchCount = hotOnly ? 40 : 20;

        LogDebug($"SearchModsAdvanced/start source={source}, hotOnly={hotOnly}, keyword='{normalizedKeyword}', version='{normalizedVersionFilter}', type='{normalizedModTypeFilter}', localization={useCommunityLocalization}, pageSize={fetchCount}");

        var results = new List<string>();

        if (includeNexus)
        {
            var nexusItems = await SearchNexusModsAsync(normalizedKeyword, settings, fetchCount);
            LogDebug($"SearchModsAdvanced/nexus raw={nexusItems.Count}");
            if (!string.IsNullOrWhiteSpace(normalizedModTypeFilter))
            {
                nexusItems = nexusItems
                    .Where(item => MatchesModTypeFilter(item.ModType, normalizedModTypeFilter))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(normalizedVersionFilter))
            {
                nexusItems = nexusItems
                    .Where(item => MatchesGameVersionFilter(item.SupportedGameVersions, item.GameVersionTag, normalizedVersionFilter))
                    .ToList();
            }

            if (useCommunityLocalization)
            {
                await ApplyCommunityLocalizationAsync(nexusItems, "NexusMods");
            }

            LogDebug($"SearchModsAdvanced/nexus filtered={nexusItems.Count}");

            results.AddRange(nexusItems.Take(10).Select(item =>
            {
                var text = FormatModResult("NexusMod", item);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.NexusMods, false);
                return text;
            }));
        }

        if (includeCurseforge)
        {
            LogDebug($"SearchModsAdvanced/curse url={BuildCurseforgeSearchUrl(normalizedKeyword, fetchCount)}");
            var curseforgeItems = await SearchCurseforgeModsAsync(normalizedKeyword, fetchCount);
            LogDebug($"SearchModsAdvanced/curse raw={curseforgeItems.Count}");
            if (!string.IsNullOrWhiteSpace(normalizedModTypeFilter))
            {
                curseforgeItems = curseforgeItems
                    .Where(item => MatchesModTypeFilter(item.ModType, normalizedModTypeFilter))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(normalizedVersionFilter))
            {
                curseforgeItems = curseforgeItems
                    .Where(item => MatchesGameVersionFilter(item.SupportedGameVersions, item.GameVersionTag, normalizedVersionFilter))
                    .ToList();
            }

            if (useCommunityLocalization)
            {
                await ApplyCommunityLocalizationAsync(curseforgeItems, "Curseforge");
            }

            LogDebug($"SearchModsAdvanced/curse filtered={curseforgeItems.Count}");

            results.AddRange(curseforgeItems.Take(10).Select(item =>
            {
                var text = FormatModResult("CurseforgeMod", item);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.Curseforge, false);
                return text;
            }));
        }

        var deduplicated = Deduplicate(results);
        LogDebug($"SearchModsAdvanced/done total={deduplicated.Count}");
        return deduplicated;
    }

    public async Task<CatalogPagedResult> SearchModsAdvancedPagedAsync(
        string keyword,
        string source = "全部",
        string gameVersion = "全部",
        string modType = "全部",
        bool useCommunityLocalization = true,
        bool hotOnly = false,
        int page = 1,
        int pageSize = 10)
    {
        var settings = _settingsStore.Load();
        var includeNexus = string.Equals(source, "全部", StringComparison.Ordinal) ||
                           string.Equals(source, "NexusMods", StringComparison.Ordinal);
        var includeCurseforge = string.Equals(source, "全部", StringComparison.Ordinal) ||
                                string.Equals(source, "Curseforge", StringComparison.Ordinal);

        var normalizedKeyword = hotOnly ? string.Empty : keyword?.Trim() ?? string.Empty;
        var normalizedVersionFilter = NormalizeFilterToken(gameVersion);
        var normalizedModTypeFilter = NormalizeFilterToken(modType);
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 30);
        var includeBothSources = includeNexus && includeCurseforge;
        var perSourceFetchCount = safePageSize;
        var mergedPageSize = includeBothSources ? safePageSize * 2 : safePageSize;

        var nexusItems = new List<RemoteSearchItem>();
        var curseItems = new List<RemoteSearchItem>();
        var nexusHasMore = false;
        var curseHasMore = false;

        if (includeNexus)
        {
            var nexusRaw = await SearchNexusModsAsync(normalizedKeyword, settings, safePage * perSourceFetchCount);
            var nexusFiltered = nexusRaw
                .Where(item => MatchesModTypeFilter(item.ModType, normalizedModTypeFilter))
                .Where(item => MatchesGameVersionFilter(item.SupportedGameVersions, item.GameVersionTag, normalizedVersionFilter))
                .ToList();

            nexusItems = nexusFiltered
                .Skip((safePage - 1) * perSourceFetchCount)
                .Take(perSourceFetchCount)
                .ToList();
            foreach (var item in nexusItems)
            {
                item.Source = CatalogSource.NexusMods;
                item.SourceTagHint = "NexusMod";
            }
            if (useCommunityLocalization)
            {
                await ApplyCommunityLocalizationAsync(nexusItems, "NexusMods");
            }

            nexusHasMore = nexusFiltered.Count > safePage * perSourceFetchCount;
        }

        if (includeCurseforge)
        {
            var curseRaw = await SearchCurseforgeModsAsync(normalizedKeyword, safePage * perSourceFetchCount);
            var curseFiltered = curseRaw
                .Where(item => MatchesModTypeFilter(item.ModType, normalizedModTypeFilter))
                .Where(item => MatchesGameVersionFilter(item.SupportedGameVersions, item.GameVersionTag, normalizedVersionFilter))
                .ToList();

            curseItems = curseFiltered
                .Skip((safePage - 1) * perSourceFetchCount)
                .Take(perSourceFetchCount)
                .ToList();
            foreach (var item in curseItems)
            {
                item.Source = CatalogSource.Curseforge;
                item.SourceTagHint = "CurseforgeMod";
            }
            if (useCommunityLocalization)
            {
                await ApplyCommunityLocalizationAsync(curseItems, "Curseforge");
            }

            curseHasMore = curseFiltered.Count > safePage * perSourceFetchCount;
        }

        var merged = MergeBySourceAlternating(nexusItems, curseItems)
            .Take(mergedPageSize)
            .ToList();

        var formatted = merged.Select(item =>
        {
            var sourceTag = item.SourceTagHint;
            var text = FormatModResult(sourceTag, item);
            _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, item.Source, false);
            return text;
        }).ToList();

        return new CatalogPagedResult
        {
            Items = Deduplicate(formatted),
            HasMore = nexusHasMore || curseHasMore
        };
    }

    public async Task<CatalogPagedResult> SearchModpacksPagedAsync(
        string keyword,
        string source = "全部",
        int page = 1,
        int pageSize = 10)
    {
        var settings = _settingsStore.Load();
        var includeNexus = string.Equals(source, "全部", StringComparison.Ordinal) ||
                           string.Equals(source, "NexusMods", StringComparison.Ordinal);
        var includeCurseforge = string.Equals(source, "全部", StringComparison.Ordinal) ||
                                string.Equals(source, "Curseforge", StringComparison.Ordinal);

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 30);
        var includeBothSources = includeNexus && includeCurseforge;
        var perSourceFetchCount = safePageSize;
        var mergedPageSize = includeBothSources ? safePageSize * 2 : safePageSize;

        var nexusItems = new List<RemoteSearchItem>();
        var curseItems = new List<RemoteSearchItem>();
        var nexusHasMore = false;
        var curseHasMore = false;

        if (includeNexus)
        {
            var nexusRaw = await SearchNexusCollectionsAsync(keyword, settings);
            nexusHasMore = nexusRaw.Count > safePage * perSourceFetchCount;
            nexusItems = nexusRaw
                .Skip((safePage - 1) * perSourceFetchCount)
                .Take(perSourceFetchCount)
                .ToList();
            foreach (var item in nexusItems)
            {
                item.Source = CatalogSource.NexusMods;
                item.SourceTagHint = "NexusPack";
            }
            nexusHasMore = nexusItems.Count >= perSourceFetchCount;
        }
        if (includeCurseforge)
        {
            var curseRaw = await SearchCurseforgeModpacksAsync(keyword);
            curseHasMore = curseRaw.Count > safePage * perSourceFetchCount;
            curseItems = curseRaw
                .Skip((safePage - 1) * perSourceFetchCount)
                .Take(perSourceFetchCount)
                .ToList();
            foreach (var item in curseItems)
            {
                item.Source = CatalogSource.Curseforge;
                item.SourceTagHint = "CurseforgePack";
            }
        }

        var merged = MergeBySourceAlternating(nexusItems, curseItems)
            .Take(mergedPageSize)
            .ToList();

        var formatted = merged.Select(item =>
        {
            var prefix = item.Source == CatalogSource.NexusMods ? "NexusPack" : "CurseforgePack";
            var text = FormatResult($"{prefix}#{item.ResourceId}", item.Name, item.Stat, item.TimeTag, item.IconUrl, item.FullIconUrl, item.Summary);
            _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, item.Source, true);
            return text;
        }).ToList();

        return new CatalogPagedResult
        {
            Items = Deduplicate(formatted),
            HasMore = nexusHasMore || curseHasMore
        };
    }

    private static List<RemoteSearchItem> MergeBySourceAlternating(
        List<RemoteSearchItem> first,
        List<RemoteSearchItem> second)
    {
        if (first.Count == 0)
        {
            return second;
        }

        if (second.Count == 0)
        {
            return first;
        }

        var merged = new List<RemoteSearchItem>(first.Count + second.Count);
        var max = Math.Max(first.Count, second.Count);
        for (var i = 0; i < max; i++)
        {
            if (i < first.Count)
            {
                merged.Add(first[i]);
            }

            if (i < second.Count)
            {
                merged.Add(second[i]);
            }
        }

        return merged;
    }

    private void LogDebug(string message)
    {
        DebugLogger?.Invoke(message);
        Debug.WriteLine($"[RemoteCatalogService] {message}");
    }

    public async Task<List<string>> SearchSmapiAsync(string keyword, string source = "全部")
    {
        var settings = _settingsStore.Load();
        var normalizedKeyword = NormalizeSmapiKeyword(keyword);
        var searchKeyword = string.IsNullOrWhiteSpace(normalizedKeyword)
            ? "SMAPI"
            : $"SMAPI {normalizedKeyword}";

        var includeGithub = string.Equals(source, "全部", StringComparison.Ordinal) ||
                            string.Equals(source, "GitHub", StringComparison.OrdinalIgnoreCase);
        var includeNexus = string.Equals(source, "全部", StringComparison.Ordinal) ||
                           string.Equals(source, "NexusMods", StringComparison.Ordinal);
        var includeCurseforge = string.Equals(source, "全部", StringComparison.Ordinal) ||
                                string.Equals(source, "Curseforge", StringComparison.Ordinal);

        LogDebug($"SearchSmapi/start source={source}, keyword='{normalizedKeyword}', query='{searchKeyword}'");
        var results = new List<string>();

        if (includeGithub)
        {
            var githubResults = await SearchGithubSmapiReleasesAsync(normalizedKeyword);
            LogDebug($"SearchSmapi/github raw={githubResults.Count}");
            results.AddRange(githubResults.Select(item =>
            {
                var text = FormatResult($"GitHub#{item.ResourceId}", item.Name, item.Stat, item.TimeTag, item.IconUrl, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.GitHub, false);
                return text;
            }));
        }

        if (includeNexus)
        {
            var nexusResults = await SearchNexusModsAsync(searchKeyword, settings);
            LogDebug($"SearchSmapi/nexus raw={nexusResults.Count}");
            results.AddRange(nexusResults.Select(item =>
            {
                var text = FormatResult($"NexusMods#{item.ResourceId}", item.Name, item.Stat, item.TimeTag, item.IconUrl, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.NexusMods, false);
                return text;
            }));
        }

        if (includeCurseforge)
        {
            var curseforgeResults = await GetCurseforgeSmapiItemsAsync();
            LogDebug($"SearchSmapi/curse raw={curseforgeResults.Count}");
            results.AddRange(curseforgeResults.Select(item =>
            {
                var text = FormatResult($"Curseforge#{item.ResourceId}", item.Name, item.Stat, item.TimeTag, item.IconUrl, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.Curseforge, false);
                return text;
            }));
        }

        var deduplicated = Deduplicate(results);
        LogDebug($"SearchSmapi/done total={deduplicated.Count}");
        return deduplicated;
    }

    private async Task<List<RemoteSearchItem>> GetCurseforgeSmapiItemsAsync()
    {
        const long smapiCurseforgeProjectId = 898372;
        var smapi = await GetCurseforgeModByIdAsync(smapiCurseforgeProjectId);
        if (smapi == null)
        {
            return [];
        }

        var normalizedName = smapi.Name.Contains("SMAPI", StringComparison.OrdinalIgnoreCase)
            ? smapi.Name
            : "SMAPI - Stardew Modding API";
        var normalizedSummary = string.IsNullOrWhiteSpace(smapi.Summary)
            ? "Stardew Valley 的模组加载 API（必须先安装）"
            : smapi.Summary;

        return
        [
            new RemoteSearchItem
            {
                ResourceId = smapi.ResourceId,
                Name = normalizedName,
                Summary = normalizedSummary,
                Stat = smapi.Stat,
                TimeTag = smapi.TimeTag,
                IconUrl = smapi.IconUrl,
                ModType = smapi.ModType,
                GameVersionTag = smapi.GameVersionTag,
                SupportedGameVersions = smapi.SupportedGameVersions,
                LocalizedName = smapi.LocalizedName,
                LocalizedSummary = smapi.LocalizedSummary
            }
        ];
    }

    public async Task<List<string>> SearchModpacksAsync(string keyword, string source = "全部")
    {
        var settings = _settingsStore.Load();
        var includeNexus = string.Equals(source, "全部", StringComparison.Ordinal) ||
                           string.Equals(source, "NexusMods", StringComparison.Ordinal);
        var includeCurseforge = string.Equals(source, "全部", StringComparison.Ordinal) ||
                                string.Equals(source, "Curseforge", StringComparison.Ordinal);

        var results = new List<string>();

        if (includeNexus)
        {
            var nexusCollections = await SearchNexusCollectionsAsync(keyword, settings);
            results.AddRange(nexusCollections.Select(item =>
            {
                var text = FormatResult($"NexusPack#{item.ResourceId}", item.Name, item.Stat, item.TimeTag, item.IconUrl, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.NexusMods, true);
                return text;
            }));
        }

        if (includeCurseforge)
        {
            var curseforgeModpacks = await SearchCurseforgeModpacksAsync(keyword);
            results.AddRange(curseforgeModpacks.Select(item =>
            {
                var text = FormatResult($"CurseforgePack#{item.ResourceId}", item.Name, item.Stat, item.TimeTag, item.IconUrl, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.Curseforge, true);
                return text;
            }));
        }

        return Deduplicate(results);
    }

    public async Task<CatalogResourceDetails> GetResourceDetailsAsync(string displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return CatalogResourceDetails.Empty;
        }

        if (!_resourceIdentityMap.TryGetValue(displayText, out var identity))
        {
            var fallbackIdentity = TryParseFallbackIdentity(displayText);
            if (fallbackIdentity.HasValue)
            {
                identity = fallbackIdentity.Value;
                _resourceIdentityMap[displayText] = identity;
            }
            else
            {
                return new CatalogResourceDetails
                {
                    Name = displayText,
                    Source = "未知",
                    Summary = "未命中资源缓存，请返回搜索页重新选择资源。"
                };
            }
        }

        var settings = _settingsStore.Load();
        return identity.Source switch
        {
            CatalogSource.GitHub => await GetGithubSmapiDetailsAsync(identity),
            CatalogSource.NexusMods => await GetNexusResourceDetailsAsync(identity, settings),
            CatalogSource.Curseforge => await GetCurseforgeResourceDetailsAsync(identity),
            _ => CatalogResourceDetails.Empty
        };
    }

    private static CatalogResourceIdentity? TryParseFallbackIdentity(string displayText)
    {
        var parts = displayText.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var header = parts[0];
        if (!header.StartsWith("[", StringComparison.Ordinal))
        {
            return null;
        }

        var closeIndex = header.IndexOf(']');
        if (closeIndex <= 1)
        {
            return null;
        }

        var sourceSegment = header[1..closeIndex].Trim();
        var nameSegment = header[(closeIndex + 1)..].Trim();

        var sourceParts = sourceSegment.Split('#', 2, StringSplitOptions.TrimEntries);
        var sourceText = sourceParts[0];
        var idText = sourceParts.Length > 1 ? sourceParts[1] : string.Empty;
        var source = ParseCatalogSource(sourceText);
        if (source == CatalogSource.Unknown)
        {
            return null;
        }

        if (!nameSegment.Contains("smapi", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!long.TryParse(idText, out var resourceId) || resourceId < 0)
        {
            resourceId = 0;
        }

        if (source == CatalogSource.NexusMods && resourceId <= 0)
        {
            resourceId = 2400;
        }

        if (source == CatalogSource.Curseforge && resourceId <= 0)
        {
            resourceId = 898372;
        }

        return new CatalogResourceIdentity(resourceId, string.IsNullOrWhiteSpace(nameSegment) ? "SMAPI - Stardew Modding API" : nameSegment, source, false);
    }

    private static CatalogSource ParseCatalogSource(string sourceText)
    {
        if (sourceText.Contains("github", StringComparison.OrdinalIgnoreCase))
        {
            return CatalogSource.GitHub;
        }

        if (sourceText.Contains("nexus", StringComparison.OrdinalIgnoreCase))
        {
            return CatalogSource.NexusMods;
        }

        if (sourceText.Contains("curse", StringComparison.OrdinalIgnoreCase))
        {
            return CatalogSource.Curseforge;
        }

        return CatalogSource.Unknown;
    }

    private static string NormalizeSmapiKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return string.Empty;
        }

        var normalized = keyword.Trim();
        if (normalized.StartsWith("SMAPI", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[5..].Trim();
        }

        return normalized;
    }

    private async Task<List<RemoteSearchItem>> SearchGithubSmapiReleasesAsync(string keyword)
    {
        var (starCount, repositoryAvatar) = await GetGithubSmapiRepositoryStatsAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Pathoschild/SMAPI/releases?per_page=20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await GetHttpClient().SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return
            [
                new RemoteSearchItem
                {
                    ResourceId = 0,
                    Name = "SMAPI - Stardew Modding API",
                    Summary = "官方发布页，包含稳定版与预发布版。",
                    Stat = starCount > 0 ? $"Star {starCount:N0}" : "GitHub",
                    TimeTag = string.Empty,
                    IconUrl = repositoryAvatar
                }
            ];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var filter = NormalizeSmapiKeyword(keyword);
        var result = new List<RemoteSearchItem>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            var tag = TryGetString(release, "tag_name");
            var name = TryGetString(release, "name");
            var title = string.IsNullOrWhiteSpace(name)
                ? (string.IsNullOrWhiteSpace(tag) ? "SMAPI 发布" : $"SMAPI {tag}")
                : name;

            var summary = ExtractGithubReleaseSummary(release);
            if (!string.IsNullOrWhiteSpace(filter) &&
                title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                tag.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                summary.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var publishedAt = TryGetString(release, "published_at");
            var stat = starCount > 0 ? $"Star {starCount:N0}" : "GitHub";
            var timeTag = TryFormatTimeTag(publishedAt);
            var iconUrl = TryGetNestedString(release, "author", "avatar_url");
            if (string.IsNullOrWhiteSpace(iconUrl))
            {
                iconUrl = repositoryAvatar;
            }

            result.Add(new RemoteSearchItem
            {
                ResourceId = TryGetLong(release, "id"),
                Name = title,
                Summary = summary,
                Stat = stat,
                TimeTag = timeTag,
                IconUrl = iconUrl
            });
        }

        return result.Count > 0
            ? result
            :
            [
                new RemoteSearchItem
                {
                    ResourceId = 0,
                    Name = "SMAPI - Stardew Modding API",
                    Summary = "未匹配到发布版本，可打开详情查看官方发布地址。",
                    Stat = starCount > 0 ? $"Star {starCount:N0}" : "GitHub",
                    TimeTag = string.Empty,
                    IconUrl = repositoryAvatar
                }
            ];
    }

    private async Task<(long Stars, string AvatarUrl)> GetGithubSmapiRepositoryStatsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Pathoschild/SMAPI");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await GetHttpClient().SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return (0, string.Empty);
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var stars = TryGetLong(doc.RootElement, "stargazers_count");
        var avatar = TryGetNestedString(doc.RootElement, "owner", "avatar_url");
        return (stars, avatar);
    }

    private static string ExtractGithubReleaseSummary(JsonElement release)
    {
        var body = TryGetString(release, "body");
        if (string.IsNullOrWhiteSpace(body))
        {
            return "SMAPI 官方发布版本";
        }

        var lines = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstLine = lines.FirstOrDefault(line => !line.StartsWith("#", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "SMAPI 官方发布版本";
        }

        return firstLine.Length > 140 ? firstLine[..140] + "..." : firstLine;
    }

    private async Task<List<RemoteSearchItem>> SearchCurseforgeModsAsync(string keyword, int pageSize = 20)
    {
        var url = BuildCurseforgeSearchUrl(keyword, pageSize);
        LogDebug($"Curseforge/search request url={url}");
        using var response = await GetWithRedirectAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            LogDebug($"Curseforge/search failed status={(int)response.StatusCode} {response.ReasonPhrase}");
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var items = ParseCurseforgeItems(doc.RootElement, onlyLikelyModpacks: false);
        await FillMissingCurseforgeIconsAsync(items);
        LogDebug($"Curseforge/search parsed={items.Count}");

        return items;
    }

    private async Task FillMissingCurseforgeIconsAsync(IEnumerable<RemoteSearchItem> items)
    {
        var list = items?.ToList();

        if (list == null || list.Count == 0)
        {
            return;
        }

        for (var index = 0; index < list.Count; index++)
        {
            var item = list[index];
            if (item == null || item.ResourceId <= 0 || !string.IsNullOrWhiteSpace(item.IconUrl))
            {
                continue;
            }

            var detail = await GetCurseforgeModByIdAsync(item.ResourceId);
            if (detail == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(detail.IconUrl))
            {
                list[index] = new RemoteSearchItem
                {
                    ResourceId = item.ResourceId,
                    Name = item.Name,
                    Summary = item.Summary,
                    Stat = item.Stat,
                    TimeTag = item.TimeTag,
                    IconUrl = detail.IconUrl,
                    FullIconUrl = FirstNonEmpty(detail.FullIconUrl, detail.IconUrl),
                    ModType = item.ModType,
                    GameVersionTag = item.GameVersionTag,
                    SupportedGameVersions = item.SupportedGameVersions,
                    LocalizedName = item.LocalizedName,
                    LocalizedSummary = item.LocalizedSummary
                };
            }
        }

        if (items is List<RemoteSearchItem> writable)
        {
            writable.Clear();
            writable.AddRange(list);
        }
    }

    private async Task<RemoteSearchItem?> GetCurseforgeModByIdAsync(long modId)
    {
        if (modId <= 0)
        {
            return null;
        }

        var url = $"https://api.curse.tools/v1/cf/mods/{modId}";
        LogDebug($"Curseforge/mod detail request url={url}");
        using var response = await GetWithRedirectAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            LogDebug($"Curseforge/mod detail failed status={(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (!doc.RootElement.TryGetProperty("data", out var itemElement) || itemElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseCurseforgeItem(itemElement, onlyLikelyModpacks: false);
    }

    private async Task<List<RemoteSearchItem>> SearchCurseforgeModpacksAsync(string keyword)
    {
        var url = BuildCurseforgeSearchUrl(keyword, 20);
        LogDebug($"Curseforge/modpacks request url={url}");
        using var response = await GetWithRedirectAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            LogDebug($"Curseforge/modpacks failed status={(int)response.StatusCode} {response.ReasonPhrase}");
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var items = ParseCurseforgeItems(doc.RootElement, onlyLikelyModpacks: true);
        await FillMissingCurseforgeIconsAsync(items);
        LogDebug($"Curseforge/modpacks parsed={items.Count}");
        return items;
    }

    private static string BuildCurseforgeSearchUrl(string keyword, int pageSize = 20)
    {
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var baseUrl = $"https://api.curse.tools/v1/cf/mods/search?gameId={StardewCurseforgeGameId}&pageSize={normalizedPageSize}&index=0&sortField=2&sortOrder=desc";
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return baseUrl;
        }

        return baseUrl + $"&searchFilter={Uri.EscapeDataString(keyword.Trim())}";
    }

    private async Task<HttpResponseMessage> GetWithRedirectAsync(string url, int maxRedirects = 3)
    {
        var currentUrl = url;
        for (var index = 0; index <= maxRedirects; index++)
        {
            var response = await GetHttpClient().GetAsync(currentUrl);
            if (!IsRedirectStatusCode(response.StatusCode) || response.Headers.Location == null)
            {
                return response;
            }

            var nextUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(new Uri(currentUrl), response.Headers.Location);

            LogDebug($"Curseforge/redirect {(int)response.StatusCode} -> {nextUri}");
            response.Dispose();
            currentUrl = nextUri.ToString();
        }

        return await GetHttpClient().GetAsync(currentUrl);
    }

    private static bool IsRedirectStatusCode(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    private static List<RemoteSearchItem> ParseCurseforgeItems(JsonElement root, bool onlyLikelyModpacks)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<RemoteSearchItem>();
        foreach (var item in data.EnumerateArray())
        {
            var parsed = ParseCurseforgeItem(item, onlyLikelyModpacks);
            if (parsed != null)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static RemoteSearchItem? ParseCurseforgeItem(JsonElement item, bool onlyLikelyModpacks)
    {
        var name = TryGetString(item, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var summary = TryGetString(item, "summary");
        var downloadCount = TryGetLong(item, "downloadCount");
        var timeTag = TryFormatTimeTag(TryGetString(item, "dateModified"));
        var fullIconUrl = TryGetNestedString(item, "logo", "url");
        var iconUrl = TryGetNestedString(item, "logo", "thumbnailUrl");
        if (string.IsNullOrWhiteSpace(iconUrl))
        {
            iconUrl = fullIconUrl;
        }

        var isLikelyModpack = IsLikelyModpack(item);
        if (onlyLikelyModpacks && !isLikelyModpack)
        {
            return null;
        }

        if (!onlyLikelyModpacks && isLikelyModpack)
        {
            return null;
        }

        var modType = ResolveCurseforgeModType(item);
        var supportedVersions = ResolveCurseforgeGameVersions(item, summary, name);

        return new RemoteSearchItem
        {
            ResourceId = TryGetLong(item, "id"),
            Name = name,
            Summary = summary,
            Stat = downloadCount > 0 ? $"下载 {downloadCount:N0}" : string.Empty,
            TimeTag = timeTag,
            IconUrl = iconUrl,
            FullIconUrl = fullIconUrl,
            ModType = modType,
            GameVersionTag = supportedVersions.FirstOrDefault() ?? string.Empty,
            SupportedGameVersions = supportedVersions
        };
    }

    private static bool IsLikelyModpack(JsonElement item)
    {
        var name = TryGetString(item, "name");
        var summary = TryGetString(item, "summary");

        if (name.Contains("modpack", StringComparison.OrdinalIgnoreCase) ||
            summary.Contains("modpack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!item.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var category in categories.EnumerateArray())
        {
            var categoryName = TryGetString(category, "name");
            var categorySlug = TryGetString(category, "slug");
            if (categoryName.Contains("modpack", StringComparison.OrdinalIgnoreCase) ||
                categorySlug.Contains("modpack", StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains("collection", StringComparison.OrdinalIgnoreCase) ||
                categorySlug.Contains("collection", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<List<RemoteSearchItem>> SearchNexusModsAsync(string keyword, AppUserSettings settings, int count = 20)
    {
        if (!HasNexusCredential(settings))
        {
            return [];
        }

        var normalized = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim();
        var safeCount = Math.Clamp(count, 1, 80);
        var graphQlQuery = @"
            query SearchModsByGame($filter: ModsFilter, $sort: [ModsSort!], $offset: Int, $count: Int) {
                mods(filter: $filter, sort: $sort, offset: $offset, count: $count) {
                    nodes {
                        modId
                        name
                        summary
                        description
                        pictureUrl
                        downloads
                        category
                        updatedAt
                    }
                }
            }";

        var filterCandidates = new List<object>();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            filterCandidates.Add(new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } }
            });
        }
        else
        {
            var wildcard = normalized.Contains('*') ? normalized : $"*{normalized}*";
            var prefixWildcard = normalized.Contains('*') ? normalized : $"{normalized}*";
            filterCandidates.Add(new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                name = new[] { new { op = "WILDCARD", value = wildcard } }
            });
            filterCandidates.Add(new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                name = new[] { new { op = "WILDCARD", value = prefixWildcard } }
            });
            filterCandidates.Add(new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                name = new[] { new { op = "EQUALS", value = normalized } }
            });
        }

        foreach (var filter in filterCandidates)
        {
            var requestBody = new
            {
                query = graphQlQuery,
                variables = new
                {
                    filter,
                    sort = new[]
                    {
                        new
                        {
                            downloads = new { direction = "DESC" }
                        }
                    },
                    offset = 0,
                    count = safeCount
                }
            };

            using var doc = await ExecuteNexusGraphQlAsync(requestBody, settings);
            if (doc == null)
            {
                continue;
            }

            var parsed = ParseNexusModsFromGraphQl(doc.RootElement);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var fallbackCount = Math.Min(Math.Max(safeCount * 8, 120), 240);
        var fallbackRequestBody = new
        {
            query = graphQlQuery,
            variables = new
            {
                filter = new
                {
                    gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } }
                },
                sort = new[]
                {
                    new
                    {
                        downloads = new { direction = "DESC" }
                    }
                },
                offset = 0,
                count = fallbackCount
            }
        };

        using var fallbackDoc = await ExecuteNexusGraphQlAsync(fallbackRequestBody, settings);
        if (fallbackDoc == null)
        {
            return [];
        }

        return ParseNexusModsFromGraphQl(fallbackDoc.RootElement)
            .Where(item => item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                           item.Summary.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(safeCount)
            .ToList();
    }

    private static List<RemoteSearchItem> ParseNexusModsFromGraphQl(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("mods", out var mods) ||
            !mods.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<RemoteSearchItem>();
        foreach (var row in nodes.EnumerateArray())
        {
            var id = TryGetLong(row, "modId");
            if (id <= 0)
            {
                continue;
            }

            var name = TryGetString(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var summary = FirstNonEmpty(TryGetString(row, "summary"), TryGetString(row, "description"));
            var pictureUrl = TryGetString(row, "pictureUrl");
            var versions = ParsePossibleGameVersions(summary);
            results.Add(new RemoteSearchItem
            {
                ResourceId = id,
                Name = name,
                Summary = summary,
                Stat = BuildDownloadsMetric("downloads", TryGetLong(row, "downloads")),
                TimeTag = BuildTimeTag(TryGetString(row, "updatedAt")),
                IconUrl = pictureUrl,
                FullIconUrl = pictureUrl,
                ModType = NormalizeModTypeTag($"{TryGetString(row, "category")} {summary}"),
                SupportedGameVersions = versions,
                GameVersionTag = versions.FirstOrDefault() ?? string.Empty,
                Source = CatalogSource.NexusMods,
                SourceTagHint = "NexusMod"
            });
        }

        return results;
    }

    private async Task<List<RemoteSearchItem>> SearchNexusCollectionsAsync(string keyword, AppUserSettings settings)
    {
        if (!HasNexusCredential(settings))
        {
            return [];
        }

        var normalized = keyword?.Trim() ?? string.Empty;
        var graphQlQuery = @"
            query GetGameCollections(
              $filter: CollectionsSearchFilter,
              $sort: [CollectionsSearchSort!],
              $offset: Int,
              $count: Int
            ) {
              collectionsV2(
                filter: $filter,
                sort: $sort,
                offset: $offset,
                count: $count
              ) {
                nodes {
                  id
                  name
                  summary
                  totalDownloads
                  updatedAt
                  tileImage { url }
                }
              }
            }";

        var filterCandidates = new object[]
        {
            new
            {
                gameDomain = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                collectionStatus = new[] { new { op = "EQUALS", value = "published" } }
            },
            new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                collectionStatus = new[] { new { op = "EQUALS", value = "published" } }
            },
            new
            {
                gameDomain = new[] { new { op = "EQUALS", value = NexusGameDomain } }
            },
            new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } }
            }
        };

        foreach (var filter in filterCandidates)
        {
            var requestBody = new
            {
                query = graphQlQuery,
                variables = new
                {
                    filter,
                    sort = new[]
                    {
                        new
                        {
                            endorsements = new { direction = "DESC" }
                        }
                    },
                    offset = 0,
                    count = 30
                }
            };

            using var doc = await ExecuteNexusGraphQlAsync(requestBody, settings);
            if (doc == null)
            {
                continue;
            }

            var collections = ParseNexusCollectionsFromGraphQl(doc.RootElement);
            if (collections.Count == 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                collections = collections
                    .Where(item => item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                                   item.Summary.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return collections;
        }

        return [];
    }

    private static List<RemoteSearchItem> ParseNexusCollectionsFromGraphQl(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("collectionsV2", out var collections) ||
            !collections.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<RemoteSearchItem>();
        foreach (var row in nodes.EnumerateArray())
        {
            var id = TryGetLong(row, "id");
            if (id <= 0)
            {
                continue;
            }

            var name = TryGetString(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var iconUrl = TryGetNestedString(row, "tileImage", "url");
            results.Add(new RemoteSearchItem
            {
                ResourceId = id,
                Name = name,
                Summary = TryGetString(row, "summary"),
                Stat = BuildDownloadsMetric("downloads", TryGetLong(row, "totalDownloads")),
                TimeTag = BuildTimeTag(TryGetString(row, "updatedAt")),
                IconUrl = iconUrl,
                FullIconUrl = iconUrl,
                Source = CatalogSource.NexusMods,
                SourceTagHint = "NexusPack"
            });
        }

        return results;
    }

    private async Task<JsonDocument?> ExecuteNexusGraphQlAsync(object requestBody, AppUserSettings settings)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        ApplyNexusHeaders(request, settings);

        using var response = await GetHttpClient(settings).SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static bool HasNexusCredential(AppUserSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.NexusOAuthAccessToken) ||
               !string.IsNullOrWhiteSpace(settings.NexusApiKey);
    }

    private static void ApplyNexusHeaders(HttpRequestMessage request, AppUserSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.NexusOAuthAccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.NexusOAuthAccessToken);
        }
        else if (!string.IsNullOrWhiteSpace(settings.NexusApiKey))
        {
            request.Headers.TryAddWithoutValidation("apikey", settings.NexusApiKey);
        }

        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Application-Name", "Stardew Valley Launcher");
        request.Headers.TryAddWithoutValidation("Application-Version", "1.0.0");
        request.Headers.TryAddWithoutValidation("Protocol-Version", "1.0.0");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string BuildDownloadsMetric(string label, long downloads)
    {
        return downloads > 0 ? $"{label} {downloads:N0}" : string.Empty;
    }

    private static string BuildTimeTag(string? rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawTime))
        {
            return string.Empty;
        }

        return TryFormatTimeTag(rawTime);
    }

    private async Task<CatalogResourceDetails> GetNexusResourceDetailsAsync(CatalogResourceIdentity identity, AppUserSettings settings)
    {
        if (identity.IsModpack)
        {
            return new CatalogResourceDetails
            {
                Name = identity.Name,
                Source = "NexusMods",
                Summary = "Collection 详情可在下载任务中通过 NXM 链接解析并安装。",
                VersionOptions = [$"Collection ID: {identity.ResourceId}"],
                Dependencies = ["依赖将在 Collection 清单解析阶段自动识别"],
                DownloadOptions = ["通过 NXM Collection 链接导入下载"]
            };
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.nexusmods.com/v1/games/{NexusGameDomain}/mods/{identity.ResourceId}/files.json");
        ApplyNexusHeaders(request, settings);

        using var response = await GetHttpClient(settings).SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return new CatalogResourceDetails
            {
                Name = identity.Name,
                Source = "NexusMods",
                Summary = "无法获取 Nexus 文件列表，请检查登录状态。",
                DownloadOptions = ["可先通过 NXM 链接导入下载"]
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return new CatalogResourceDetails
            {
                Name = identity.Name,
                Source = "NexusMods",
                Summary = "未获取到可用文件。",
                DownloadOptions = ["可先通过 NXM 链接导入下载"]
            };
        }

        var versions = new List<string>();
        var downloadOptions = new List<string>();
        foreach (var file in files.EnumerateArray().Take(12))
        {
            var version = TryGetString(file, "version");
            var fileName = TryGetString(file, "file_name");
            var fileId = TryGetLong(file, "file_id");

            if (!string.IsNullOrWhiteSpace(version))
            {
                versions.Add(version);
            }

            if (fileId > 0)
            {
                downloadOptions.Add($"File {fileId}: {fileName}");
            }
        }

        return new CatalogResourceDetails
        {
            Name = identity.Name,
            Source = "NexusMods",
            Summary = $"已获取 {downloadOptions.Count} 个可下载文件",
            VersionOptions = versions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Dependencies = ["依赖信息将在下载文件后按 manifest 解析"],
            DownloadOptions = downloadOptions
        };
    }

    private async Task<CatalogResourceDetails> GetCurseforgeResourceDetailsAsync(CatalogResourceIdentity identity)
    {
        using var response = await GetHttpClient().GetAsync($"https://api.curse.tools/v1/cf/mods/{identity.ResourceId}/files?index=0&pageSize=30");
        if (!response.IsSuccessStatusCode)
        {
            return new CatalogResourceDetails
            {
                Name = identity.Name,
                Source = "Curseforge",
                Summary = "无法获取 Curseforge 文件列表。",
                DownloadOptions = ["可改用 URL/NXM 方式导入下载"]
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (!doc.RootElement.TryGetProperty("data", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return new CatalogResourceDetails
            {
                Name = identity.Name,
                Source = "Curseforge",
                Summary = "未获取到可用文件。"
            };
        }

        var versions = new List<string>();
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloadOptions = new List<string>();

        foreach (var file in files.EnumerateArray().Take(15))
        {
            var displayName = TryGetString(file, "displayName");
            var fileName = TryGetString(file, "fileName");
            var fileId = TryGetLong(file, "id");

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                versions.Add(displayName);
            }

            if (fileId > 0)
            {
                downloadOptions.Add($"File {fileId}: {fileName}");
            }

            if (file.TryGetProperty("dependencies", out var depArray) && depArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var dependency in depArray.EnumerateArray())
                {
                    var modId = TryGetLong(dependency, "modId");
                    var relationType = TryGetLong(dependency, "relationType");
                    if (modId > 0)
                    {
                        dependencies.Add($"ModId {modId} (relationType={relationType})");
                    }
                }
            }
        }

        return new CatalogResourceDetails
        {
            Name = identity.Name,
            Source = "Curseforge",
            Summary = $"已获取 {downloadOptions.Count} 个可下载文件",
            VersionOptions = versions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Dependencies = dependencies.Count > 0 ? dependencies.ToList() : ["未声明显式依赖"],
            DownloadOptions = downloadOptions
        };
    }

    public async Task<string> ResolveCurseforgeFileDownloadUrlAsync(
        long modId,
        long fileId,
        string fallbackUrl = "",
        CancellationToken cancellationToken = default)
    {
        if (modId <= 0 || fileId <= 0)
        {
            return fallbackUrl ?? string.Empty;
        }

        var client = GetHttpClient();
        var candidateEndpoints = new[]
        {
            $"https://api.curse.tools/v1/cf/mods/{modId}/files/{fileId}/download-url",
            $"https://api.curse.tools/v1/cf/mods/{modId}/files/{fileId}",
            $"https://api.curse.tools/v1/cf/mods/{modId}/files?index=0&pageSize=60"
        };

        foreach (var endpoint in candidateEndpoints)
        {
            try
            {
                using var response = await client.GetAsync(endpoint, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var resolved = await TryExtractCurseforgeDownloadUrlAsync(response.Content, fileId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }
            catch
            {
                // Keep this resolver best-effort and continue fallback chain.
            }
        }

        return fallbackUrl ?? string.Empty;
    }

    private static async Task<string> TryExtractCurseforgeDownloadUrlAsync(
        HttpContent content,
        long fileId,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var direct = FindDownloadUrlInElement(doc.RootElement, fileId);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return string.Empty;
    }

    private static string FindDownloadUrlInElement(JsonElement element, long fileId)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                if (TryReadDownloadUrl(element, out var directUrl))
                {
                    return directUrl;
                }

                if (fileId > 0 &&
                    element.TryGetProperty("id", out var idElement) &&
                    idElement.ValueKind == JsonValueKind.Number &&
                    idElement.TryGetInt64(out var currentId) &&
                    currentId == fileId &&
                    TryReadDownloadUrl(element, out var matchedUrl))
                {
                    return matchedUrl;
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindDownloadUrlInElement(property.Value, fileId);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                return string.Empty;
            }
            case JsonValueKind.Array:
            {
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindDownloadUrlInElement(item, fileId);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                return string.Empty;
            }
            default:
                return string.Empty;
        }
    }

    private static bool TryReadDownloadUrl(JsonElement element, out string downloadUrl)
    {
        var candidateKeys = new[] { "downloadUrl", "download_url", "fileUrl", "file_url", "url" };
        foreach (var key in candidateKeys)
        {
            if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var candidate = value.GetString();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                downloadUrl = uri.ToString();
                return true;
            }
        }

        downloadUrl = string.Empty;
        return false;
    }

    private async Task<CatalogResourceDetails> GetGithubSmapiDetailsAsync(CatalogResourceIdentity identity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Pathoschild/SMAPI/releases?per_page=20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await GetHttpClient().SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return new CatalogResourceDetails
            {
                Name = string.IsNullOrWhiteSpace(identity.Name) ? "SMAPI - Stardew Modding API" : identity.Name,
                Source = "GitHub",
                Summary = "无法获取 GitHub 发布详情，可直接访问官方发布页。",
                VersionOptions = ["https://github.com/Pathoschild/SMAPI/releases"],
                Dependencies = ["安装前请确保已配置游戏目录"],
                DownloadOptions = ["手动下载并导入 ZIP"]
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return CatalogResourceDetails.Empty;
        }

        JsonElement? targetRelease = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (identity.ResourceId > 0 && TryGetLong(release, "id") == identity.ResourceId)
            {
                targetRelease = release;
                break;
            }
        }

        if (targetRelease == null)
        {
            targetRelease = doc.RootElement.EnumerateArray().FirstOrDefault();
        }

        if (targetRelease == null || targetRelease.Value.ValueKind == JsonValueKind.Undefined)
        {
            return new CatalogResourceDetails
            {
                Name = string.IsNullOrWhiteSpace(identity.Name) ? "SMAPI - Stardew Modding API" : identity.Name,
                Source = "GitHub",
                Summary = "未获取到可用发布记录。",
                DownloadOptions = ["https://github.com/Pathoschild/SMAPI/releases"]
            };
        }

        var selectedRelease = targetRelease.Value;
        var tag = TryGetString(selectedRelease, "tag_name");
        var title = TryGetString(selectedRelease, "name");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = string.IsNullOrWhiteSpace(tag)
                ? (string.IsNullOrWhiteSpace(identity.Name) ? "SMAPI - Stardew Modding API" : identity.Name)
                : $"SMAPI {tag}";
        }

        var publishedAt = TryGetString(selectedRelease, "published_at");
        var versionOptions = new List<string>();
        if (!string.IsNullOrWhiteSpace(tag))
        {
            versionOptions.Add($"Tag: {tag}");
        }

        if (DateTime.TryParse(publishedAt, out var published))
        {
            versionOptions.Add($"发布时间: {published:yyyy-MM-dd HH:mm}");
        }

        versionOptions.Add("官方发布页: https://github.com/Pathoschild/SMAPI/releases");

        var downloadOptions = new List<string>();
        if (selectedRelease.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray().Take(12))
            {
                var assetName = TryGetString(asset, "name");
                var assetUrl = TryGetString(asset, "browser_download_url");
                if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(assetUrl))
                {
                    continue;
                }

                downloadOptions.Add($"{assetName} | {assetUrl}");
            }
        }

        if (downloadOptions.Count == 0)
        {
            downloadOptions.Add("https://github.com/Pathoschild/SMAPI/releases");
        }

        return new CatalogResourceDetails
        {
            Name = title,
            Source = "GitHub",
            Summary = ExtractGithubReleaseSummary(selectedRelease),
            VersionOptions = versionOptions,
            Dependencies = ["安装前请确保已配置游戏目录", "推荐下载稳定版并按向导安装"],
            DownloadOptions = downloadOptions
        };
    }

    private static string FormatResult(string source, string name, string stat, string summary)
    {
        var parts = new List<string> { $"[{SanitizeSegment(source)}] {SanitizeSegment(name)}" };
        if (!string.IsNullOrWhiteSpace(stat))
        {
            parts.Add(SanitizeSegment(stat));
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(SanitizeSegment(summary));
        }

        return string.Join(" | ", parts);
    }

    private static string FormatResult(string source, string name, string metricTag, string timeTag, string iconUrl, string fullIconUrl, string summary)
    {
        var parts = new List<string> { $"[{SanitizeSegment(source)}] {SanitizeSegment(name)}" };
        if (!string.IsNullOrWhiteSpace(metricTag))
        {
            parts.Add($"metric={SanitizeSegment(metricTag)}");
        }

        if (!string.IsNullOrWhiteSpace(timeTag))
        {
            parts.Add($"time={SanitizeSegment(timeTag)}");
        }

        if (!string.IsNullOrWhiteSpace(iconUrl))
        {
            parts.Add($"icon={SanitizeSegment(iconUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(fullIconUrl))
        {
            parts.Add($"fullIcon={SanitizeSegment(fullIconUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(SanitizeSegment(summary));
        }

        return string.Join(" | ", parts);
    }

    private static string FormatResult(string source, string name, string metricTag, string timeTag, string iconUrl, string summary)
    {
        return FormatResult(source, name, metricTag, timeTag, iconUrl, string.Empty, summary);
    }

    private static string FormatModResult(string source, RemoteSearchItem item)
    {
        var parts = new List<string> { $"[{SanitizeSegment(source)}#{item.ResourceId}] {SanitizeSegment(item.Name)}" };

        if (!string.IsNullOrWhiteSpace(item.Stat))
        {
            parts.Add($"metric={SanitizeSegment(item.Stat)}");
        }

        if (!string.IsNullOrWhiteSpace(item.TimeTag))
        {
            parts.Add($"time={SanitizeSegment(item.TimeTag)}");
        }

        if (!string.IsNullOrWhiteSpace(item.IconUrl))
        {
            parts.Add($"icon={SanitizeSegment(item.IconUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(item.FullIconUrl))
        {
            parts.Add($"fullIcon={SanitizeSegment(item.FullIconUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(item.ModType))
        {
            parts.Add($"type={SanitizeSegment(item.ModType)}");
        }

        if (!string.IsNullOrWhiteSpace(item.GameVersionTag))
        {
            parts.Add($"compat={SanitizeSegment(item.GameVersionTag)}");
        }

        parts.Add($"srcName={SanitizeSegment(item.Name)}");
        parts.Add($"srcSummary={SanitizeSegment(item.Summary)}");

        if (!string.IsNullOrWhiteSpace(item.LocalizedName))
        {
            parts.Add($"zhName={SanitizeSegment(item.LocalizedName)}");
        }

        if (!string.IsNullOrWhiteSpace(item.LocalizedSummary))
        {
            parts.Add($"zhSummary={SanitizeSegment(item.LocalizedSummary)}");
        }

        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            parts.Add(SanitizeSegment(item.Summary));
        }

        return string.Join(" | ", parts);
    }

    private static string NormalizeFilterToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var normalized = token.Trim();
        return string.Equals(normalized, "全部", StringComparison.OrdinalIgnoreCase) ? string.Empty : normalized;
    }

    private async Task ApplyCommunityLocalizationAsync(IEnumerable<RemoteSearchItem> items, string platform)
    {
        var tasks = items.Select(async item =>
        {
            if (item.ResourceId <= 0)
            {
                return;
            }

            try
            {
                var (nameZhCn, summaryZhCn) = await FetchCommunityLocalizationAsync(platform, item.ResourceId);
                if (string.IsNullOrWhiteSpace(nameZhCn) && string.IsNullOrWhiteSpace(summaryZhCn))
                {
                    return;
                }

                item.LocalizedName = nameZhCn;
                item.LocalizedSummary = summaryZhCn;
            }
            catch
            {
                // Ignore single-item localization failures.
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<(string NameZhCn, string SummaryZhCn)> FetchCommunityLocalizationAsync(string platform, long id)
    {
        var normalizedPlatform = NormalizeCommunityPlatform(platform);
        if (string.IsNullOrWhiteSpace(normalizedPlatform) || id <= 0)
        {
            return (string.Empty, string.Empty);
        }

        var relativePath = $"Mods/{normalizedPlatform}/{id}.json";
        var urls = new[]
        {
            $"https://raw.githubusercontent.com/panda-lsy/StardewValley-Community-Localization/main/{relativePath}",
            $"https://gitee.com/mc_shengxia/StardewValley-Community-Localization/raw/main/{relativePath}"
        };

        foreach (var url in urls)
        {
            try
            {
                using var response = await GetHttpClient().GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;
                var nameZhCn = TryGetNestedString(root, "name", "zh-CN");
                var summaryZhCn = TryGetNestedString(root, "description", "zh-CN");

                if (!string.IsNullOrWhiteSpace(nameZhCn) || !string.IsNullOrWhiteSpace(summaryZhCn))
                {
                    return (nameZhCn, summaryZhCn);
                }
            }
            catch
            {
                // Try next provider.
            }
        }

        return (string.Empty, string.Empty);
    }

    private static string NormalizeCommunityPlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return string.Empty;
        }

        if (platform.Contains("nexus", StringComparison.OrdinalIgnoreCase))
        {
            return "NexusMods";
        }

        if (platform.Contains("curse", StringComparison.OrdinalIgnoreCase))
        {
            return "Curseforge";
        }

        return string.Empty;
    }

    private static bool MatchesModTypeFilter(string itemModType, string requestedType)
    {
        if (string.IsNullOrWhiteSpace(requestedType))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(itemModType))
        {
            return false;
        }

        return string.Equals(itemModType, requestedType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGameVersionFilter(IEnumerable<string> candidates, string fallbackTag, string requestedVersion)
    {
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return true;
        }

        var normalizedRequest = requestedVersion.Trim();
        var normalizedCandidates = (candidates ?? Enumerable.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        if (!string.IsNullOrWhiteSpace(fallbackTag))
        {
            normalizedCandidates.Add(fallbackTag.Trim());
        }

        if (normalizedCandidates.Count == 0)
        {
            return false;
        }

        return normalizedCandidates.Any(candidate =>
            candidate.Equals(normalizedRequest, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(normalizedRequest + ".", StringComparison.OrdinalIgnoreCase) ||
            normalizedRequest.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeModTypeTag(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return string.Empty;
        }

        var text = rawType.Trim();
        if (text.Contains("ui", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("界面", StringComparison.OrdinalIgnoreCase))
        {
            return "界面美化";
        }

        if (text.Contains("content", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("expansion", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("内容", StringComparison.OrdinalIgnoreCase))
        {
            return "游戏内容";
        }

        if (text.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("utility", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("工具", StringComparison.OrdinalIgnoreCase))
        {
            return "工具类";
        }

        if (text.Contains("texture", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sound", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("材质", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("音效", StringComparison.OrdinalIgnoreCase))
        {
            return "音效材质";
        }

        if (text.Contains("cheat", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("作弊", StringComparison.OrdinalIgnoreCase))
        {
            return "作弊类";
        }

        if (text.Contains("framework", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mechanic", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("功能", StringComparison.OrdinalIgnoreCase))
        {
            return "功能扩展";
        }

        return "功能扩展";
    }

    private static string ResolveCurseforgeModType(JsonElement item)
    {
        if (!item.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var category in categories.EnumerateArray())
        {
            var name = TryGetString(category, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            return NormalizeModTypeTag(name);
        }

        return string.Empty;
    }

    private static List<string> ResolveCurseforgeGameVersions(JsonElement item, string summary, string name)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (item.TryGetProperty("latestFilesIndexes", out var fileIndexes) && fileIndexes.ValueKind == JsonValueKind.Array)
        {
            foreach (var fileIndex in fileIndexes.EnumerateArray())
            {
                var version = TryGetString(fileIndex, "gameVersion");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    versions.Add(version);
                }
            }
        }

        foreach (var version in ParsePossibleGameVersions(summary).Concat(ParsePossibleGameVersions(name)))
        {
            versions.Add(version);
        }

        return versions
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .OrderByDescending(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParsePossibleGameVersions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var matches = Regex.Matches(text, @"\b1\.\d(?:\.\d+)?\b", RegexOptions.IgnoreCase);
        return matches
            .Select(match => match.Value)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> Deduplicate(IEnumerable<string> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static string TryGetNestedString(JsonElement element, string objectPropertyName, string valuePropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return TryGetString(nested, valuePropertyName);
    }

    private static string TryFormatTimeTag(string rawTime)
    {
        if (!DateTime.TryParse(rawTime, out var value))
        {
            return string.Empty;
        }

        return $"{value:yyyy-MM-dd}";
    }

    private static string SanitizeSegment(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Replace("|", "｜");
    }

    private static long TryGetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return 0;
    }

    private sealed class RemoteSearchItem
    {
        public long ResourceId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Summary { get; init; } = string.Empty;

        public string Stat { get; init; } = string.Empty;

        public string TimeTag { get; init; } = string.Empty;

        public string IconUrl { get; init; } = string.Empty;

        public string FullIconUrl { get; init; } = string.Empty;

        public string ModType { get; set; } = string.Empty;

        public string GameVersionTag { get; set; } = string.Empty;

        public List<string> SupportedGameVersions { get; set; } = [];

        public string LocalizedName { get; set; } = string.Empty;

        public string LocalizedSummary { get; set; } = string.Empty;

        public CatalogSource Source { get; set; } = CatalogSource.Unknown;

        public string SourceTagHint { get; set; } = string.Empty;
    }
}

public sealed class CatalogResourceDetails
{
    public static CatalogResourceDetails Empty => new();

    public string Name { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public List<string> VersionOptions { get; init; } = [];

    public List<string> Dependencies { get; init; } = [];

    public List<string> DownloadOptions { get; init; } = [];
}

public sealed class CatalogPagedResult
{
    public List<string> Items { get; init; } = [];

    public bool HasMore { get; init; }
}

internal enum CatalogSource
{
    Unknown,
    GitHub,
    NexusMods,
    Curseforge
}

internal readonly record struct CatalogResourceIdentity(long ResourceId, string Name, CatalogSource Source, bool IsModpack);
