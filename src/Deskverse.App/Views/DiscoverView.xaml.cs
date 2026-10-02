namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;

public sealed partial class DiscoverView
{
    public DiscoverViewModel ViewModel { get; } = App.Services.GetRequiredService<DiscoverViewModel>();

    public DiscoverView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }
}
