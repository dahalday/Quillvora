using System.Windows;
namespace Transcriber;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Quillvora", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
    }
}
