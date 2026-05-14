using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ProEdit.Collaboration;
using ProEdit.Collaboration.UI;
using ProEdit.Collaboration.UI.ViewModels;
using ProEdit.Word.Avalonia;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ProEdit.Word.Avalonia.Headless.Tests.HeadlessTestAppBuilder))]

namespace ProEdit.Word.Avalonia.Headless.Tests;

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

public sealed class CollabUiHeadlessTests
{
    [AvaloniaFact]
    public async Task JoinAndLeaveUpdateStatusChip()
    {
        var identity = new DefaultCollabIdentityService();
        var state = new CollabUiState(identity)
        {
            TransportMode = CollabTransportMode.SharedFile,
            SharedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };

        var viewModel = new CollabSessionViewModel(state);
        var chip = new CollabStatusChip { DataContext = viewModel };
        var window = new Window { Content = chip };
        window.Show();

        await state.JoinAsync();

        var statusText = chip.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
        Assert.Equal("Connected", statusText);

        await state.LeaveAsync();

        statusText = chip.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
        Assert.Equal("Not Connected", statusText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task PresenceUpdatesParticipantsPane()
    {
        var identity = new DefaultCollabIdentityService();
        var state = new CollabUiState(identity);
        var shell = new CollabShellViewModel(state)
        {
            IsPaneVisible = true
        };

        var pane = new CollabPane { DataContext = shell };
        var window = new Window { Content = pane };
        window.Show();

        var remotePresence = new PresenceState(Guid.NewGuid(), "Remote", null, null, DateTimeOffset.UtcNow, "#33AAFF");
        state.UpdatePresence(remotePresence, TimeSpan.FromSeconds(5));

        await Dispatcher.UIThread.InvokeAsync(() => { });

        var listBox = pane.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault()
                      ?? pane.GetVisualDescendants().OfType<ListBox>().FirstOrDefault();
        Assert.NotNull(listBox);
        Assert.Equal(2, listBox!.ItemCount);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ConflictBannerShowsError()
    {
        var state = new CollabUiState();
        var conflict = new CollabConflictViewModel(state);
        var banner = new CollabBanner { DataContext = conflict };
        var window = new Window { Content = banner };
        window.Show();

        state.SetConnectionState(CollabConnectionState.Error, "Network failure");

        await Dispatcher.UIThread.InvokeAsync(() => { });

        var border = banner.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        Assert.NotNull(border);
        Assert.True(border!.IsVisible);

        var text = banner.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
        Assert.Equal("Network failure", text);

        window.Close();
    }
}
