using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using WpfPath = System.Windows.Shapes.Path;

internal static class AppAssets
{
    internal const string ApplicationTitle = "DeepSeek Harness";
    internal const string BackgroundHex = "#0B0F14";
    internal const string AccentHex = "#4D6BFE";
    internal const string ForegroundHex = "#E8EEF9";

    internal static readonly SolidColorBrush BackgroundBrush = CreateFrozenBrush(0x0B, 0x0F, 0x14);
    internal static readonly SolidColorBrush AccentBrush = CreateFrozenBrush(0x4D, 0x6B, 0xFE);
    internal static readonly SolidColorBrush ForegroundBrush = CreateFrozenBrush(0xE8, 0xEE, 0xF9);
    internal static readonly SolidColorBrush MutedForegroundBrush = CreateFrozenBrush(0x9A, 0xA8, 0xBE);
    internal static readonly SolidColorBrush SubtleBorderBrush = CreateFrozenBrush(0x2A, 0x34, 0x43);

    internal static Geometry LoadWhaleGeometry(string applicationDirectory)
    {
        if (String.IsNullOrEmpty(applicationDirectory))
            throw new ArgumentException("Application directory is required.", "applicationDirectory");

        string assetPath = Path.Combine(applicationDirectory, "favicon.svg");
        XDocument document = XDocument.Load(assetPath, LoadOptions.None);
        XElement pathElement = document.Descendants()
            .FirstOrDefault(delegate(XElement element) { return element.Name.LocalName == "path"; });
        string pathData = pathElement == null ? null : (string)pathElement.Attribute("d");
        if (String.IsNullOrWhiteSpace(pathData))
            throw new InvalidDataException("favicon.svg does not contain an SVG path with geometry data.");

        Geometry geometry = Geometry.Parse(pathData);
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    internal static WpfPath CreateWhalePath(Geometry geometry, double size)
    {
        if (geometry == null) throw new ArgumentNullException("geometry");
        return new WpfPath
        {
            Data = geometry,
            Fill = AccentBrush,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            SnapsToDevicePixels = true
        };
    }

    internal static ImageSource LoadWindowIcon(string applicationDirectory)
    {
        string iconPath = Path.Combine(applicationDirectory, "dsh-whale.ico");
        BitmapFrame frame = BitmapFrame.Create(
            new Uri(iconPath, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (frame.CanFreeze) frame.Freeze();
        return frame;
    }

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
