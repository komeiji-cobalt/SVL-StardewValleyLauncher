using Avalonia.Controls;
using Avalonia;
using SVL.Avalonia.Models;
using SVL.Avalonia.ViewModels;

namespace SVL.Avalonia.Controls;

public partial class NexusLoginDialog : Window
{
    public NexusLoginDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is NexusLoginDialogViewModel viewModel)
        {
            viewModel.RequestClose -= HandleRequestClose;
            viewModel.RequestClose += HandleRequestClose;
            _ = viewModel.InitializeAsync();
        }
    }

    private void HandleRequestClose(object? sender, NexusLoginResult? result)
    {
        Close(result);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is NexusLoginDialogViewModel viewModel)
        {
            viewModel.RequestClose -= HandleRequestClose;
        }

        base.OnClosed(e);
    }
}
