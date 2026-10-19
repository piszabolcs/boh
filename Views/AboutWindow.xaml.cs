using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace BOH.Views;

/// <summary>The "app info" card opened from the footer signature.</summary>
public partial class AboutWindow : Window
{
    private bool _closing;

    public AboutWindow() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        base.OnKeyDown(e);
    }

    // A click outside (e.g. on the main window) deactivates the card, which closes it.
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_closing)
            Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
