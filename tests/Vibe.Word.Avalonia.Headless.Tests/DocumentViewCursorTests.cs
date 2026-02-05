using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Vibe.Word.Avalonia;
using Xunit;

namespace Vibe.Word.Avalonia.Headless.Tests;

public sealed class DocumentViewCursorTests
{
    [AvaloniaFact]
    public async Task DocumentViewShowsIBeamCursorOverPage()
    {
        var view = new DocumentView();
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        window.Show();

        await Dispatcher.UIThread.InvokeAsync(() => { });

        var layout = view.Layout;
        Assert.NotEmpty(layout.Pages);

        var page = layout.Pages[0].Bounds;
        var docX = page.Left + page.Width * 0.5f;
        var docY = page.Top + page.Height * 0.5f;
        var offset = view.EffectiveScrollOffset;
        var viewPoint = new Point(docX * view.ZoomFactor - offset.X, docY * view.ZoomFactor - offset.Y);

        window.MouseMove(viewPoint);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.NotNull(view.Cursor);

        window.Close();
    }
}
