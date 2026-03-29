using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace SVL.Avalonia.Controls;

public partial class LocalModDetailDialog : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, string>(nameof(Title), "本地 Mod 详情");

    public static readonly StyledProperty<string> ModNameProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, string>(nameof(ModName), string.Empty);

    public static readonly StyledProperty<string> VersionProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, string>(nameof(Version), string.Empty);

    public static readonly StyledProperty<string> AuthorProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, string>(nameof(Author), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<ICommand?> OpenFolderCommandProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, ICommand?>(nameof(OpenFolderCommand));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<LocalModDetailDialog, ICommand?>(nameof(CloseCommand));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string ModName
    {
        get => GetValue(ModNameProperty);
        set => SetValue(ModNameProperty, value);
    }

    public string Version
    {
        get => GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public string Author
    {
        get => GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ICommand? OpenFolderCommand
    {
        get => GetValue(OpenFolderCommandProperty);
        set => SetValue(OpenFolderCommandProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public LocalModDetailDialog()
    {
        InitializeComponent();
    }
}
