using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GoggleCast
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--self-test")
                {
                    string output = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test");
                    return SelfTests.Run(output).GetAwaiter().GetResult();
                }
                if (args.Length > 0 && args[0] == "--discover")
                {
                    var devices = DlnaDiscovery.DiscoverAsync(5000, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    string output = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discovery.txt");
                    using (var writer = new StreamWriter(output))
                    {
                        writer.WriteLine("Found: " + devices.Count);
                        foreach (var d in devices) writer.WriteLine(d.Name + " | " + d.Model + " | " + d.DescriptionUri);
                    }
                    return 0;
                }
                var app = new Application();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show(I18n.T("操作未完成：{0}", ErrorText.Translate(e.Exception)), "GoggleCast", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                var window = new MainWindow();
                if (args.Length > 0 && args[0] == "--ui-smoke")
                {
                    string output = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-smoke.png");
                    if (args.Length > 2) window.Width = int.Parse(args[2]);
                    if (args.Length > 3) window.Height = int.Parse(args[3]);
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = -20000;
                    window.Top = -20000;
                    window.ShowInTaskbar = false;
                    window.Loaded += async (s, e) =>
                    {
                        await Task.Delay(1600);
                        window.UpdateLayout();
                        var root = window.Content as FrameworkElement;
                        var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        image.Render(root);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                        using (var stream = File.Create(output)) encoder.Save(stream);
                        window.Close();
                    };
                }
                return app.Run(window);
            }
            catch (Exception ex)
            {
                string error = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.txt");
                try { File.WriteAllText(error, ex.ToString()); } catch { }
                if (args.Length == 0) MessageBox.Show(I18n.T("无法启动 GoggleCast。{0}\n\n详细记录：{1}", ErrorText.Translate(ex), error), "GoggleCast", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
