using Vibe.Office.Collaboration.UI;
using Vibe.Office.Collaboration.UI.ViewModels;
using Vibe.Office.Ribbon;

namespace Vibe.Word.Avalonia;

internal sealed class CollaborationRibbonExtension : IRibbonExtension
{
    public void Build(RibbonModelBuilder builder, RibbonExtensionContext context)
    {
        if (!context.TryGetService<ICollabUiService>(out var collabService))
        {
            return;
        }

        bool CanJoin() => collabService.ConnectionState is CollabConnectionState.Disconnected or CollabConnectionState.Error or CollabConnectionState.Offline;
        bool CanLeave() => collabService.ConnectionState is CollabConnectionState.Connected or CollabConnectionState.Connecting or CollabConnectionState.Reconnecting;

        var joinButton = new RibbonButton(
            "collab-join",
            "Join",
            new RibbonCommand(() => collabService.JoinAsync(), CanJoin),
            iconKey: "RibbonIcon.Link",
            size: RibbonControlSize.Medium,
            toolTipDescription: "Join a collaboration session.");

        var leaveButton = new RibbonButton(
            "collab-leave",
            "Leave",
            new RibbonCommand(() => collabService.LeaveAsync(), CanLeave),
            iconKey: "RibbonIcon.Reject",
            size: RibbonControlSize.Small,
            toolTipDescription: "Leave the collaboration session.");

        var shareButton = new RibbonButton(
            "collab-share",
            "Share",
            new RibbonCommand(() => collabService.ShareAsync()),
            iconKey: "RibbonIcon.Link",
            size: RibbonControlSize.Small,
            toolTipDescription: "Share a collaboration link.");

        var paneButton = new RibbonButton(
            "collab-pane",
            "People",
            new RibbonCommand(() =>
            {
                if (context.TryGetService<CollabShellViewModel>(out var viewModel))
                {
                    viewModel.IsPaneVisible = !viewModel.IsPaneVisible;
                }
            }),
            iconKey: "RibbonIcon.User",
            size: RibbonControlSize.Small,
            toolTipDescription: "Show or hide the collaboration pane.");

        var group = new RibbonGroup(
            "collaboration",
            "Collaboration",
            new IRibbonControl[] { joinButton, shareButton, leaveButton, paneButton },
            keyTip: "CO");

        builder.AddTab("collaboration", "Collaboration", keyTip: "Y")
            .AddGroup(group);
    }
}
