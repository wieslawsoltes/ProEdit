using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.IO;
namespace Vibe.Word.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? path = null;
            if (desktop.Args is { Length: > 0 })
            {
                var candidate = desktop.Args[0];
                if (File.Exists(candidate) && Path.GetExtension(candidate).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    path = candidate;
                }
            }

            desktop.MainWindow = new MainWindow(null, path);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
