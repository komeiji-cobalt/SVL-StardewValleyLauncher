using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace SVL.Avalonia.Controls;

public partial class ModpackDropDialog : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ModpackDropDialog, string>(nameof(Title), "导入 Modpack");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<ModpackDropDialog, string>(nameof(Description), "支持拖拽、粘贴 URL，或输入本地压缩包路径。\n");

    public static readonly StyledProperty<string> SourceInputProperty =
        AvaloniaProperty.Register<ModpackDropDialog, string>(nameof(SourceInput), string.Empty);

    public static readonly StyledProperty<ICommand?> ImportCommandProperty =
        AvaloniaProperty.Register<ModpackDropDialog, ICommand?>(nameof(ImportCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ModpackDropDialog, ICommand?>(nameof(CancelCommand));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string SourceInput
    {
        get => GetValue(SourceInputProperty);
        set => SetValue(SourceInputProperty, value);
    }

    public ICommand? ImportCommand
    {
        get => GetValue(ImportCommandProperty);
        set => SetValue(ImportCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ModpackDropDialog()
    {
        InitializeComponent();
    }
}
