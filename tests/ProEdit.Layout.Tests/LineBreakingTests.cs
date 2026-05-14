using System.Linq;
using ProEdit.Documents;
using ProEdit.Layout;
using Xunit;

namespace ProEdit.Layout.Tests;

public sealed class LineBreakingTests
{
    [Fact]
    public void Uax14LineBreaker_BreaksOnHyphen()
    {
        var text = "alpha-beta";
        var options = new LineBreakOptions(new DocumentCompatibilitySettings());

        var lines = Uax14LineBreaker.BreakParagraph(
            text,
            6f,
            6f,
            options,
            (_, length) => length,
            (_, length) => length).ToList();

        Assert.Equal(new[] { "alpha-", "beta" }, lines.Select(line => line.Text(text)).ToArray());
    }

    [Fact]
    public void Uax14LineBreaker_BreaksOnCrLf()
    {
        var text = "alpha\r\nbeta";
        var options = new LineBreakOptions(new DocumentCompatibilitySettings());

        var lines = Uax14LineBreaker.BreakParagraph(
            text,
            20f,
            20f,
            options,
            (_, length) => length,
            (_, length) => length).ToList();

        Assert.Equal(new[] { "alpha", "beta" }, lines.Select(line => line.Text(text)).ToArray());
    }
}
