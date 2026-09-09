using System.Windows;
using System.IO;
namespace Prism;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "prism-error.log"), args.Exception.ToString());
            MessageBox.Show("Prism couldn't complete this action. Your open document is still available.\n\n" + args.Exception.Message, "Prism", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
    }
}
