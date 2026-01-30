using System.Text;
using Vibe.Office.Layout;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public sealed class TextScriptTests
{
    [Fact]
    public void ClassifyRune_UsesUnicodeScriptProperty()
    {
        Assert.Equal(TextScriptKind.Latin, TextScript.ClassifyRune(new Rune('A')));
        Assert.Equal(TextScriptKind.EastAsian, TextScript.ClassifyRune(new Rune('\u6f22')));
        Assert.Equal(TextScriptKind.Complex, TextScript.ClassifyRune(new Rune('\u0627')));
    }

    [Fact]
    public void TryGetScriptTag_ReturnsLatnForLatinText()
    {
        Assert.True(TextScript.TryGetScriptTag("abc".AsSpan(), out var tag));
        Assert.Equal(Tag("Latn"), tag);
    }

    private static uint Tag(string tag)
    {
        var value = 0u;
        for (var i = 0; i < tag.Length; i++)
        {
            value = (value << 8) | tag[i];
        }

        return value;
    }
}
