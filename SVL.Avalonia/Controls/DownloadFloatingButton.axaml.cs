using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace SVL.Avalonia.Controls;

public partial class DownloadFloatingButton : UserControl
{
    public static readonly StyledProperty<int> PendingTaskCountProperty =
        AvaloniaProperty.Register<DownloadFloatingButton, int>(nameof(PendingTaskCount), 0);

    public static readonly StyledProperty<ICommand?> OpenQueueCommandProperty =
        AvaloniaProperty.Register<DownloadFloatingButton, ICommand?>(nameof(OpenQueueCommand));

    public int PendingTaskCount
    {
        get => GetValue(PendingTaskCountProperty);
        set => SetValue(PendingTaskCountProperty, value);
    }

    public ICommand? OpenQueueCommand
    {
        get => GetValue(OpenQueueCommandProperty);
        set => SetValue(OpenQueueCommandProperty, value);
    }

    public DownloadFloatingButton()
    {
        InitializeComponent();
    }
}
