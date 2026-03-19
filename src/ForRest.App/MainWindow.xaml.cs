using System.Numerics;
using ForRest.App.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ForRest.App;

public sealed partial class MainWindow : Window
{
    private const double NarrowBreakpoint = 1280;
    private const double WideBreakpoint = 1700;
    private const double LeftPaneMinWidth = 260;
    private const double LeftPaneMaxWidth = 420;
    private const double CenterPaneMinWidth = 460;
    private const double RightPaneMinWidth = 320;
    private const double RightPaneMaxWidth = 640;
    private const double SplitterWidth = 18;

    private bool hasPlayedEntranceAnimations;
    private bool isApplyingLayout;
    private bool isLeftSplitterDragging;
    private bool isRightSplitterDragging;

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
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await ViewModel.Load();
        ApplyWorkbenchLayout();
        PlayEntranceAnimations();
    }

    private void OnRootLayoutSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        ApplyWorkbenchLayout();
    }

    private async void WorkspaceSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is ComboBox { SelectedItem: WorkspaceOptionViewModel workspace })
        {
            await ViewModel.UseWorkspace(workspace);
            ApplyWorkbenchLayout();
        }
    }

    private void ExplorerItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        if (eventArgs.ClickedItem is ExplorerListItemViewModel { RequestId: Guid requestId })
        {
            ViewModel.OpenRequest(requestId);
            if (ViewModel.IsNarrowLayout)
            {
                ViewModel.CloseLeftRail();
            }
        }
    }

    private void HistoryItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        if (eventArgs.ClickedItem is ExecutionRun run)
        {
            ViewModel.ShowHistoryRun(run);
            if (ViewModel.IsNarrowLayout)
            {
                ViewModel.CloseLeftRail();
            }
        }
    }

    private void OpenRequestTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs eventArgs)
    {
        if (eventArgs.Item is RequestTabViewModel request)
        {
            ViewModel.CloseRequest(request);
        }
    }

    private void LeftRailScrimTapped(object sender, TappedRoutedEventArgs eventArgs)
    {
        ViewModel.CloseLeftRail();
    }

    private void LeftPaneThumbDragStarted(object sender, DragStartedEventArgs eventArgs)
    {
        isLeftSplitterDragging = true;
        SetSplitterState(LeftSplitterRail, LeftSplitterGrip, isActive: true);
    }

    private void LeftPaneThumbDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        if (ViewModel.IsNarrowLayout)
        {
            return;
        }

        var nextLeftWidth = ViewModel.LeftPaneWidth + eventArgs.HorizontalChange;
        var nextMiddleWidth = ViewModel.MiddlePaneWidth - eventArgs.HorizontalChange;

        if (nextLeftWidth < LeftPaneMinWidth || nextLeftWidth > LeftPaneMaxWidth || nextMiddleWidth < CenterPaneMinWidth)
        {
            return;
        }

        ViewModel.UpdatePaneWidths(nextLeftWidth, nextMiddleWidth, ViewModel.RightPaneWidth);
        ApplyWorkbenchLayout();
    }

    private async void LeftPaneThumbDragCompleted(object sender, DragCompletedEventArgs eventArgs)
    {
        isLeftSplitterDragging = false;
        SetSplitterState(LeftSplitterRail, LeftSplitterGrip, isActive: false);

        if (!ViewModel.IsNarrowLayout)
        {
            await ViewModel.PersistShellLayout();
        }
    }

    private void RightPaneThumbDragStarted(object sender, DragStartedEventArgs eventArgs)
    {
        isRightSplitterDragging = true;
        SetSplitterState(RightSplitterRail, RightSplitterGrip, isActive: true);
    }

    private void RightPaneThumbDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        if (ViewModel.IsNarrowLayout)
        {
            return;
        }

        var nextMiddleWidth = ViewModel.MiddlePaneWidth + eventArgs.HorizontalChange;
        var nextRightWidth = ViewModel.RightPaneWidth - eventArgs.HorizontalChange;

        if (nextRightWidth < RightPaneMinWidth || nextRightWidth > RightPaneMaxWidth || nextMiddleWidth < CenterPaneMinWidth)
        {
            return;
        }

        ViewModel.UpdatePaneWidths(ViewModel.LeftPaneWidth, nextMiddleWidth, nextRightWidth);
        ApplyWorkbenchLayout();
    }

    private async void RightPaneThumbDragCompleted(object sender, DragCompletedEventArgs eventArgs)
    {
        isRightSplitterDragging = false;
        SetSplitterState(RightSplitterRail, RightSplitterGrip, isActive: false);

        if (!ViewModel.IsNarrowLayout)
        {
            await ViewModel.PersistShellLayout();
        }
    }

    private void OnSplitterPointerEntered(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, LeftPaneThumb))
        {
            SetSplitterState(LeftSplitterRail, LeftSplitterGrip, isActive: true);
        }
        else if (ReferenceEquals(sender, RightPaneThumb))
        {
            SetSplitterState(RightSplitterRail, RightSplitterGrip, isActive: true);
        }
    }

    private void OnSplitterPointerExited(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, LeftPaneThumb) && !isLeftSplitterDragging)
        {
            SetSplitterState(LeftSplitterRail, LeftSplitterGrip, isActive: false);
        }
        else if (ReferenceEquals(sender, RightPaneThumb) && !isRightSplitterDragging)
        {
            SetSplitterState(RightSplitterRail, RightSplitterGrip, isActive: false);
        }
    }

    private void ApplyWorkbenchLayout()
    {
        if (isApplyingLayout || WorkbenchGrid.ActualWidth <= 0)
        {
            return;
        }

        isApplyingLayout = true;

        try
        {
            var layoutMode = RootLayout.ActualWidth >= WideBreakpoint
                ? WorkbenchLayoutMode.Wide
                : RootLayout.ActualWidth >= NarrowBreakpoint
                    ? WorkbenchLayoutMode.Medium
                    : WorkbenchLayoutMode.Narrow;

            ViewModel.SetLayoutMode(layoutMode);
            ApplyRailState();

            if (layoutMode == WorkbenchLayoutMode.Narrow)
            {
                ApplyNarrowLayout();
            }
            else
            {
                ApplyDesktopLayout(layoutMode);
            }
        }
        finally
        {
            isApplyingLayout = false;
        }
    }

    private void ApplyDesktopLayout(WorkbenchLayoutMode layoutMode)
    {
        LeftRailScrim.Visibility = Visibility.Collapsed;
        LeftPaneHost.Visibility = Visibility.Visible;
        LeftSplitterHost.Visibility = Visibility.Visible;
        RightSplitterHost.Visibility = Visibility.Visible;
        ResponsePaneHost.Visibility = Visibility.Visible;

        LeftPaneHost.Margin = new Thickness(12);
        LeftPaneHost.Width = double.NaN;
        LeftPaneHost.MaxWidth = double.PositiveInfinity;
        LeftPaneHost.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid.SetColumn(LeftPaneHost, 0);
        Grid.SetColumnSpan(LeftPaneHost, 1);
        Grid.SetRow(LeftPaneHost, 0);
        Grid.SetRowSpan(LeftPaneHost, 4);

        Grid.SetColumn(LeftSplitterHost, 1);
        Grid.SetColumnSpan(LeftSplitterHost, 1);
        Grid.SetRow(LeftSplitterHost, 0);
        Grid.SetRowSpan(LeftSplitterHost, 4);

        Grid.SetColumn(RequestPaneHost, 2);
        Grid.SetColumnSpan(RequestPaneHost, 1);
        Grid.SetRow(RequestPaneHost, 1);
        Grid.SetRowSpan(RequestPaneHost, 1);

        Grid.SetColumn(RightSplitterHost, 3);
        Grid.SetColumnSpan(RightSplitterHost, 1);
        Grid.SetRow(RightSplitterHost, 0);
        Grid.SetRowSpan(RightSplitterHost, 4);

        Grid.SetColumn(ResponsePaneHost, 4);
        Grid.SetColumnSpan(ResponsePaneHost, 1);
        Grid.SetRow(ResponsePaneHost, 1);
        Grid.SetRowSpan(ResponsePaneHost, 1);

        PrimaryWorkbenchRow.Height = new GridLength(1, GridUnitType.Star);
        NarrowPaneDividerRow.Height = new GridLength(0);
        ResponseWorkbenchRow.Height = new GridLength(0);

        ApplyDesktopPaneWidths(layoutMode);
    }

    private void ApplyDesktopPaneWidths(WorkbenchLayoutMode layoutMode)
    {
        var availableWidth = Math.Max(0, WorkbenchGrid.ActualWidth - (SplitterWidth * 2));
        if (availableWidth <= 0)
        {
            return;
        }

        var leftMax = layoutMode == WorkbenchLayoutMode.Medium ? 340 : LeftPaneMaxWidth;
        var rightMax = layoutMode == WorkbenchLayoutMode.Medium ? 500 : RightPaneMaxWidth;
        var leftWidth = Math.Clamp(ViewModel.LeftPaneWidth, LeftPaneMinWidth, leftMax);
        var rightWidth = Math.Clamp(ViewModel.RightPaneWidth, RightPaneMinWidth, rightMax);
        var middleWidth = Math.Max(CenterPaneMinWidth, availableWidth - leftWidth - rightWidth);
        var overflow = leftWidth + middleWidth + rightWidth - availableWidth;

        if (overflow > 0)
        {
            var rightReduction = Math.Min(overflow, rightWidth - RightPaneMinWidth);
            rightWidth -= rightReduction;
            overflow -= rightReduction;

            if (overflow > 0)
            {
                var leftReduction = Math.Min(overflow, leftWidth - LeftPaneMinWidth);
                leftWidth -= leftReduction;
            }

            middleWidth = Math.Max(CenterPaneMinWidth, availableWidth - leftWidth - rightWidth);
        }
        else
        {
            middleWidth = availableWidth - leftWidth - rightWidth;
        }

        ViewModel.UpdatePaneWidths(leftWidth, middleWidth, rightWidth);
        LeftPaneColumn.Width = new GridLength(leftWidth);
        LeftSplitterColumn.Width = new GridLength(SplitterWidth);
        CenterPaneColumn.Width = new GridLength(middleWidth);
        RightSplitterColumn.Width = new GridLength(SplitterWidth);
        RightPaneColumn.Width = new GridLength(rightWidth);
    }

    private void ApplyNarrowLayout()
    {
        LeftSplitterHost.Visibility = Visibility.Collapsed;
        RightSplitterHost.Visibility = Visibility.Collapsed;
        ResponsePaneHost.Visibility = Visibility.Visible;

        LeftPaneColumn.Width = new GridLength(0);
        LeftSplitterColumn.Width = new GridLength(0);
        CenterPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        RightSplitterColumn.Width = new GridLength(0);
        RightPaneColumn.Width = new GridLength(0);

        PrimaryWorkbenchRow.Height = new GridLength(1, GridUnitType.Star);
        NarrowPaneDividerRow.Height = new GridLength(12);
        ResponseWorkbenchRow.Height = new GridLength(Math.Max(260, Math.Min(420, RootLayout.ActualHeight * 0.34)));

        Grid.SetColumn(RequestPaneHost, 2);
        Grid.SetColumnSpan(RequestPaneHost, 1);
        Grid.SetRow(RequestPaneHost, 1);
        Grid.SetRowSpan(RequestPaneHost, 1);

        Grid.SetColumn(ResponsePaneHost, 2);
        Grid.SetColumnSpan(ResponsePaneHost, 1);
        Grid.SetRow(ResponsePaneHost, 3);
        Grid.SetRowSpan(ResponsePaneHost, 1);

        ConfigureLeftRailOverlay();
    }

    private void ConfigureLeftRailOverlay()
    {
        if (!ViewModel.IsLeftRailOpen)
        {
            LeftRailScrim.Visibility = Visibility.Collapsed;
            LeftPaneHost.Visibility = Visibility.Collapsed;
            return;
        }

        var overlayWidth = Math.Min(
            Math.Max(ViewModel.LeftPaneWidth, LeftPaneMinWidth),
            Math.Max(300, WorkbenchGrid.ActualWidth - 36));

        LeftRailScrim.Visibility = Visibility.Visible;
        LeftPaneHost.Visibility = Visibility.Visible;
        LeftPaneHost.Width = overlayWidth;
        LeftPaneHost.MaxWidth = overlayWidth;
        LeftPaneHost.HorizontalAlignment = HorizontalAlignment.Left;
        LeftPaneHost.Margin = new Thickness(12);

        Grid.SetColumn(LeftPaneHost, 0);
        Grid.SetColumnSpan(LeftPaneHost, 5);
        Grid.SetRow(LeftPaneHost, 0);
        Grid.SetRowSpan(LeftPaneHost, 4);
    }

    private void ApplyRailState()
    {
        LeftRailToggleButton.Visibility = ViewModel.ShowLeftRailToggle ? Visibility.Visible : Visibility.Collapsed;
        ExplorerFilterBoxHost.Visibility = ViewModel.IsExplorerRailVisible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerList.Visibility = ViewModel.IsExplorerRailVisible ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = ViewModel.IsHistoryRailVisible ? Visibility.Visible : Visibility.Collapsed;
        SetRailButtonState(ExplorerRailButton, ViewModel.IsExplorerRailSelected);
        SetRailButtonState(HistoryRailButton, ViewModel.IsHistoryRailSelected);
    }

    private void SetRailButtonState(Button button, bool isActive)
    {
        var resources = Application.Current.Resources;
        button.Background = (Brush)resources[isActive ? "ShellAccentSoftBrush" : "ShellPanelAltBrush"];
        button.BorderBrush = (Brush)resources[isActive ? "ShellAccentBrush" : "ShellPanelAltBrush"];
        button.BorderThickness = isActive ? new Thickness(1) : new Thickness(0);
        button.Opacity = isActive ? 1 : 0.82;
    }

    private void ConfigureWindowChrome()
    {
        SystemBackdrop = new MicaBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
        }

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private void PlayEntranceAnimations()
    {
        if (hasPlayedEntranceAnimations)
        {
            return;
        }

        hasPlayedEntranceAnimations = true;
        AnimateEntrance(LeftPaneHost, new Vector3(-28, 0, 0), 0);
        AnimateEntrance(ShellTopStrip, new Vector3(0, -18, 0), 40);
        AnimateEntrance(RequestPaneHost, new Vector3(0, 24, 0), 80);
        AnimateEntrance(ResponsePaneHost, new Vector3(28, 0, 0), 120);
    }

    private static void AnimateEntrance(UIElement element, Vector3 initialOffset, int delayMilliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        visual.Opacity = 0;
        visual.Offset = initialOffset;

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMilliseconds);
        opacityAnimation.Duration = TimeSpan.FromMilliseconds(260);
        opacityAnimation.InsertKeyFrame(1f, 1f);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMilliseconds);
        offsetAnimation.Duration = TimeSpan.FromMilliseconds(320);
        offsetAnimation.InsertKeyFrame(1f, Vector3.Zero);

        visual.StartAnimation(nameof(visual.Opacity), opacityAnimation);
        visual.StartAnimation(nameof(visual.Offset), offsetAnimation);
    }

    private static void SetSplitterState(FrameworkElement rail, FrameworkElement grip, bool isActive)
    {
        AnimateOpacity(rail, isActive ? 0.74f : 0.36f, 140);
        AnimateOpacity(grip, isActive ? 1f : 0.52f, 140);
        AnimateScale(grip, isActive ? 1.18f : 1f, 180);
    }

    private static void AnimateOpacity(UIElement element, float targetOpacity, int durationMilliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        animation.InsertKeyFrame(1f, targetOpacity);
        visual.StartAnimation(nameof(visual.Opacity), animation);
    }

    private static void AnimateScale(FrameworkElement element, float targetScale, int durationMilliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3(
            (float)Math.Max(1, element.ActualWidth / 2),
            (float)Math.Max(1, element.ActualHeight / 2),
            0);

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        animation.InsertKeyFrame(1f, new Vector3(targetScale, targetScale, 1f));
        visual.StartAnimation(nameof(visual.Scale), animation);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.LeftPaneWidth)
            or nameof(MainWindowViewModel.MiddlePaneWidth)
            or nameof(MainWindowViewModel.RightPaneWidth)
            or nameof(MainWindowViewModel.IsLeftRailOpen)
            or nameof(MainWindowViewModel.IsExplorerRailSelected)
            or nameof(MainWindowViewModel.IsHistoryRailSelected)
            or nameof(MainWindowViewModel.ShowLeftRailToggle))
        {
            ApplyWorkbenchLayout();
        }
    }
}
