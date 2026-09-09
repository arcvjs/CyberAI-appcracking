using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Prism.Controls;

internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        // Render the application's own visual tree without showing a window,
        // invoking input automation, or activating/renewing a license.
        var application = new Prism.App(); application.InitializeComponent();
        var window = new Prism.MainWindow(); window.ApplyTemplate();
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        const int width = 1500, height = 960;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        foreach (var method in new[] { "UpdateAll", "FitCanvas", "UpdateLicenseBadge" })
            typeof(Prism.MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        Save(bitmap, args[0]);
        var surface = (DocumentSurface)window.FindName("Surface");
        Save(surface.RenderBitmap(), args[1]);
        window.Close();
    }
    private static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file); Console.WriteLine(path);
    }
}
