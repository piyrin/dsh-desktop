using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

internal sealed class SplashOverlay : Grid
{
    private static readonly Duration RevealDuration = new Duration(TimeSpan.FromSeconds(1.2));
    private static readonly Duration ShimmerDuration = new Duration(TimeSpan.FromSeconds(1.8));

    private readonly Storyboard revealStoryboard;
    private readonly Storyboard shimmerStoryboard;
    private readonly Grid revealViewport;
    private readonly Brush revealMask;
    private bool isAnimationRunning;

    internal SplashOverlay(Geometry whaleGeometry)
    {
        if (whaleGeometry == null) throw new ArgumentNullException("whaleGeometry");

        Background = AppAssets.BackgroundBrush;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = true;
        AutomationProperties.SetName(this, "DeepSeek Harness loading");
        NameScope.SetNameScope(this, new NameScope());

        Grid identity = CreateIdentity(whaleGeometry);
        LinearGradientBrush softRevealMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        softRevealMask.GradientStops.Add(new GradientStop(Colors.White, 0.0));
        softRevealMask.GradientStops.Add(new GradientStop(Colors.White, 0.82));
        softRevealMask.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        revealMask = softRevealMask;

        Grid revealHost = new Grid
        {
            Width = 420,
            Height = 210,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        revealViewport = new Grid
        {
            Width = 0,
            Height = 210,
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = true,
            OpacityMask = revealMask
        };
        identity.HorizontalAlignment = HorizontalAlignment.Left;
        revealViewport.Children.Add(identity);
        revealHost.Children.Add(revealViewport);
        Children.Add(revealHost);

        Rectangle shimmer;
        Grid shimmerViewport = CreateShimmer(out shimmer);
        Children.Add(shimmerViewport);

        RegisterName("SplashRevealViewport", revealViewport);
        RegisterName("SplashShimmer", shimmer);
        revealStoryboard = CreateRevealStoryboard();
        shimmerStoryboard = CreateShimmerStoryboard();
        revealStoryboard.Completed += delegate {
            if (isAnimationRunning) revealViewport.OpacityMask = null;
        };
    }

    internal bool IsAnimationRunning
    {
        get { return isAnimationRunning; }
    }

    internal void StartAnimated()
    {
        if (isAnimationRunning)
        {
            revealStoryboard.Remove(this);
            shimmerStoryboard.Remove(this);
        }
        revealViewport.Width = 0;
        revealViewport.OpacityMask = revealMask;
        Visibility = Visibility.Visible;
        revealStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        shimmerStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        isAnimationRunning = true;
    }

    internal void StopAndHide()
    {
        if (isAnimationRunning)
        {
            revealStoryboard.Remove(this);
            shimmerStoryboard.Remove(this);
        }
        isAnimationRunning = false;
        Visibility = Visibility.Collapsed;
    }

    private static Grid CreateIdentity(Geometry whaleGeometry)
    {
        Grid identity = new Grid
        {
            Width = 420,
            Height = 210,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid mark = new Grid
        {
            Width = 112,
            Height = 112,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RadialGradientBrush bloomBrush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        bloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb(42, 77, 107, 254), 0.0));
        bloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 77, 107, 254), 1.0));
        mark.Children.Add(new Ellipse
        {
            Width = 112,
            Height = 82,
            Fill = bloomBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        mark.Children.Add(AppAssets.CreateWhalePath(whaleGeometry, 82));
        stack.Children.Add(mark);

        stack.Children.Add(new TextBlock
        {
            Text = AppAssets.ApplicationTitle,
            Foreground = AppAssets.ForegroundBrush,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 17, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        identity.Children.Add(stack);
        return identity;
    }

    private static Grid CreateShimmer(out Rectangle shimmer)
    {
        LinearGradientBrush brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 77, 107, 254), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(28, 124, 148, 255), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 77, 107, 254), 1.0));

        shimmer = new Rectangle
        {
            Width = 120,
            Height = 190,
            Fill = brush,
            Opacity = 0.18,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        Canvas.SetLeft(shimmer, -120);
        Canvas canvas = new Canvas();
        canvas.Children.Add(shimmer);
        return new Grid
        {
            Width = 420,
            Height = 210,
            ClipToBounds = true,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { canvas }
        };
    }

    private static Storyboard CreateRevealStoryboard()
    {
        Storyboard result = new Storyboard();
        DoubleAnimation reveal = new DoubleAnimation(0, 420, RevealDuration)
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTargetName(reveal, "SplashRevealViewport");
        Storyboard.SetTargetProperty(reveal, new PropertyPath(FrameworkElement.WidthProperty));
        result.Children.Add(reveal);
        return result;
    }

    private static Storyboard CreateShimmerStoryboard()
    {
        Storyboard result = new Storyboard();
        DoubleAnimation shimmer = new DoubleAnimation(-120, 420, ShimmerDuration)
        {
            BeginTime = TimeSpan.FromSeconds(1.2),
            RepeatBehavior = RepeatBehavior.Forever,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTargetName(shimmer, "SplashShimmer");
        Storyboard.SetTargetProperty(shimmer, new PropertyPath(Canvas.LeftProperty));
        result.Children.Add(shimmer);

        return result;
    }
}
