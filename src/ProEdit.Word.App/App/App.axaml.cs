using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.IO;
namespace ProEdit.Word.App;

public partial class App : Application
{
    public override void Initialize()
    {
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
        AvaloniaXamlLoader.Load(this);
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ProEdit.Word.Avalonia/"))
        {
            Source = new Uri("avares://ProEdit.Word.Avalonia/WordEditorResources.axaml")
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
                        || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".pdx", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".ps", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".eps", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".xps", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".oxps", StringComparison.OrdinalIgnoreCase))
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
