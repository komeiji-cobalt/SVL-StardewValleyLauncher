using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace SVL.Avalonia.Controls;

public partial class FloatingNotificationControl : UserControl
{
    public static readonly StyledProperty<string> LevelTextProperty =
        AvaloniaProperty.Register<FloatingNotificationControl, string>(nameof(LevelText), "信息");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<FloatingNotificationControl, string>(nameof(Message), string.Empty);

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<FloatingNotificationControl, ICommand?>(nameof(DismissCommand));

    public string LevelText
    {
        get => GetValue(LevelTextProperty);
        set => SetValue(LevelTextProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public FloatingNotificationControl()
    {
        InitializeComponent();
    }
}
