using SVL.Avalonia.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SVL.Avalonia.Services;

public sealed class RemoteCatalogService
{
    private const int StardewCurseforgeGameId = 1303;
    private const string NexusGameDomain = "stardewvalley";
    private readonly AppUserSettingsStore _settingsStore;
    private readonly Dictionary<string, CatalogResourceIdentity> _resourceIdentityMap = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "SVL-Avalonia-Migration" }
        }
    };

    public RemoteCatalogService(AppUserSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
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
                var text = FormatResult($"NexusPack#{item.ResourceId}", item.Name, item.Stat, item.Summary);
                _resourceIdentityMap[text] = new CatalogResourceIdentity(item.ResourceId, item.Name, CatalogSource.NexusMods, true);
                return text;
            }));
        }

        if (includeCurseforge)
        {
            var curseforgeModpacks = await SearchCurseforgeModpacksAsync(keyword);
            results.AddRange(curseforgeModpacks.Select(item =>
            {
                var text = FormatResult($"CurseforgePack#{item.ResourceId}", item.Name, item.Stat, item.Summary);
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
            return new CatalogResourceDetails
            {
                Name = displayText,
                Source = "未知",
                Summary = "未命中资源缓存，请返回搜索页重新选择资源。"
            };
        }

        var settings = _settingsStore.Load();
        return identity.Source switch
        {
            CatalogSource.NexusMods => await GetNexusResourceDetailsAsync(identity, settings),
            CatalogSource.Curseforge => await GetCurseforgeResourceDetailsAsync(identity),
            _ => CatalogResourceDetails.Empty
        };
    }

    private static async Task<List<RemoteSearchItem>> SearchCurseforgeModsAsync(string keyword)
    {
        var url = BuildCurseforgeSearchUrl(keyword);
        using var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return ParseCurseforgeItems(doc.RootElement, onlyLikelyModpacks: false);
    }

    private static async Task<List<RemoteSearchItem>> SearchCurseforgeModpacksAsync(string keyword)
    {
        var url = BuildCurseforgeSearchUrl(keyword);
        using var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return ParseCurseforgeItems(doc.RootElement, onlyLikelyModpacks: true);
    }

    private static string BuildCurseforgeSearchUrl(string keyword)
    {
        var baseUrl = $"https://api.curse.tools/v1/cf/mods/search?gameId={StardewCurseforgeGameId}&pageSize=20&index=0&sortField=2&sortOrder=desc";
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return baseUrl;
        }

        return baseUrl + $"&searchFilter={Uri.EscapeDataString(keyword.Trim())}";
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
            var name = TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var summary = TryGetString(item, "summary");
            var downloadCount = TryGetLong(item, "downloadCount");
            var isLikelyModpack = IsLikelyModpack(item);
            if (onlyLikelyModpacks && !isLikelyModpack)
            {
                continue;
            }

            if (!onlyLikelyModpacks && isLikelyModpack)
            {
                continue;
            }

            result.Add(new RemoteSearchItem
            {
                ResourceId = TryGetLong(item, "id"),
                Name = name,
                Summary = summary,
                Stat = downloadCount > 0 ? $"下载 {downloadCount}" : string.Empty
            });
        }

        return result;
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

    private static async Task<List<RemoteSearchItem>> SearchNexusModsAsync(string keyword, AppUserSettings settings)
    {
        if (!HasNexusCredential(settings))
        {
            return [];
        }

        var graphQlQuery = @"
            query SearchModsByGame($filter: ModsFilter, $sort: [ModsSort!], $offset: Int, $count: Int) {
              mods(filter: $filter, sort: $sort, offset: $offset, count: $count) {
                nodes {
                                    modId
                  name
                  summary
                  downloads
                }
              }
            }";

        var normalized = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim();
        var wildcard = normalized.Contains('*') || string.IsNullOrWhiteSpace(normalized)
            ? normalized
            : $"*{normalized}*";

        object filter = string.IsNullOrWhiteSpace(normalized)
            ? new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } }
            }
            : new
            {
                gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                name = new[] { new { op = "WILDCARD", value = wildcard } }
            };

        var body = new
        {
            query = graphQlQuery,
            variables = new
            {
                filter,
                sort = new[] { new { downloads = new { direction = "DESC" } } },
                offset = 0,
                count = 20
            }
        };

        var root = await PostNexusGraphQlAsync(body, settings);
        if (root.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("mods", out var mods) ||
            !mods.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<RemoteSearchItem>();
        foreach (var node in nodes.EnumerateArray())
        {
            var name = TryGetString(node, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new RemoteSearchItem
            {
                ResourceId = TryGetLong(node, "modId"),
                Name = name,
                Summary = TryGetString(node, "summary"),
                Stat = $"下载 {TryGetLong(node, "downloads")}"
            });
        }

        return result;
    }

    private static async Task<List<RemoteSearchItem>> SearchNexusCollectionsAsync(string keyword, AppUserSettings settings)
    {
        if (!HasNexusCredential(settings))
        {
            return [];
        }

        var graphQlQuery = @"
            query GetGameCollections($filter: CollectionsSearchFilter, $sort: [CollectionsSearchSort!], $offset: Int, $count: Int) {
              collectionsV2(filter: $filter, sort: $sort, offset: $offset, count: $count) {
                nodes {
                                    id
                  name
                  summary
                  totalDownloads
                }
              }
            }";

        var body = new
        {
            query = graphQlQuery,
            variables = new
            {
                filter = new
                {
                    gameDomainName = new[] { new { op = "EQUALS", value = NexusGameDomain } },
                    collectionStatus = new[] { new { op = "EQUALS", value = "published" } }
                },
                sort = new[] { new { endorsements = new { direction = "DESC" } } },
                offset = 0,
                count = 60
            }
        };

        var root = await PostNexusGraphQlAsync(body, settings);
        if (root.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("collectionsV2", out var collections) ||
            !collections.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var normalized = keyword?.Trim() ?? string.Empty;
        var result = new List<RemoteSearchItem>();
        foreach (var node in nodes.EnumerateArray())
        {
            var name = TryGetString(node, "name");
            var summary = TryGetString(node, "summary");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalized) &&
                name.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) < 0 &&
                summary.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            result.Add(new RemoteSearchItem
            {
                ResourceId = TryGetLong(node, "id"),
                Name = name,
                Summary = summary,
                Stat = $"下载 {TryGetLong(node, "totalDownloads")}"
            });
        }

        return result.Take(20).ToList();
    }

    private static async Task<JsonElement> PostNexusGraphQlAsync(object body, AppUserSettings settings)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        ApplyNexusHeaders(request, settings);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
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

    private static bool HasNexusCredential(AppUserSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.NexusOAuthAccessToken) ||
               !string.IsNullOrWhiteSpace(settings.NexusApiKey);
    }

    private static async Task<CatalogResourceDetails> GetNexusResourceDetailsAsync(CatalogResourceIdentity identity, AppUserSettings settings)
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

        using var response = await Http.SendAsync(request);
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

    private static async Task<CatalogResourceDetails> GetCurseforgeResourceDetailsAsync(CatalogResourceIdentity identity)
    {
        using var response = await Http.GetAsync($"https://api.curse.tools/v1/cf/mods/{identity.ResourceId}/files?index=0&pageSize=30");
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

    private static string FormatResult(string source, string name, string stat, string summary)
    {
        var parts = new List<string> { $"[{source}] {name}" };
        if (!string.IsNullOrWhiteSpace(stat))
        {
            parts.Add(stat);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(summary);
        }

        return string.Join(" | ", parts);
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

internal enum CatalogSource
{
    Unknown,
    NexusMods,
    Curseforge
}

internal readonly record struct CatalogResourceIdentity(long ResourceId, string Name, CatalogSource Source, bool IsModpack);
