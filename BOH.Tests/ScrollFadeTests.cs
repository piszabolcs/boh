using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BOH.Views;

namespace BOH.Tests;

public class ScrollFadeTests
{
    [Fact]
    public void FadesOnlyTheEdgesWithHiddenContent()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewer = new ScrollViewer { Content = new Border { Height = 600 } };
                ScrollFade.SetIsEnabled(viewer, true);
                Layout(viewer, 200);
                var presenter = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);

                // At the top: only the bottom edge fades.
                var mask = Assert.IsType<LinearGradientBrush>(presenter.OpacityMask);
                Assert.Equal(255, mask.GradientStops[0].Color.A);
                Assert.Equal(0, mask.GradientStops[3].Color.A);

                // In the middle: both edges fade.
                viewer.ScrollToVerticalOffset(200);
                Layout(viewer, 200);
                Assert.Equal(0, mask.GradientStops[0].Color.A);
                Assert.Equal(0, mask.GradientStops[3].Color.A);

                // Everything fits: no mask at all.
                Layout(viewer, 800);
                Assert.Null(presenter.OpacityMask);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void Layout(FrameworkElement element, double height)
    {
        element.Measure(new Size(300, height));
        element.Arrange(new Rect(0, 0, 300, height));
        element.UpdateLayout();
    }
}
