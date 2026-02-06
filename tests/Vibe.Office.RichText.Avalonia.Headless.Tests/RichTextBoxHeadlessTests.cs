using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.OpenXml;
using Vibe.Office.RichText.Avalonia;
using Xunit;
using DocumentModel = Vibe.Office.Documents.Document;
using DocumentParagraphBlock = Vibe.Office.Documents.ParagraphBlock;
using DocumentRunInline = Vibe.Office.Documents.RunInline;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;
using FlowList = Vibe.Office.FlowDocument.List;
using FlowListItem = Vibe.Office.FlowDocument.ListItem;
using FlowParagraph = Vibe.Office.FlowDocument.Paragraph;
using FlowRun = Vibe.Office.FlowDocument.Run;

[assembly: AvaloniaTestApplication(typeof(Vibe.Office.RichText.Avalonia.Headless.Tests.HeadlessTestAppBuilder))]

namespace Vibe.Office.RichText.Avalonia.Headless.Tests;

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

public sealed class RichTextBoxHeadlessTests
{
    [AvaloniaFact]
    public void DefaultDocument_IsCreated()
    {
        var box = new RichTextBox();

        Assert.NotNull(box.Document);
    }

    [Fact]
    public void ApiSurface_IncludesCoreWpfParityMembers()
    {
        var type = typeof(RichTextBox);

        Assert.NotNull(type.GetProperty(nameof(RichTextBox.Document)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBox.Selection)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBox.CaretPosition)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBox.AcceptsReturn)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBox.AcceptsTab)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBox.IsReadOnly)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBox.IsReadOnlyCaretVisible)));

        Assert.NotNull(type.GetMethod(nameof(RichTextBox.Copy)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.Cut)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.Paste)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.Undo)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.Redo)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.SelectAll)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.BeginChange)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.EndChange)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.GetPositionFromPoint)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.GetSpellingError)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.GetSpellingErrorRange)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.GetNextSpellingErrorPosition)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBox.ShouldSerializeDocument)));
    }

    [AvaloniaFact]
    public void SettingDocumentToNull_Throws()
    {
        var box = new RichTextBox();

        Assert.Throws<ArgumentNullException>(() => box.Document = null!);
    }

    [AvaloniaFact]
    public void FlowDocument_CannotBeAttachedToTwoOwners()
    {
        var shared = new FlowDocumentModel();

        var ownerA = new RichTextBox { Document = shared };
        var ownerB = new RichTextBox();

        Assert.Throws<InvalidOperationException>(() =>
        {
            ownerB.Document = shared;
        });
        Assert.Same(shared, ownerA.Document);
    }

    [AvaloniaFact]
    public async Task NonInitialDocumentAssignment_RaisesTextChanged()
    {
        var box = new RichTextBox();
        var eventCount = 0;
        box.TextChanged += (_, _) => eventCount++;

        var document = new FlowDocumentModel();
        document.Blocks.Add(new FlowParagraph("Assigned"));

        box.Document = document;

        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.True(eventCount >= 1);
    }

    [AvaloniaFact]
    public async Task TemplateHookup_AttachesDocumentPresenter()
    {
        var style = new StyleInclude(new Uri("avares://Vibe.Office.RichText.Avalonia/"))
        {
            Source = new Uri("avares://Vibe.Office.RichText.Avalonia/Themes/Generic.axaml")
        };

        Application.Current!.Styles.Add(style);

        var box = new RichTextBox();
        var window = new Window { Content = box, Width = 800, Height = 600 };
        window.Show();

        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.True(box.HasDocumentPresenterForTests);

        window.Close();
        Application.Current.Styles.Remove(style);
    }

    [AvaloniaFact]
    public void EditorMutation_SyncsBackToFlowDocumentTree()
    {
        var flowDocument = new FlowDocumentModel();
        flowDocument.Blocks.Add(new FlowParagraph("Original"));

        var box = new RichTextBox
        {
            Document = flowDocument
        };

        var editorDocument = new DocumentModel();
        editorDocument.Blocks.Clear();
        var editorParagraph = new DocumentParagraphBlock();
        editorParagraph.Inlines.Add(new DocumentRunInline("Edited from editor"));
        editorDocument.Blocks.Add(editorParagraph);

        var changedEvents = 0;
        box.TextChanged += (_, _) => changedEvents++;

        box.ReplaceEditorDocumentForTests(editorDocument);

        Assert.Same(flowDocument, box.Document);
        var roundtripParagraph = Assert.IsType<FlowParagraph>(box.Document.Blocks[0]);
        var run = Assert.IsType<FlowRun>(roundtripParagraph.Inlines[0]);
        Assert.Equal("Edited from editor", run.Text);
        Assert.True(changedEvents >= 1);
    }

    [AvaloniaFact]
    public async Task Keyboard_EndAndCtrlEnd_MoveCaretToLineAndDocumentEnd()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha", "Beta")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            PressKey(window, Key.End);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var lineEnd = box.SelectionForTests.Normalize().End;
            Assert.Equal(0, lineEnd.ParagraphIndex);
            Assert.Equal(5, lineEnd.Offset);

            PressKey(window, Key.End, RawInputModifiers.Control);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var documentEnd = box.SelectionForTests.Normalize().End;
            Assert.Equal(1, documentEnd.ParagraphIndex);
            Assert.Equal(4, documentEnd.Offset);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_HomeAndCtrlHome_MoveCaretToLineAndDocumentStart()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha", "Beta")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            PressKey(window, Key.End, RawInputModifiers.Control);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            PressKey(window, Key.Home);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var lineStart = box.SelectionForTests.Normalize().End;
            Assert.Equal(1, lineStart.ParagraphIndex);
            Assert.Equal(0, lineStart.Offset);

            PressKey(window, Key.Home, RawInputModifiers.Control);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var documentStart = box.SelectionForTests.Normalize().End;
            Assert.Equal(0, documentStart.ParagraphIndex);
            Assert.Equal(0, documentStart.Offset);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_PageDownAndPageUp_MoveAcrossVisibleContent()
    {
        var box = new RichTextBox
        {
            Document = BuildLongDocument(120)
        };

        var (window, style) = await ShowInWindowAsync(box, width: 700, height: 420);
        try
        {
            var before = box.SelectionForTests.Normalize().End;
            PressKey(window, Key.PageDown);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var afterPageDown = box.SelectionForTests.Normalize().End;

            Assert.True(afterPageDown.ParagraphIndex > before.ParagraphIndex);

            PressKey(window, Key.PageUp);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var afterPageUp = box.SelectionForTests.Normalize().End;

            Assert.True(afterPageUp.ParagraphIndex < afterPageDown.ParagraphIndex);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_CtrlA_SelectsWholeDocument()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha", "Beta")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            PressKey(window, Key.A, RawInputModifiers.Control);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var selection = box.SelectionForTests.Normalize();
            Assert.Equal(0, selection.Start.ParagraphIndex);
            Assert.Equal(0, selection.Start.Offset);
            Assert.Equal(1, selection.End.ParagraphIndex);
            Assert.Equal(4, selection.End.Offset);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_Tab_InsertsTabWhenAcceptsTabEnabled()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha"),
            AcceptsTab = true
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            PressKey(window, Key.End);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            PressKey(window, Key.Tab);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var text = GetEditorParagraphText(box, 0);
            Assert.Equal("Alpha\t", text);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_Tab_DoesNotInsertWhenAcceptsTabDisabled()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha"),
            AcceptsTab = false
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            PressKey(window, Key.End);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            PressKey(window, Key.Tab);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var text = GetEditorParagraphText(box, 0);
            Assert.Equal("Alpha", text);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_Enter_DoesNotInsertWhenAcceptsReturnDisabled()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha"),
            AcceptsReturn = false
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            PressKey(window, Key.End);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            PressKey(window, Key.Enter);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Equal(1, box.EditorDocumentForTests.ParagraphCount);
            Assert.Equal("Alpha", GetEditorParagraphText(box, 0));
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public void SelectionProperty_IsStable()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        var first = box.Selection;
        var second = box.Selection;
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public void ShouldSerializeDocument_TracksImplicitVsExplicitOwnership()
    {
        var box = new RichTextBox();
        Assert.False(box.ShouldSerializeDocument());

        box.Document = BuildFlowDocument("Alpha");
        Assert.True(box.ShouldSerializeDocument());
    }

    [AvaloniaFact]
    public void CaretPosition_SetterUpdatesCollapsedSelection()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        box.CaretPosition = new FlowTextPointer(box.Document, 0, 3);

        var selection = box.SelectionForTests.Normalize();
        Assert.True(selection.IsEmpty);
        Assert.Equal(0, selection.Start.ParagraphIndex);
        Assert.Equal(3, selection.Start.Offset);
        Assert.Equal(3, box.CaretPosition.Offset);
    }

    [AvaloniaFact]
    public void CaretPosition_FromDifferentDocument_Throws()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        var otherDocument = BuildFlowDocument("Beta");
        var pointer = new FlowTextPointer(otherDocument, 0, 0);

        Assert.Throws<InvalidOperationException>(() => box.CaretPosition = pointer);
    }

    [AvaloniaFact]
    public void FlowTextRange_ApplyPropertyValue_PreservesCurrentSelection()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha Beta")
        };

        box.CaretPosition = new FlowTextPointer(box.Document, 0, 0);
        var range = new FlowTextRange(
            new FlowTextPointer(box.Document, 0, 0),
            new FlowTextPointer(box.Document, 0, 5));

        range.ApplyPropertyValue(FlowTextRangeProperty.FontWeight, DocFontWeight.Bold);

        var selection = box.SelectionForTests.Normalize();
        Assert.True(selection.IsEmpty);
        Assert.Equal(0, selection.Start.Offset);

        var applied = range.GetPropertyValue(FlowTextRangeProperty.FontWeight);
        Assert.Equal(DocFontWeight.Bold, applied);
    }

    [AvaloniaFact]
    public void FlowTextRange_GetPropertyValue_ReturnsMixedValueForMixedFormatting()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("AB")
        };

        var firstCharacter = new FlowTextRange(
            new FlowTextPointer(box.Document, 0, 0),
            new FlowTextPointer(box.Document, 0, 1));
        firstCharacter.ApplyPropertyValue(FlowTextRangeProperty.FontWeight, DocFontWeight.Bold);

        var range = new FlowTextRange(
            new FlowTextPointer(box.Document, 0, 0),
            new FlowTextPointer(box.Document, 0, 2));

        var value = range.GetPropertyValue(FlowTextRangeProperty.FontWeight);
        Assert.Same(FlowTextRange.MixedValue, value);
    }

    [AvaloniaFact]
    public void BeginChange_EndChange_CoalescesTextChangedEvents()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        box.Selection.Select(
            new FlowTextPointer(box.Document, 0, 0),
            new FlowTextPointer(box.Document, 0, 5));

        var changedEvents = 0;
        box.TextChanged += (_, _) => changedEvents++;

        box.BeginChange();
        try
        {
            box.Selection.ApplyPropertyValue(FlowTextRangeProperty.FontWeight, DocFontWeight.Bold);
            box.Selection.ApplyPropertyValue(FlowTextRangeProperty.FontStyle, DocFontStyle.Italic);
            Assert.Equal(0, changedEvents);
        }
        finally
        {
            box.EndChange();
        }

        Assert.Equal(1, changedEvents);
        Assert.Equal(DocFontWeight.Bold, box.Selection.GetPropertyValue(FlowTextRangeProperty.FontWeight));
        Assert.Equal(DocFontStyle.Italic, box.Selection.GetPropertyValue(FlowTextRangeProperty.FontStyle));
    }

    [AvaloniaFact]
    public async Task ReadOnly_BlocksTypingAndMutatingMethods()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha"),
            IsReadOnly = true
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            box.CaretPosition = new FlowTextPointer(box.Document, 0, 5);
            var textInput = new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = box,
                Text = "!"
            };

            box.RaiseEvent(textInput);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Equal("Alpha", GetEditorParagraphText(box, 0));
            Assert.False(box.Cut());
            Assert.False(box.Paste());
            Assert.False(box.Undo());
            Assert.False(box.Redo());
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task CopyCutPaste_Methods_UseEditorClipboardPipeline()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            box.Selection.Select(
                new FlowTextPointer(box.Document, 0, 0),
                new FlowTextPointer(box.Document, 0, 5));
            Assert.True(box.Copy());
            await Dispatcher.UIThread.InvokeAsync(() => { });

            box.CaretPosition = new FlowTextPointer(box.Document, 0, 5);
            Assert.True(box.Paste());
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var afterPasteText = GetEditorDocumentText(box);
            Assert.True(box.EditorDocumentForTests.ParagraphCount > 1);
            Assert.True(CountOccurrences(afterPasteText, "Alpha") >= 2);

            box.Selection.Select(
                new FlowTextPointer(box.Document, 0, 0),
                new FlowTextPointer(box.Document, 0, 5));
            Assert.True(box.Cut());
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var afterCutText = GetEditorDocumentText(box);
            Assert.NotEqual(afterPasteText, afterCutText);
            Assert.Contains("Alpha", afterCutText, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task SelectAll_Method_SelectsWholeDocument()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha", "Beta")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            Assert.True(box.SelectAll());
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var selection = box.SelectionForTests.Normalize();
            Assert.Equal(0, selection.Start.ParagraphIndex);
            Assert.Equal(0, selection.Start.Offset);
            Assert.Equal(1, selection.End.ParagraphIndex);
            Assert.Equal(4, selection.End.Offset);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task UndoRedo_Methods_RestoreTypedContent()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            box.CaretPosition = new FlowTextPointer(box.Document, 0, 5);
            var textInput = new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = box,
                Text = "!"
            };

            box.RaiseEvent(textInput);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Equal("Alpha!", GetEditorParagraphText(box, 0));

            Assert.True(box.Undo());
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Equal("Alpha", GetEditorParagraphText(box, 0));

            Assert.True(box.Redo());
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Equal("Alpha!", GetEditorParagraphText(box, 0));
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task ProofingProperties_SyncWithProofingToggleService()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("teh")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            Assert.True(box.DocumentViewForTests.TryGetService<IProofingToggleService>(out var toggle));

            box.IsSpellingEnabled = true;
            box.IsGrammarEnabled = true;
            box.IsStyleEnabled = true;
            box.IsProofingEnabled = true;
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.True(toggle.IsEnabled);
            Assert.True(toggle.IsSpellingEnabled);
            Assert.True(toggle.IsGrammarEnabled);
            Assert.True(toggle.IsStyleEnabled);

            box.IsProofingEnabled = false;
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.False(toggle.IsEnabled);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task HostTextInput_InsertsTextThroughHostedCompositionPath()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            box.CaretPosition = new FlowTextPointer(box.Document, 0, 5);
            var textInput = new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = box,
                Text = "IME"
            };

            box.RaiseEvent(textInput);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Contains("IME", GetEditorParagraphText(box, 0), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task SimpleParagraphEdit_UsesIncrementalFlowMirrorSync()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha", "Beta")
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            var firstBefore = Assert.IsType<FlowParagraph>(box.Document.Blocks[0]);
            var secondBefore = Assert.IsType<FlowParagraph>(box.Document.Blocks[1]);

            box.CaretPosition = new FlowTextPointer(box.Document, 0, 5);
            var textInput = new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = box,
                Text = "!"
            };

            box.RaiseEvent(textInput);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var firstAfter = Assert.IsType<FlowParagraph>(box.Document.Blocks[0]);
            var secondAfter = Assert.IsType<FlowParagraph>(box.Document.Blocks[1]);
            Assert.NotSame(firstBefore, firstAfter);
            Assert.Same(secondBefore, secondAfter);
            Assert.Equal("Beta", Assert.IsType<FlowRun>(secondAfter.Inlines[0]).Text);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task ListEdit_FallsBackToFullFlowMirrorSync()
    {
        var list = new FlowList();
        var firstItem = new FlowListItem();
        firstItem.Blocks.Add(new FlowParagraph("Item A"));
        list.ListItems.Add(firstItem);
        var secondItem = new FlowListItem();
        secondItem.Blocks.Add(new FlowParagraph("Item B"));
        list.ListItems.Add(secondItem);

        var document = new FlowDocumentModel();
        document.Blocks.Clear();
        document.Blocks.Add(list);
        document.Blocks.Add(new FlowParagraph("Tail"));

        var box = new RichTextBox
        {
            Document = document
        };

        var (window, style) = await ShowInWindowAsync(box);
        try
        {
            var tailBefore = Assert.IsType<FlowParagraph>(box.Document.Blocks[1]);

            box.CaretPosition = new FlowTextPointer(box.Document, 0, 6);
            var textInput = new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = box,
                Text = "!"
            };

            box.RaiseEvent(textInput);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var tailAfter = Assert.IsType<FlowParagraph>(box.Document.Blocks[1]);
            Assert.NotSame(tailBefore, tailAfter);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task AutomationPeer_ExposesValueAndScrollProviders()
    {
        var box = new RichTextBox
        {
            Document = BuildLongDocument(120)
        };

        var (window, style) = await ShowInWindowAsync(box, width: 680, height: 420);
        try
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(box);
            Assert.NotNull(peer);
            Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());

            var valueProvider = peer.GetProvider<IValueProvider>();
            Assert.NotNull(valueProvider);
            Assert.True(valueProvider!.IsReadOnly);
            Assert.Contains("Paragraph 000", valueProvider.Value, StringComparison.Ordinal);

            var scrollProvider = peer.GetProvider<IScrollProvider>();
            Assert.NotNull(scrollProvider);
            Assert.True(scrollProvider!.VerticallyScrollable);
            Assert.NotEqual(ScrollPatternIdentifiers.NoScroll, scrollProvider.VerticalScrollPercent);

            var beforePercent = scrollProvider.VerticalScrollPercent;
            scrollProvider.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var afterPercent = scrollProvider.VerticalScrollPercent;
            Assert.True(afterPercent > beforePercent);

            scrollProvider.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 0d);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.True(scrollProvider.VerticalScrollPercent <= afterPercent);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [AvaloniaFact]
    public async Task EmbeddedUiContainers_Mount_AndRespectDocumentEnabled_Gating()
    {
        var inlineButton = new Button
        {
            Content = "Inline",
            Width = 96,
            Height = 28
        };
        var blockButton = new Button
        {
            Content = "Block",
            Width = 112,
            Height = 32
        };

        var document = new FlowDocumentModel();
        document.Blocks.Clear();
        var first = new FlowParagraph();
        first.Inlines.Add(new Vibe.Office.FlowDocument.InlineUIContainer
        {
            Child = inlineButton
        });
        document.Blocks.Add(first);
        document.Blocks.Add(new Vibe.Office.FlowDocument.BlockUIContainer
        {
            Child = blockButton
        });
        for (var i = 0; i < 48; i++)
        {
            document.Blocks.Add(new FlowParagraph($"Paragraph {i:D2}"));
        }

        var box = new RichTextBox
        {
            Document = document,
            IsDocumentEnabled = false
        };

        var (window, style) = await ShowInWindowAsync(box, width: 900, height: 600);
        try
        {
            Assert.Equal(2, box.EmbeddedControlCountForTests);
            Assert.True(box.TryGetFirstEmbeddedControlForTests(out var hostedControl));
            Assert.False(hostedControl.IsEnabled);
            Assert.False(hostedControl.IsHitTestVisible);
            Assert.True(box.TryGetFirstEmbeddedControlBoundsForTests(out var beforeBounds));

            box.IsDocumentEnabled = true;
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.True(hostedControl.IsEnabled);
            Assert.True(hostedControl.IsHitTestVisible);

            box.DocumentViewForTests.ZoomIn();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.True(box.TryGetFirstEmbeddedControlBoundsForTests(out var zoomedBounds));
            Assert.True(zoomedBounds.Width > beforeBounds.Width);

            var previousTop = zoomedBounds.Top;
            var currentOffset = box.DocumentViewForTests.Offset;
            box.DocumentViewForTests.Offset = new Vector(currentOffset.X, currentOffset.Y + 80);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.True(box.TryGetFirstEmbeddedControlBoundsForTests(out var scrolledBounds));
            Assert.True(scrolledBounds.Top < previousTop);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(style);
        }
    }

    [Fact]
    public void ClipboardParser_PrefersOpenXml_ThenRtf_ThenHtml_ThenText()
    {
        var docxDocument = BuildClipboardDocument("Docx wins");
        var rtfDocument = BuildClipboardDocument("Rtf fallback");
        var htmlDocument = BuildClipboardDocument("Html fallback");
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateBytesPlatformFormat("application/vnd.openxmlformats-officedocument.wordprocessingml.document"), BuildDocx(docxDocument));
        item.Set(DataFormat.CreateStringPlatformFormat("text/rtf"), ClipboardRtfSerializer.ToRtf(rtfDocument));
        item.Set(DataFormat.CreateStringPlatformFormat("text/html"), ClipboardHtmlSerializer.ToHtml(htmlDocument));
        item.Set(DataFormat.Text, "Plain fallback");
        var transfer = new DataTransfer();
        transfer.Add(item);

        Assert.True(RichTextBox.TryBuildClipboardContentFromDataTransferForTests(transfer, out var content));
        Assert.Equal("Docx wins", ClipboardPlainTextSerializer.ToPlainText(content).Trim());
    }

    [Fact]
    public void ClipboardParser_FallsBackToRtf_WhenOpenXmlMissing()
    {
        var rtfDocument = BuildClipboardDocument("Rtf wins");
        var htmlDocument = BuildClipboardDocument("Html fallback");
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateStringPlatformFormat("text/rtf"), ClipboardRtfSerializer.ToRtf(rtfDocument));
        item.Set(DataFormat.CreateStringPlatformFormat("text/html"), ClipboardHtmlSerializer.ToHtml(htmlDocument));
        item.Set(DataFormat.Text, "Plain fallback");
        var transfer = new DataTransfer();
        transfer.Add(item);

        Assert.True(RichTextBox.TryBuildClipboardContentFromDataTransferForTests(transfer, out var content));
        Assert.Equal("Rtf wins", ClipboardPlainTextSerializer.ToPlainText(content).Trim());
    }

    [Fact]
    public void ClipboardParser_FallsBackToHtml_ThenText()
    {
        var htmlDocument = BuildClipboardDocument("Html wins");
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateStringPlatformFormat("text/html"), ClipboardHtmlSerializer.ToHtml(htmlDocument));
        item.Set(DataFormat.Text, "Plain fallback");
        var transfer = new DataTransfer();
        transfer.Add(item);

        Assert.True(RichTextBox.TryBuildClipboardContentFromDataTransferForTests(transfer, out var content));
        var parsedHtmlText = ClipboardPlainTextSerializer.ToPlainText(content);
        Assert.Contains("Html", parsedHtmlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wins", parsedHtmlText, StringComparison.OrdinalIgnoreCase);

        var textOnlyTransfer = new DataTransfer();
        textOnlyTransfer.Add(DataTransferItem.CreateText("Plain wins"));
        Assert.True(RichTextBox.TryBuildClipboardContentFromDataTransferForTests(textOnlyTransfer, out var plainContent));
        Assert.Equal("Plain wins", ClipboardPlainTextSerializer.ToPlainText(plainContent).Trim());
    }

    private static FlowDocumentModel BuildFlowDocument(params string[] paragraphs)
    {
        var document = new FlowDocumentModel();
        document.Blocks.Clear();
        for (var i = 0; i < paragraphs.Length; i++)
        {
            document.Blocks.Add(new FlowParagraph(paragraphs[i]));
        }

        return document;
    }

    private static FlowDocumentModel BuildLongDocument(int paragraphCount)
    {
        var document = new FlowDocumentModel();
        document.Blocks.Clear();
        for (var i = 0; i < paragraphCount; i++)
        {
            document.Blocks.Add(new FlowParagraph($"Paragraph {i:D3} " + new string('x', 48)));
        }

        return document;
    }

    private static string GetEditorParagraphText(RichTextBox box, int paragraphIndex)
    {
        var paragraph = box.EditorDocumentForTests.GetParagraph(paragraphIndex);
        return DocumentEditHelpers.GetParagraphText(paragraph);
    }

    private static string GetEditorDocumentText(RichTextBox box)
    {
        var document = box.EditorDocumentForTests;
        var lines = new List<string>(document.ParagraphCount);
        for (var i = 0; i < document.ParagraphCount; i++)
        {
            lines.Add(DocumentEditHelpers.GetParagraphText(document.GetParagraph(i)));
        }

        return string.Join('\n', lines);
    }

    private static int CountOccurrences(string value, string token)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static DocumentModel BuildClipboardDocument(string text)
    {
        var document = new DocumentModel();
        document.Blocks.Clear();
        var paragraph = new DocumentParagraphBlock();
        paragraph.Inlines.Add(new DocumentRunInline(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static byte[] BuildDocx(DocumentModel document)
    {
        using var stream = new MemoryStream();
        var exporter = new DocxExporter();
        exporter.Save(document, stream);
        return stream.ToArray();
    }

    private static async Task<(Window Window, StyleInclude Style)> ShowInWindowAsync(RichTextBox box, double width = 900, double height = 700)
    {
        var style = new StyleInclude(new Uri("avares://Vibe.Office.RichText.Avalonia/"))
        {
            Source = new Uri("avares://Vibe.Office.RichText.Avalonia/Themes/Generic.axaml")
        };
        Application.Current!.Styles.Add(style);

        var window = new Window
        {
            Content = box,
            Width = width,
            Height = height
        };
        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });
        box.Focus();
        await Dispatcher.UIThread.InvokeAsync(() => { });
        return (window, style);
    }

    private static void PressKey(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, MapPhysicalKey(key), string.Empty);
    }

    private static PhysicalKey MapPhysicalKey(Key key)
    {
        return key switch
        {
            Key.Home => PhysicalKey.Home,
            Key.End => PhysicalKey.End,
            Key.PageUp => PhysicalKey.PageUp,
            Key.PageDown => PhysicalKey.PageDown,
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.A => PhysicalKey.A,
            _ => PhysicalKey.None
        };
    }
}
