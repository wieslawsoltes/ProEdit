using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.IO;
using Vibe.Office.Documents;
using Vibe.Office.OpenXml;

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
            Document? document = null;
            string? path = null;
            if (desktop.Args is { Length: > 0 })
            {
                var candidate = desktop.Args[0];
                if (File.Exists(candidate) && Path.GetExtension(candidate).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        document = new DocxImporter().Load(candidate);
                        path = candidate;
                    }
                    catch
                    {
                        document = null;
                    }
                }
            }

            desktop.MainWindow = new MainWindow(document, path);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
