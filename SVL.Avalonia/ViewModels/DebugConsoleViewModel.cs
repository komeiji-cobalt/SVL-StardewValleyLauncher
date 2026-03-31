using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Text;

namespace SVL.Avalonia.ViewModels;

public partial class DebugConsoleViewModel : ObservableObject
{
    private readonly DebugConsoleService _consoleService;

    public ObservableCollection<string> Logs { get; } = [];

    [ObservableProperty]
    private string _logsText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "可在日志区选中并复制文本，或点击导出保存日志文件。";

    public DebugConsoleViewModel(DebugConsoleService consoleService)
    {
        _consoleService = consoleService;
        _consoleService.LineAdded += HandleLineAdded;
        _consoleService.Cleared += HandleCleared;

        foreach (var line in _consoleService.Snapshot())
        {
            Logs.Add(line);
        }

        RebuildLogsText();
    }

    [RelayCommand]
    private void Clear()
    {
        _consoleService.Clear();
        _consoleService.Append("Logs cleared.");
        StatusMessage = "日志已清空。";
    }

    [RelayCommand]
    private void Export()
    {
        if (Logs.Count == 0)
        {
            StatusMessage = "当前没有可导出的日志。";
            return;
        }

        try
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia",
                "Logs");
            Directory.CreateDirectory(exportDir);

            var filePath = Path.Combine(exportDir, $"debug-console-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(filePath, LogsText, Encoding.UTF8);
            StatusMessage = $"已导出日志: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
        }
    }

    private void HandleLineAdded(string line)
    {
        Logs.Add(line);
        while (Logs.Count > 800)
        {
            Logs.RemoveAt(0);
        }

        RebuildLogsText();
    }

    private void HandleCleared()
    {
        Logs.Clear();
        RebuildLogsText();
    }

    private void RebuildLogsText()
    {
        LogsText = string.Join(Environment.NewLine, Logs);
    }
}
