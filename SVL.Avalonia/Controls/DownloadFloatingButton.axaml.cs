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

    public static readonly DirectProperty<DownloadFloatingButton, bool> HasPendingTasksProperty =
        AvaloniaProperty.RegisterDirect<DownloadFloatingButton, bool>(
            nameof(HasPendingTasks),
            button => button.HasPendingTasks);

    private bool _hasPendingTasks;

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

    public bool HasPendingTasks
    {
        get => _hasPendingTasks;
        private set => SetAndRaise(HasPendingTasksProperty, ref _hasPendingTasks, value);
    }

    public DownloadFloatingButton()
    {
        InitializeComponent();

        HasPendingTasks = PendingTaskCount > 0;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PendingTaskCountProperty)
        {
            HasPendingTasks = PendingTaskCount > 0;
        }
    }
}
