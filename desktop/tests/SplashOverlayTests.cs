using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class SplashOverlayTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("stopping splash collapses it and removes the animation", delegate {
            RunSta(delegate {
                SplashOverlay splash = new SplashOverlay(Geometry.Parse("M 0,0 L 10,0 10,10 0,10 Z"));

                splash.StartAnimated();
                AssertEx.Equal(Visibility.Visible, splash.Visibility);
                AssertEx.True(splash.IsAnimationRunning);

                splash.StopAndHide();
                AssertEx.Equal(Visibility.Collapsed, splash.Visibility);
                AssertEx.False(splash.IsAnimationRunning);
            });
        });

        runner.Add("splash reveal reaches the full identity without hiding it again", delegate {
            RunSta(delegate {
                SplashOverlay splash = new SplashOverlay(Geometry.Parse("M 0,0 L 10,0 10,10 0,10 Z"));
                Grid revealHost = (Grid)splash.Children[0];
                Grid revealViewport = (Grid)revealHost.Children[0];
                Window host = new Window
                {
                    Width = 500,
                    Height = 300,
                    Content = splash,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };

                host.Show();
                splash.StartAnimated();
                PumpDispatcher(TimeSpan.FromMilliseconds(1400));

                AssertEx.Equal(420.0, revealViewport.Width);
                AssertEx.Equal<Brush>(null, revealViewport.OpacityMask);
                host.Close();
            });
        });
    }

    private static void RunSta(Action action)
    {
        Exception failure = null;
        Thread thread = new Thread(new ThreadStart(delegate {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += delegate {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
