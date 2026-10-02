namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;

public sealed partial class LibraryView
{
    public LibraryViewModel ViewModel { get; } = App.Services.GetRequiredService<LibraryViewModel>();

    public LibraryView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }
}
