using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using SVL.Avalonia.Controls;
using SVL.Avalonia.Models;
using SVL.Avalonia.ViewModels;
using System.Diagnostics;
using System.Windows.Input;
using System.Threading.Tasks;

namespace SVL.Avalonia.Services;

public enum ModpackFailureDialogAction
{
    Close,
    Retry
}

public sealed class DialogService
{
    private static Window? GetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dialog = new ConfirmDialog
        {
            DataContext = new ConfirmDialogModel { Title = title, Message = message }
        };

        var owner = GetMainWindow();
        if (owner == null)
        {
            return false;
        }

        var result = await dialog.ShowDialog<bool>(owner);
        return result;
    }

    public async Task<string?> ShowInputAsync(string title, string message, string defaultValue = "")
    {
        var dialog = new InputDialog
        {
            DataContext = new InputDialogModel { Title = title, Message = message, Value = defaultValue }
        };

        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        return await dialog.ShowDialog<string?>(owner);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new SvlMessageBox
        {
            DataContext = new SvlMessageBoxModel { Title = title, Message = message }
        };

        var owner = GetMainWindow();
        if (owner == null)
        {
            return;
        }

        await dialog.ShowDialog(owner);
    }

    public async Task<NexusLoginResult?> ShowNexusLoginAsync(string existingApiKey, NexusAuthService nexusAuthService, NexusOAuthService nexusOAuthService)
    {
        var dialog = new NexusLoginDialog
        {
            DataContext = new NexusLoginDialogViewModel(nexusAuthService, nexusOAuthService, existingApiKey)
        };

        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        return await dialog.ShowDialog<NexusLoginResult?>(owner);
    }

    public async Task<UpdateDialogAction> ShowUpdateDialogAsync(Version currentVersion, LauncherReleaseInfo releaseInfo, string source)
    {
        var dialog = new UpdateDialog(currentVersion, releaseInfo, source);

        var owner = GetMainWindow();
        if (owner == null)
        {
            return UpdateDialogAction.Later;
        }

        var result = await dialog.ShowDialog<UpdateDialogAction?>(owner);
        return result ?? UpdateDialogAction.Later;
    }

    public async Task<string?> ShowInstanceSelectionDialogAsync(
        IEnumerable<string> instances,
        string title = "选择实例",
        string? selectedInstance = null)
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        var instanceList = (instances ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dialog = new InstanceSelectionDialog
        {
            Title = title,
            Instances = instanceList,
            SelectedInstance = string.IsNullOrWhiteSpace(selectedInstance)
                ? instanceList.FirstOrDefault()
                : selectedInstance
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.ConfirmCommand = new DelegateCommand(_ => host.Close(dialog.SelectedInstance));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(null));

        return await host.ShowDialog<string?>(owner);
    }

    public async Task<string?> ShowGamePathSelectionDialogAsync(
        string currentPath = "",
        string title = "选择游戏路径",
        string description = "未自动探测到 Stardew Valley，请手动选择游戏目录。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        var dialog = new GamePathSelectionDialog
        {
            Title = title,
            Description = description,
            SelectedPath = currentPath
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.BrowseCommand = new DelegateCommand(async _ =>
        {
            var selected = await PickFolderPathAsync(owner, "选择游戏目录");
            if (!string.IsNullOrWhiteSpace(selected))
            {
                dialog.SelectedPath = selected;
            }
        });
        dialog.ConfirmCommand = new DelegateCommand(_ => host.Close(dialog.SelectedPath?.Trim()));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(null));

        return await host.ShowDialog<string?>(owner);
    }

    public async Task<bool> ShowGamePathConfirmDialogAsync(
        string pathToConfirm,
        string title = "确认游戏路径",
        string message = "请确认此目录为 Stardew Valley 游戏安装目录。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return false;
        }

        var dialog = new GamePathConfirmDialog
        {
            Title = title,
            Message = message,
            PathToConfirm = pathToConfirm
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.ConfirmCommand = new DelegateCommand(_ => host.Close(true));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(false));

        return await host.ShowDialog<bool>(owner);
    }

    public async Task<string?> ShowInstanceNameDialogAsync(string title, string initialName = "")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        var dialog = new InstanceNameDialog
        {
            Title = title,
            InstanceName = initialName
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.ConfirmCommand = new DelegateCommand(_ => host.Close(dialog.InstanceName?.Trim()));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(null));

        return await host.ShowDialog<string?>(owner);
    }

    public async Task<bool> ShowDependencyEnableDialogAsync(
        IEnumerable<string> dependencies,
        string title = "启用依赖",
        string message = "检测到当前资源依赖以下模组，建议一并启用。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return false;
        }

        var dependencyList = (dependencies ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dialog = new DependencyEnableDialog
        {
            Title = title,
            Message = message,
            Dependencies = dependencyList
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.EnableCommand = new DelegateCommand(_ => host.Close(true));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(false));

        return await host.ShowDialog<bool>(owner);
    }

    public async Task<bool> ShowModUpdateConfirmDialogAsync(
        IEnumerable<string> updateItems,
        string title = "确认更新 Mod",
        string summary = "以下 Mod 将执行更新。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return false;
        }

        var itemList = (updateItems ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dialog = new ModUpdateConfirmDialog
        {
            Title = title,
            Summary = summary,
            UpdateItems = itemList
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.ConfirmCommand = new DelegateCommand(_ => host.Close(true));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(false));

        return await host.ShowDialog<bool>(owner);
    }

    public async Task<string?> ShowModpackDropDialogAsync(
        string sourceInput = "",
        string title = "导入 Modpack",
        string description = "支持拖拽、粘贴 URL，或输入本地压缩包路径。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        var dialog = new ModpackDropDialog
        {
            Title = title,
            Description = description,
            SourceInput = sourceInput
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.ImportCommand = new DelegateCommand(_ => host.Close(dialog.SourceInput?.Trim()));
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(null));

        return await host.ShowDialog<string?>(owner);
    }

    public async Task<bool> ShowNestedFolderFixDialogAsync(
        string sourceFolder,
        string targetFolder,
        string title = "嵌套目录修复",
        string message = "检测到压缩包目录层级异常，可自动整理到正确目录结构。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return false;
        }

        var dialog = new NestedFolderFixDialog
        {
            Title = title,
            Message = message,
            SourceFolder = sourceFolder,
            TargetFolder = targetFolder
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.FixCommand = new DelegateCommand(_ => host.Close(true));
        dialog.SkipCommand = new DelegateCommand(_ => host.Close(false));

        return await host.ShowDialog<bool>(owner);
    }

    public async Task<ModpackFailureDialogAction> ShowModpackFailureDialogAsync(
        string failureReason,
        string logPath,
        string title = "Modpack 安装失败")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return ModpackFailureDialogAction.Close;
        }

        var dialog = new ModpackFailureDialog
        {
            Title = title,
            FailureReason = failureReason,
            LogPath = logPath
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.OpenLogCommand = new DelegateCommand(_ => TryOpenExternal(logPath));
        dialog.RetryCommand = new DelegateCommand(_ => host.Close(ModpackFailureDialogAction.Retry));
        dialog.CloseCommand = new DelegateCommand(_ => host.Close(ModpackFailureDialogAction.Close));

        return await host.ShowDialog<ModpackFailureDialogAction>(owner);
    }

    public async Task ShowBrowserDownloadGuideDialogAsync(
        string downloadUrl,
        string title = "浏览器下载指引",
        string guideMessage = "该资源需要在浏览器完成授权或手动下载。请打开链接并完成下载后返回。")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return;
        }

        var dialog = new BrowserDownloadGuideDialog
        {
            Title = title,
            GuideMessage = guideMessage,
            DownloadUrl = downloadUrl
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.OpenInBrowserCommand = new DelegateCommand(_ =>
        {
            TryOpenExternal(downloadUrl);
            host.Close();
        });
        dialog.CopyLinkCommand = new DelegateCommand(async _ =>
        {
            if (owner.Clipboard != null && !string.IsNullOrWhiteSpace(downloadUrl))
            {
                await owner.Clipboard.SetTextAsync(downloadUrl);
            }

            host.Close();
        });

        await host.ShowDialog(owner);
    }

    public async Task<string?> ShowIconPickerDialogAsync(
        string title = "选择图标")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return null;
        }

        var options = new List<IconPickerOption>
        {
            new() { Name = "经典", IconPath = "avares://SVL.Avalonia/Assets/Icons/Vanilla.png" },
            new() { Name = "模组", IconPath = "avares://SVL.Avalonia/Assets/Icons/Modded.png" },
            new() { Name = "祝尼魔", IconPath = "avares://SVL.Avalonia/Assets/Icons/Junimo.png" },
            new() { Name = "绿色祝尼魔", IconPath = "avares://SVL.Avalonia/Assets/Icons/Junimo2.png" },
            new() { Name = "上传", IconPath = string.Empty, IsCustom = true }
        };

        var dialog = new IconPickerDialog
        {
            Title = title,
            Options = options
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.SelectOptionCommand = new DelegateCommand(async parameter =>
        {
            if (parameter is not IconPickerOption option)
            {
                return;
            }

            if (option.IsCustom)
            {
                var customPath = await PickFilePathAsync(
                    owner,
                    "选择 PNG 图标",
                    [new FilePickerFileType("PNG 图像") { Patterns = ["*.png"], MimeTypes = ["image/png"] }]);
                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    host.Close(customPath);
                }

                return;
            }

            host.Close(option.IconPath);
        });
        dialog.CancelCommand = new DelegateCommand(_ => host.Close(null));

        return await host.ShowDialog<string?>(owner);
    }

    public async Task ShowWindowTitleHelpDialogAsync(
        string helpText,
        string title = "窗口标题帮助")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return;
        }

        var dialog = new WindowTitleHelpDialog
        {
            Title = title,
            HelpText = helpText
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.CloseCommand = new DelegateCommand(_ => host.Close());

        await host.ShowDialog(owner);
    }

    public async Task ShowLocalModDetailDialogAsync(
        string modName,
        string version,
        string author,
        string description,
        string? folderPath = null,
        string title = "本地 Mod 详情")
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            return;
        }

        var dialog = new LocalModDetailDialog
        {
            Title = title,
            ModName = modName,
            Version = version,
            Author = author,
            Description = description
        };

        var host = CreateHostedDialogWindow(title, dialog);
        dialog.OpenFolderCommand = new DelegateCommand(_ =>
        {
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                TryOpenExternal(folderPath);
            }
        });
        dialog.CloseCommand = new DelegateCommand(_ => host.Close());

        await host.ShowDialog(owner);
    }

    private static Window CreateHostedDialogWindow(string title, Control content)
    {
        return new Window
        {
            Title = string.IsNullOrWhiteSpace(title) ? "对话框" : title,
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
    }

    private static async Task<string?> PickFolderPathAsync(Window owner, string title)
    {
        var items = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        var folder = items.FirstOrDefault();
        if (folder?.Path == null)
        {
            return null;
        }

        var uri = folder.Path;
        return uri.IsAbsoluteUri ? Uri.UnescapeDataString(uri.LocalPath) : uri.ToString();
    }

    private static async Task<string?> PickFilePathAsync(
        Window owner,
        string title,
        IReadOnlyList<FilePickerFileType>? fileTypes = null)
    {
        var items = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes
        });

        var file = items.FirstOrDefault();
        if (file?.Path == null)
        {
            return null;
        }

        var uri = file.Path;
        return uri.IsAbsoluteUri ? Uri.UnescapeDataString(uri.LocalPath) : uri.ToString();
    }

    private static void TryOpenExternal(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pathOrUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failures for optional UX actions.
        }
    }

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public DelegateCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
