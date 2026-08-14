using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal sealed class MainWindow : Window
{
    private readonly AppPaths paths;
    private readonly WebView2 webView;
    private readonly SplashOverlay splashOverlay;
    private readonly Grid errorOverlay;
    private readonly DesktopPresentationState presentationState;
    private readonly RetryableAsyncOperation initialization = new RetryableAsyncOperation();
    private Uri allowedOrigin;

    internal MainWindow(AppPaths paths)
    {
        if (paths == null) throw new ArgumentNullException("paths");
        this.paths = paths;
        presentationState = new DesktopPresentationState();

        Title = AppAssets.ApplicationTitle;
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = AppAssets.BackgroundBrush;
        Foreground = AppAssets.ForegroundBrush;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Icon = AppAssets.LoadWindowIcon(AppDomain.CurrentDomain.BaseDirectory);

        Grid root = new Grid { Background = AppAssets.BackgroundBrush };
        Content = root;

        webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Hidden,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 15, 20)
        };
        AutomationProperties.SetName(webView, "DeepSeek Harness web content");
        root.Children.Add(webView);

        Geometry whaleGeometry = AppAssets.LoadWhaleGeometry(AppDomain.CurrentDomain.BaseDirectory);
        splashOverlay = new SplashOverlay(whaleGeometry);
        Panel.SetZIndex(splashOverlay, 10);
        root.Children.Add(splashOverlay);

        errorOverlay = new Grid
        {
            Background = AppAssets.BackgroundBrush,
            Visibility = Visibility.Collapsed
        };
        KeyboardNavigation.SetTabNavigation(errorOverlay, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(errorOverlay, "DeepSeek Harness error");
        Panel.SetZIndex(errorOverlay, 20);
        root.Children.Add(errorOverlay);
    }

    internal Task InitializeWebViewAsync()
    {
        VerifyAccess();
        return initialization.Run(InitializeWebViewCoreAsync);
    }

    internal async Task NavigateToDsh(Uri uri)
    {
        VerifyAccess();
        if (uri == null) throw new ArgumentNullException("uri");
        if (!NavigationPolicy.CanOpenExternally(uri))
            throw new ArgumentException("The DSH URI must use HTTP or HTTPS.", "uri");

        allowedOrigin = new Uri(uri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        await InitializeWebViewAsync();
        webView.CoreWebView2.Navigate(uri.AbsoluteUri);
    }

    internal void ShowAnimatedSplash()
    {
        VerifyAccess();
        presentationState.ShowAnimatedSplash();
        DesktopWindowChrome.Apply(this, presentationState.WindowChrome);
        errorOverlay.Visibility = Visibility.Collapsed;
        webView.Visibility = Visibility.Hidden;
        splashOverlay.StartAnimated();
        ShowWindow();
    }

    internal void ShowStaticWaiting()
    {
        VerifyAccess();
        presentationState.ShowStaticWaiting();
        DesktopWindowChrome.Apply(this, presentationState.WindowChrome);
        errorOverlay.Visibility = Visibility.Collapsed;
        webView.Visibility = Visibility.Hidden;
        splashOverlay.ShowStatic();
        ShowWindow();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ActivateAndFocus();
    }

    internal void ShowWebContent()
    {
        PresentWebContent(true);
    }

    internal void PresentWebContent(bool revealWindow)
    {
        VerifyAccess();
        presentationState.ShowWebContent();
        DesktopWindowChrome.Apply(this, presentationState.WindowChrome);
        splashOverlay.StopAndHide();
        errorOverlay.Visibility = Visibility.Collapsed;
        webView.Visibility = Visibility.Visible;
        if (revealWindow) ShowWindow();
    }

    internal void ShowError(
        string title,
        string detail,
        Action retry,
        Action viewLogs,
        Action exit)
    {
        PresentError(title, detail, retry, viewLogs, exit, true);
    }

    internal void PresentError(
        string title,
        string detail,
        Action retry,
        Action viewLogs,
        Action exit,
        bool revealWindow)
    {
        VerifyAccess();
        if (retry == null) throw new ArgumentNullException("retry");
        if (viewLogs == null) throw new ArgumentNullException("viewLogs");
        if (exit == null) throw new ArgumentNullException("exit");

        presentationState.ShowError();
        DesktopWindowChrome.Apply(this, presentationState.WindowChrome);
        splashOverlay.StopAndHide();
        webView.Visibility = Visibility.Hidden;
        errorOverlay.Children.Clear();

        StackPanel content = new StackPanel
        {
            Width = 640,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new Border
        {
            Width = 46,
            Height = 3,
            Background = AppAssets.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 25)
        });
        content.Children.Add(new TextBlock
        {
            Text = String.IsNullOrWhiteSpace(title) ? "DeepSeek Harness could not start" : title,
            Foreground = AppAssets.ForegroundBrush,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = detail ?? String.Empty,
            Foreground = AppAssets.MutedForegroundBrush,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 15,
            LineHeight = 23,
            Margin = new Thickness(0, 15, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 31, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Button retryButton = CreateActionButton("Retry", true, retry);
        actions.Children.Add(retryButton);
        actions.Children.Add(CreateActionButton("View Logs", false, viewLogs));
        actions.Children.Add(CreateActionButton("Exit", false, exit));
        content.Children.Add(actions);
        errorOverlay.Children.Add(content);
        errorOverlay.Visibility = Visibility.Visible;
        if (revealWindow)
        {
            ShowWindow();
            retryButton.Focus();
        }
    }

    internal void RestoreWithoutAnimation()
    {
        VerifyAccess();
        presentationState.RestoreWithoutAnimation();
        DesktopWindowChrome.Apply(this, presentationState.WindowChrome);
        if (presentationState.Surface == DesktopSurface.Error)
        {
            splashOverlay.StopAndHide();
            webView.Visibility = Visibility.Hidden;
            errorOverlay.Visibility = Visibility.Visible;
        }
        else if (presentationState.Surface == DesktopSurface.Waiting)
        {
            webView.Visibility = Visibility.Hidden;
            errorOverlay.Visibility = Visibility.Collapsed;
            splashOverlay.ShowStatic();
        }
        else
        {
            splashOverlay.StopAndHide();
            errorOverlay.Visibility = Visibility.Collapsed;
            webView.Visibility = Visibility.Visible;
        }

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ShowWindow();
        ActivateAndFocus();
    }

    private void ActivateAndFocus()
    {
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        splashOverlay.StopAndHide();
        if (webView.CoreWebView2 != null)
        {
            webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        }
        webView.Dispose();
        base.OnClosed(eventArgs);
    }

    private async Task InitializeWebViewCoreAsync()
    {
        Directory.CreateDirectory(paths.WebView2Data);
        AppLogger.WriteDesktop(paths, "Creating the WebView2 environment.");
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, paths.WebView2Data);
        AppLogger.WriteDesktop(paths, "WebView2 environment created; loading the invisible preload host.");
        await InvisibleWindowPreloader.PrepareAsync(new MainWindowPreloadHost(this));
        AppLogger.WriteDesktop(paths, "Invisible preload host loaded; ensuring the WebView2 control.");
        await webView.EnsureCoreWebView2Async(environment);
        AppLogger.WriteDesktop(paths, "WebView2 control initialized.");
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
        webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
    }

    private sealed class MainWindowPreloadHost : IInvisiblePreloadWindow
    {
        private readonly MainWindow window;

        internal MainWindowPreloadHost(MainWindow window)
        {
            this.window = window;
        }

        public bool IsLoaded { get { return window.IsLoaded; } }
        public bool ShowInTaskbar
        {
            get { return window.ShowInTaskbar; }
            set { window.ShowInTaskbar = value; }
        }
        public bool ShowActivated
        {
            get { return window.ShowActivated; }
            set { window.ShowActivated = value; }
        }
        public double Opacity
        {
            get { return window.Opacity; }
            set { window.Opacity = value; }
        }

        public Task ShowAndWaitUntilLoadedAsync()
        {
            if (window.IsLoaded)
            {
                window.Show();
                return Task.FromResult(true);
            }

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            RoutedEventHandler loaded = null;
            loaded = delegate {
                window.Loaded -= loaded;
                completion.TrySetResult(true);
            };
            window.Loaded += loaded;
            try { window.Show(); }
            catch (Exception exception)
            {
                window.Loaded -= loaded;
                completion.TrySetException(exception);
            }
            return completion.Task;
        }

        public void Hide()
        {
            window.Hide();
        }
    }

    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        Uri candidate;
        eventArgs.Cancel = !Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out candidate)
            || !NavigationPolicy.IsAllowedOrigin(allowedOrigin, candidate);
    }

    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        Uri candidate;
        if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out candidate)
            || !NavigationPolicy.CanOpenExternally(candidate))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(candidate.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.WriteDesktop(paths, "Unable to open external link: " + exception.Message);
        }
    }

    private void ShowWindow()
    {
        if (!IsVisible) Show();
    }

    private static Button CreateActionButton(string label, bool primary, Action action)
    {
        Button button = new Button
        {
            Content = label,
            MinWidth = 112,
            Height = 42,
            Padding = new Thickness(17, 0, 17, 0),
            Margin = new Thickness(0, 0, 12, 0),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            IsDefault = primary,
            Style = CreateButtonStyle(primary)
        };
        AutomationProperties.SetName(button, label);
        button.Click += delegate { action(); };
        return button;
    }

    private static Style CreateButtonStyle(bool primary)
    {
        SolidColorBrush baseBackground = primary
            ? AppAssets.AccentBrush
            : AppAssets.BackgroundBrush;
        SolidColorBrush hoverBackground = new SolidColorBrush(
            primary ? Color.FromRgb(0x5D, 0x78, 0xFF) : Color.FromRgb(0x16, 0x1D, 0x28));
        hoverBackground.Freeze();

        Style style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, AppAssets.ForegroundBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, baseBackground));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            primary ? AppAssets.AccentBrush : AppAssets.SubtleBorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.AppendChild(presenter);

        ControlTemplate template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBackground));
        template.Triggers.Add(hover);
        Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, AppAssets.ForegroundBrush));
        focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
