using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BOH.ViewModels;

namespace BOH.Views;

public partial class MainWindow : Window
{
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        // The log is oldest-first; keep the newest line in view.
        viewModel.LogEntries.CollectionChanged += (_, _) => LogScroll.ScrollToEnd();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        RootBorder.Margin = WindowState == WindowState.Maximized ? MaximizedFrame() : new Thickness(0);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_viewModel.IsDirty)
            return;
        var answer = MessageBox.Show(this, "You have unsaved changes. Save them before closing?",
                                     "Backup O Horse", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !_viewModel.TrySave()))
            e.Cancel = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void SignatureClick(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.Show();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Long paths: show the end (the folder name) while the box is not being edited.</summary>
    private void PathBox_ShowEnd(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocused)
            box.Dispatcher.InvokeAsync(() => box.ScrollToHorizontalOffset(double.MaxValue), DispatcherPriority.Background);
    }

    /// <summary>
    /// A maximized window is sized to the work area plus its invisible resize frame. With a custom title bar the
    /// content would reach into that frame, so it is padded by the frame size: the visible window then matches the
    /// work area and the taskbar stays uncovered.
    /// </summary>
    private Thickness MaximizedFrame()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
        var padding = GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpiValue);
        double x = (GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpiValue) + padding) / dpi.DpiScaleX;
        double y = (GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpiValue) + padding) / dpi.DpiScaleY;
        return new Thickness(x, y, x, y);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
