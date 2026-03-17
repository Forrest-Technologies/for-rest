using ForRest.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ForRest.App;

public sealed partial class MainWindow : Window
{
    #region Constructors

    public MainWindow()
        : this((MainWindowViewModel)((App)Application.Current).Services.GetRequiredService(typeof(MainWindowViewModel)))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ConfigureWindowChrome();
        RootLayout.DataContext = ViewModel;
    }

    #endregion

    #region Properties

    public MainWindowViewModel ViewModel { get; }

    #endregion

    #region Event Handlers

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await ViewModel.Load();
    }

    private async void WorkspaceSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is ComboBox { SelectedItem: WorkspaceOptionViewModel workspace })
        {
            await ViewModel.UseWorkspace(workspace);
        }
    }

    private void ExplorerItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        if (eventArgs.ClickedItem is ExplorerListItemViewModel { RequestId: Guid requestId })
        {
            ViewModel.OpenRequest(requestId);
        }
    }

    private void HistoryItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        if (eventArgs.ClickedItem is ExecutionRun run)
        {
            ViewModel.ShowHistoryRun(run);
        }
    }

    #endregion

    #region Private Methods

    private void ConfigureWindowChrome()
    {
        SystemBackdrop = new MicaBackdrop
        {
        };

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        ExtendsContentIntoTitleBar = true;
        if (RootLayout.FindName("TitleBarDragRegion") is UIElement titleBarRegion)
        {
            SetTitleBar(titleBarRegion);
        }

        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 154, 164, 178);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 36, 42, 52);
        AppWindow.TitleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 52, 60, 74);
        AppWindow.TitleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
    }

    #endregion
}
