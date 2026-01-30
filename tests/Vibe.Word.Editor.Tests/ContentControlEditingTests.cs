using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Word.Editor;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public sealed class ContentControlEditingTests
{
    [Fact]
    public void CheckBoxContentControl_TogglesOnActivate()
    {
        var properties = new ContentControlProperties
        {
            Id = 1,
            Kind = ContentControlKind.Run,
            DataType = ContentControlDataType.CheckBox,
            IsChecked = false,
            ShowingPlaceholder = true
        };
        var session = CreateSessionWithContentControl(properties);
        Assert.True(session.TryGetContentControlAtCaret(out var hit));

        var updated = session.TryActivateContentControl(hit, ContentControlActivationSource.Keyboard, EditorModifiers.None, null);

        Assert.True(updated);
        Assert.True(properties.IsChecked);
        Assert.False(properties.ShowingPlaceholder ?? true);
    }

    [Fact]
    public void DropDownContentControl_CyclesSelection()
    {
        var properties = new ContentControlProperties
        {
            Id = 2,
            Kind = ContentControlKind.Run,
            DataType = ContentControlDataType.DropDownList,
            SelectedValue = "A"
        };
        properties.Items.Add(new ContentControlListItem { Value = "A" });
        properties.Items.Add(new ContentControlListItem { Value = "B" });

        var session = CreateSessionWithContentControl(properties);
        Assert.True(session.TryGetContentControlAtCaret(out var hit));

        var updated = session.TryActivateContentControl(hit, ContentControlActivationSource.Keyboard, EditorModifiers.None, null);

        Assert.True(updated);
        Assert.Equal("B", properties.SelectedValue);
    }

    [Fact]
    public void LockedContentControl_DoesNotActivate()
    {
        var properties = new ContentControlProperties
        {
            Id = 3,
            Kind = ContentControlKind.Run,
            DataType = ContentControlDataType.CheckBox,
            IsChecked = false,
            Lock = "contentLocked"
        };
        var session = CreateSessionWithContentControl(properties);
        Assert.True(session.TryGetContentControlAtCaret(out var hit));

        var updated = session.TryActivateContentControl(hit, ContentControlActivationSource.Keyboard, EditorModifiers.None, null);

        Assert.False(updated);
        Assert.False(properties.IsChecked);
    }

    private static EditorController CreateSessionWithContentControl(ContentControlProperties properties)
    {
        var document = new Document();
        var paragraph = document.GetParagraph(0);
        paragraph.Text = string.Empty;
        paragraph.Inlines.Clear();
        paragraph.Inlines.Add(new ContentControlStartInline(properties));
        paragraph.Inlines.Add(new RunInline("X"));
        paragraph.Inlines.Add(new ContentControlEndInline(properties.Id));

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        session.RefreshLayout();
        session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(0, 0)));
        return session;
    }
}
