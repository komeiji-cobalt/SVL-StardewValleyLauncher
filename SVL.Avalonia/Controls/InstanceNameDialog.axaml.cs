using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace SVL.Avalonia.Controls;

public partial class InstanceNameDialog : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<InstanceNameDialog, string>(nameof(Title), "实例命名");

    public static readonly StyledProperty<string> InstanceNameProperty =
        AvaloniaProperty.Register<InstanceNameDialog, string>(nameof(InstanceName), string.Empty);

    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<InstanceNameDialog, ICommand?>(nameof(ConfirmCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<InstanceNameDialog, ICommand?>(nameof(CancelCommand));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string InstanceName
    {
        get => GetValue(InstanceNameProperty);
        set => SetValue(InstanceNameProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public InstanceNameDialog()
    {
        InitializeComponent();
    }
}
