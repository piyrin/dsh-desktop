using System;
using System.IO;
using System.Windows.Media;

internal static class AppAssetsTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("whale geometry comes from the first svg path", delegate {
            string directory = CreateAssetDirectory(
                "<svg xmlns=\"http://www.w3.org/2000/svg\">"
                + "<path d=\"M 0,0 L 10,0 10,20 0,20 Z\"/>"
                + "<path d=\"M 0,0 L 99,0 99,99 0,99 Z\"/>"
                + "</svg>");
            try
            {
                Geometry geometry = AppAssets.LoadWhaleGeometry(directory);

                AssertEx.Equal(10.0, geometry.Bounds.Width);
                AssertEx.Equal(20.0, geometry.Bounds.Height);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        runner.Add("svg without a path is rejected", delegate {
            string directory = CreateAssetDirectory(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"1\" cy=\"1\" r=\"1\"/></svg>");
            try
            {
                bool rejected = false;
                try
                {
                    AppAssets.LoadWhaleGeometry(directory);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                AssertEx.True(rejected);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
    }

    private static string CreateAssetDirectory(string svg)
    {
        string directory = Path.Combine(Path.GetTempPath(), "dsh-ui-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "favicon.svg"), svg);
        return directory;
    }
}
