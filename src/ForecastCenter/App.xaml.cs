using Microsoft.UI.Xaml;

namespace ForecastCenter;

public partial class App : Application
{
    private MainWindow? _window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                var folder = AppIdentity.DataRoot;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "startup-error.txt"), $"{DateTimeOffset.Now:O}\n{args.Message}\n{args.Exception}");
            }
            catch { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppIdentity.ApplyProcessIdentity();
        _window = new MainWindow();
        _window.Activate();
    }
}
