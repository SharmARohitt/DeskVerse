namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public sealed partial class StudioView
{
    public StudioViewModel ViewModel { get; } = App.Services.GetRequiredService<StudioViewModel>();

    public StudioView()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }
}
