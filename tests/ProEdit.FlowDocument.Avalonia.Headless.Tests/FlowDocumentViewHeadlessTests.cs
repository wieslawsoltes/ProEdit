using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ProEdit.FlowDocument;
using ProEdit.FlowDocument.Avalonia;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ProEdit.FlowDocument.Avalonia.Headless.Tests.HeadlessTestAppBuilder))]

namespace ProEdit.FlowDocument.Avalonia.Headless.Tests;

public sealed class HeadlessTestApp : Application
{
}

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

public sealed class FlowDocumentViewHeadlessTests
{
    [AvaloniaFact]
    public async Task FlowDocumentViewRendersDocument()
    {
        var document = new FlowDocument();
        document.Blocks.Add(new Paragraph("Hello"));

        var view = new FlowDocumentView { FlowDocument = document, Width = 400, Height = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };
        window.Show();

        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.NotNull(view.RenderedDocument);
        Assert.NotNull(view.Layout);

        document.Blocks.Add(new Paragraph("Updated"));
        window.InvalidateVisual();

        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.True(view.RenderedDocument?.Blocks.Count >= 2);

        window.Close();
    }

    [AvaloniaFact]
    public async Task FlowDocumentViewHostsBlockUiContainerControl()
    {
        var document = new FlowDocument();
        var button = new Button
        {
            Content = "Hosted UI",
            Width = 180,
            Height = 40
        };
        document.Blocks.Add(new BlockUIContainer { Child = button });

        var view = new FlowDocumentView { FlowDocument = document, Width = 600, Height = 400 };
        var window = new Window { Content = view, Width = 640, Height = 480 };
        window.Show();

        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Contains(button, view.Children);
        Assert.True(button.Bounds.Width > 0d);
        Assert.True(button.Bounds.Height > 0d);

        window.Close();
    }
}
