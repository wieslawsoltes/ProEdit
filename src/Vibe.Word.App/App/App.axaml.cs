using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using System.IO;
namespace Vibe.Word.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Vibe.Word.Avalonia/"))
        {
            Source = new Uri("avares://Vibe.Word.Avalonia/WordEditorResources.axaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? path = null;
            if (desktop.Args is { Length: > 0 })
            {
                var candidate = desktop.Args[0];
                if (File.Exists(candidate))
                {
                    var extension = Path.GetExtension(candidate);
                    if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase))
                    {
                        path = candidate;
                    }
                }
            }

            desktop.MainWindow = new MainWindow(null, path);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
