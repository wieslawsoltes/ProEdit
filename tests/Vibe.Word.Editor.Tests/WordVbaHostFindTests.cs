using System;
using System.Collections.Generic;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Vba.Runtime;
using Vibe.Word.Editor;
using Vibe.Word.Editor.Editing;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public sealed class WordVbaHostFindTests
{
    [Fact]
    public void SelectionFind_Forward_WrapContinue_FindsNextThenWraps()
    {
        var host = CreateHost("alpha beta alpha", out _);
        SetSelection(host, 0, 0);
        ConfigureFind(host, "Selection", "alpha", forward: true, wrapMode: 1);

        Assert.True(ExecuteFind(host, "Selection"));
        AssertSelection(host, 0, 5);

        Assert.True(ExecuteFind(host, "Selection"));
        AssertSelection(host, 11, 16);

        Assert.True(ExecuteFind(host, "Selection"));
        AssertSelection(host, 0, 5);
    }

    [Fact]
    public void SelectionFind_Backward_WrapStop_FindsPreviousThenStops()
    {
        var host = CreateHost("alpha beta alpha", out _);
        var length = "alpha beta alpha".Length;
        SetSelection(host, length, length);
        ConfigureFind(host, "Selection", "alpha", forward: false, wrapMode: 0);

        Assert.True(ExecuteFind(host, "Selection"));
        AssertSelection(host, 11, 16);

        Assert.True(ExecuteFind(host, "Selection"));
        AssertSelection(host, 0, 5);

        Assert.False(ExecuteFind(host, "Selection"));
        AssertSelection(host, 0, 5);
    }

    [Fact]
    public void RangeFind_Forward_WrapStop_DoesNotWrap()
    {
        var host = CreateHost("alpha beta alpha", out _);
        SetSelection(host, 0, 10);
        var rangePath = GetSelectionRangePath(host);
        ConfigureFind(host, rangePath, "alpha", forward: true, wrapMode: 0);

        Assert.True(ExecuteFind(host, rangePath));
        AssertRange(host, rangePath, 0, 5);

        Assert.False(ExecuteFind(host, rangePath));
        AssertRange(host, rangePath, 0, 5);
    }

    [Fact]
    public void RangeFind_Backward_WrapContinue_WrapsWithinScope()
    {
        var host = CreateHost("alpha beta alpha", out _);
        SetSelection(host, 6, 16);
        var rangePath = GetSelectionRangePath(host);
        ConfigureFind(host, rangePath, "alpha", forward: false, wrapMode: 1);

        Assert.True(ExecuteFind(host, rangePath));
        AssertRange(host, rangePath, 11, 16);
    }

    [Fact]
    public void SelectionFind_MatchWildcards_FindsPattern()
    {
        var host = CreateHost("alpha beta", out _);
        SetSelection(host, 0, 0);
        ConfigureFind(host, "Selection", "a?pha", forward: true, wrapMode: 1);
        Assert.True(host.TrySetMember("Selection.Find.MatchWildcards", VbaValue.FromBoolean(true)));

        Assert.True(ExecuteFind(host, "Selection"));
        AssertSelection(host, 0, 5);
    }

    [Fact]
    public void ApplicationRun_ReturnsFunctionValue()
    {
        var host = CreateHost("alpha", out var session);
        var runtime = new VbaRuntime(host);
        host.SetRuntime(runtime);

        session.Document.Macros.IsTrusted = true;
        session.Document.Macros.Items.Add(new MacroDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Compute",
            Language = MacroLanguage.Vba,
            IsTrusted = true,
            Source = "Function Compute(a, b)\n    Compute = a + b\nEnd Function"
        });

        var arguments = new List<VbaValue>
        {
            VbaValue.FromString("Compute"),
            VbaValue.FromDouble(2d),
            VbaValue.FromDouble(3d)
        };

        Assert.True(host.TryInvokeMember("Application.Run", arguments, out var result));
        Assert.Equal(5d, result.AsDouble());
    }

    private static WordVbaHost CreateHost(string text, out EditorController session)
    {
        var document = new Document();
        session = new EditorController(new TestTextMeasurer(), document);
        if (!string.IsNullOrEmpty(text))
        {
            session.InsertText(text);
        }

        var selectionText = new EditorSelectionTextServiceAdapter(session);
        return new WordVbaHost(session, selectionText);
    }

    private static void ConfigureFind(WordVbaHost host, string targetPath, string text, bool forward, int wrapMode)
    {
        Assert.True(host.TrySetMember($"{targetPath}.Find.Text", VbaValue.FromString(text)));
        Assert.True(host.TrySetMember($"{targetPath}.Find.Forward", VbaValue.FromBoolean(forward)));
        Assert.True(host.TrySetMember($"{targetPath}.Find.Wrap", VbaValue.FromDouble(wrapMode)));
    }

    private static bool ExecuteFind(WordVbaHost host, string targetPath)
    {
        Assert.True(host.TryInvokeMember($"{targetPath}.Find.Execute", Array.Empty<VbaValue>(), out var result));
        return result.AsBoolean();
    }

    private static void SetSelection(WordVbaHost host, int start, int end)
    {
        var args = new[] { VbaValue.FromDouble(start), VbaValue.FromDouble(end) };
        Assert.True(host.TryInvokeMember("Selection.SetRange", args, out _));
    }

    private static string GetSelectionRangePath(WordVbaHost host)
    {
        Assert.True(host.TryGetMember("Selection.Range", out var rangeValue));
        var path = rangeValue.AsObjectPath();
        Assert.False(string.IsNullOrEmpty(path));
        return path!;
    }

    private static void AssertSelection(WordVbaHost host, int start, int end)
    {
        Assert.Equal(start, GetOffset(host, "Selection.Start"));
        Assert.Equal(end, GetOffset(host, "Selection.End"));
    }

    private static void AssertRange(WordVbaHost host, string rangePath, int start, int end)
    {
        Assert.Equal(start, GetOffset(host, $"{rangePath}.Start"));
        Assert.Equal(end, GetOffset(host, $"{rangePath}.End"));
    }

    private static int GetOffset(WordVbaHost host, string path)
    {
        Assert.True(host.TryGetMember(path, out var value));
        return (int)Math.Round(value.AsDouble());
    }

    private sealed class TestTextMeasurer : ITextMeasurerAdvancedSpan
    {
        public TextMetrics MeasureText(string text, TextStyle style)
        {
            return new TextMetrics(text.Length, 1f, 0.8f, 0.2f);
        }

        public TextMetrics MeasureText(ReadOnlySpan<char> text, TextStyle style)
        {
            return new TextMetrics(text.Length, 1f, 0.8f, 0.2f);
        }

        public TextShapeInfo ShapeText(string text, TextStyle style)
        {
            return BuildShape(text.Length);
        }

        public TextShapeInfo ShapeText(ReadOnlySpan<char> text, TextStyle style)
        {
            return BuildShape(text.Length);
        }

        private static TextShapeInfo BuildShape(int length)
        {
            if (length <= 0)
            {
                return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
            }

            var offsets = new int[length];
            var advances = new float[length];
            for (var i = 0; i < length; i++)
            {
                offsets[i] = i;
                advances[i] = 1f;
            }

            return new TextShapeInfo(length, offsets, advances);
        }
    }
}
