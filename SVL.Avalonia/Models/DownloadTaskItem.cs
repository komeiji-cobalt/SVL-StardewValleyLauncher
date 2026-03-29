using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace SVL.Avalonia.Models;

public enum DownloadTaskKind
{
    Generic,
    NxmMod,
    NxmCollection
}

public partial class DownloadTaskItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private DownloadTaskKind _taskKind = DownloadTaskKind.Generic;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    [ObservableProperty]
    private string _outputFilePath = string.Empty;

    [ObservableProperty]
    private string _installedPath = string.Empty;

    [ObservableProperty]
    private string _reportPath = string.Empty;

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private string _failedDetails = string.Empty;

    [ObservableProperty]
    private string _retryReportPath = string.Empty;

    [ObservableProperty]
    private string _statusIconSource = string.Empty;

    public bool HasReportPath => !string.IsNullOrWhiteSpace(ReportPath);

    public bool HasBackupPath => !string.IsNullOrWhiteSpace(BackupPath);

    public bool HasFailedDetails => !string.IsNullOrWhiteSpace(FailedDetails);

    public bool HasRetryReportPath => !string.IsNullOrWhiteSpace(RetryReportPath);

    public bool IsRunning =>
        Status.Contains("下载中", StringComparison.Ordinal) ||
        Status.Contains("安装中", StringComparison.Ordinal) ||
        Status.Contains("解析", StringComparison.Ordinal) ||
        Status.Contains("获取", StringComparison.Ordinal);

    public bool IsFailed =>
        Status.Contains("失败", StringComparison.Ordinal) ||
        Status.Contains("可重试", StringComparison.Ordinal) ||
        Status.Contains("中断", StringComparison.Ordinal);

    public bool IsFinished =>
        Status.Contains("已完成", StringComparison.Ordinal) ||
        Status.Contains("安装完成", StringComparison.Ordinal) ||
        Status.Contains("已取消", StringComparison.Ordinal) ||
        IsFailed;

    public string DisplayStatusText
    {
        get
        {
            if (Status.Contains("已加入队列", StringComparison.Ordinal) || Status.Contains("等待下载", StringComparison.Ordinal))
            {
                return "排队中";
            }

            if (Status.Contains("下载中", StringComparison.Ordinal) || Status.Contains("安装中", StringComparison.Ordinal))
            {
                return Status;
            }

            if (Status.Contains("失败", StringComparison.Ordinal) || Status.Contains("可重试", StringComparison.Ordinal))
            {
                return "失败，可重试";
            }

            if (Status.Contains("已取消", StringComparison.Ordinal) || Status.Contains("安装已取消", StringComparison.Ordinal))
            {
                return "已取消";
            }

            if (Status.Contains("完成", StringComparison.Ordinal))
            {
                return "已完成";
            }

            return Status;
        }
    }

    partial void OnReportPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasReportPath));
    }

    partial void OnBackupPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasBackupPath));
    }

    partial void OnFailedDetailsChanged(string value)
    {
        OnPropertyChanged(nameof(HasFailedDetails));
    }

    partial void OnRetryReportPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasRetryReportPath));
    }

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(DisplayStatusText));
    }

    public List<string> DependencyUrls { get; set; } = [];

    public List<string> FailedDownloadUrls { get; set; } = [];

    public List<string> ConflictPreviewItems { get; set; } = [];
}
