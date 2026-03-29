using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using System.Diagnostics;

namespace SVL.Avalonia.ViewModels;

public partial class NexusLoginDialogViewModel : ObservableObject
{
    private readonly NexusAuthService _nexusAuthService;
    private readonly NexusOAuthService _nexusOAuthService;
    private NexusOAuthStartResult? _oauthStart;
    private string _lastOAuthAuthorizeUrl = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "请输入 Nexus API Key 后点击验证";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _oauthCallbackInput = string.Empty;

    [ObservableProperty]
    private string _oauthStatusMessage = "点击“开始 OAuth 授权”后，在浏览器完成登录并粘贴回调 URL 或 code";

    public bool CanRetryOpenAuthorizePage => !string.IsNullOrWhiteSpace(_lastOAuthAuthorizeUrl);

    public bool CanCopyAuthorizeUrl => !string.IsNullOrWhiteSpace(_lastOAuthAuthorizeUrl);

    public event EventHandler<NexusLoginResult?>? RequestClose;

    public NexusLoginDialogViewModel(NexusAuthService nexusAuthService, NexusOAuthService nexusOAuthService, string existingApiKey)
    {
        _nexusAuthService = nexusAuthService;
        _nexusOAuthService = nexusOAuthService;
        ApiKey = existingApiKey;
    }

    [RelayCommand]
    private void OpenApiKeyPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://next.nexusmods.com/settings/api-keys",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开浏览器失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ValidateAndLoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "正在验证 Nexus API Key...";

        var result = await _nexusAuthService.ValidateApiKeyAsync(ApiKey);
        if (!result.IsSuccess)
        {
            StatusMessage = result.Message;
            IsBusy = false;
            return;
        }

        StatusMessage = $"验证成功：{result.UserName} ({result.MembershipType})";
        IsBusy = false;

        RequestClose?.Invoke(this, new NexusLoginResult
        {
            ApiKey = ApiKey.Trim(),
            IsOAuthLogin = false,
            UserName = result.UserName,
            MembershipType = result.MembershipType,
            UserId = result.UserId
        });
    }

    [RelayCommand]
    private void StartOAuthAuthorize()
    {
        _oauthStart = _nexusOAuthService.CreateAuthorizationUrl();
        _lastOAuthAuthorizeUrl = _oauthStart.AuthorizeUrl;
        OnPropertyChanged(nameof(CanRetryOpenAuthorizePage));
        OnPropertyChanged(nameof(CanCopyAuthorizeUrl));
        OauthStatusMessage = "已打开授权页面，请完成授权后粘贴回调 URL 或 code";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _oauthStart.AuthorizeUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            OauthStatusMessage = $"打开授权页面失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartOAuthAutoLoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        OauthStatusMessage = "正在启动本地回调监听并打开授权页面...";

        var tokenResult = await _nexusOAuthService.AuthorizeWithLoopbackAsync(url =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });

        IsBusy = false;
        if (!tokenResult.IsSuccess || tokenResult.Token == null)
        {
            _lastOAuthAuthorizeUrl = tokenResult.AuthorizeUrl;
            OnPropertyChanged(nameof(CanRetryOpenAuthorizePage));
            OnPropertyChanged(nameof(CanCopyAuthorizeUrl));

            OauthStatusMessage = tokenResult.FailureReason switch
            {
                NexusOAuthFailureReason.Timeout => "授权等待超时。可点击“重开授权页”继续，或使用手动 code 回填。",
                NexusOAuthFailureReason.ListenerStartFailed => "本地监听端口不可用。请关闭占用进程后重试，或切换手动 code 回填。",
                NexusOAuthFailureReason.UserCancelled => "你在浏览器中取消了授权，可重试授权。",
                NexusOAuthFailureReason.BrowserOpenFailed => "无法自动打开浏览器，请点击“重开授权页”手动打开。",
                _ => tokenResult.Message
            };
            return;
        }

        var profile = tokenResult.Profile;
        OauthStatusMessage = $"OAuth 自动登录成功：{profile.UserName}";

        RequestClose?.Invoke(this, new NexusLoginResult
        {
            IsOAuthLogin = true,
            OAuthAccessToken = tokenResult.Token.AccessToken,
            OAuthRefreshToken = tokenResult.Token.RefreshToken,
            OAuthIdToken = tokenResult.Token.IdToken,
            UserName = profile.UserName,
            MembershipType = profile.MembershipType,
            UserId = profile.UserId
        });
    }

    [RelayCommand]
    private void RetryOpenAuthorizePage()
    {
        if (string.IsNullOrWhiteSpace(_lastOAuthAuthorizeUrl))
        {
            OauthStatusMessage = "当前没有可重开的授权页面，请重新开始 OAuth。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastOAuthAuthorizeUrl,
                UseShellExecute = true
            });
            OauthStatusMessage = "已重开授权页面，请在浏览器完成授权后返回。";
        }
        catch (Exception ex)
        {
            OauthStatusMessage = $"重开授权页面失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyAuthorizeUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastOAuthAuthorizeUrl))
        {
            OauthStatusMessage = "当前没有可复制的授权地址";
            return;
        }

        var clipboard = GetClipboard();
        if (clipboard == null)
        {
            OauthStatusMessage = "当前环境不支持剪贴板";
            return;
        }

        await clipboard.SetTextAsync(_lastOAuthAuthorizeUrl);
        OauthStatusMessage = "授权地址已复制，可粘贴到浏览器打开";
    }

    [RelayCommand]
    private async Task CompleteOAuthLoginAsync()
    {
        if (_oauthStart == null)
        {
            OauthStatusMessage = "请先点击“开始 OAuth 授权”";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        if (!NexusOAuthService.TryExtractCodeFromCallback(OauthCallbackInput, out var code, out var stateFromCallback))
        {
            OauthStatusMessage = "未能解析授权码，请粘贴完整回调 URL 或纯 code";
            return;
        }

        if (!string.IsNullOrWhiteSpace(stateFromCallback) &&
            !string.Equals(stateFromCallback, _oauthStart.State, StringComparison.Ordinal))
        {
            OauthStatusMessage = "state 校验失败，请重新发起授权";
            return;
        }

        IsBusy = true;
        OauthStatusMessage = "正在交换 Token...";

        var tokenResult = await _nexusOAuthService.ExchangeCodeAsync(code, _oauthStart.CodeVerifier, _oauthStart.RedirectUri);
        IsBusy = false;

        if (!tokenResult.IsSuccess || tokenResult.Token == null)
        {
            OauthStatusMessage = tokenResult.Message;
            return;
        }

        var profile = tokenResult.Profile;
        OauthStatusMessage = $"OAuth 登录成功：{profile.UserName}";

        RequestClose?.Invoke(this, new NexusLoginResult
        {
            IsOAuthLogin = true,
            OAuthAccessToken = tokenResult.Token.AccessToken,
            OAuthRefreshToken = tokenResult.Token.RefreshToken,
            OAuthIdToken = tokenResult.Token.IdToken,
            UserName = profile.UserName,
            MembershipType = profile.MembershipType,
            UserId = profile.UserId
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    private static global::Avalonia.Input.Platform.IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }
}