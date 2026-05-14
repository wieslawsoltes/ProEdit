using System.Reactive.Linq;
using ProEdit.Collaboration.UI;
using ProEdit.Collaboration.UI.ViewModels;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class CollabViewModelTests
{
    [Fact]
    public void SessionViewModelReflectsStateChanges()
    {
        var state = new CollabUiState();
        var viewModel = new CollabSessionViewModel(state);

        state.SetConnectionState(CollabConnectionState.Connected);

        Assert.Equal(CollabConnectionState.Connected, viewModel.ConnectionState);
        Assert.Equal("Connected", viewModel.StatusText);
    }

    [Fact]
    public void ParticipantsViewModelRefreshesOnUpdate()
    {
        var state = new CollabUiState();
        var viewModel = new CollabParticipantsViewModel(state);

        state.UpdateParticipants(new[]
        {
            new CollabParticipant(Guid.NewGuid(), "User", "#2D7DF0", DateTimeOffset.UtcNow, false)
        });

        Assert.Equal(1, viewModel.Count);
    }

    [Fact]
    public void ShellViewModelTogglesPaneVisibility()
    {
        var state = new CollabUiState();
        var shell = new CollabShellViewModel(state);

        var initial = shell.IsPaneVisible;
        shell.TogglePaneCommand.Execute().Subscribe();

        Assert.NotEqual(initial, shell.IsPaneVisible);
    }
}
