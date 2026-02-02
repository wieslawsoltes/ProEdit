using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public class ProofingServiceTests
{
    [Fact]
    public void ProofingService_FlagsMisspelledWordsWithOffsets()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("hello wrld"));
        document.DefaultTextStyle.Language = "en-US";

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        var registry = SpellDictionaryRegistry.CreateDefault();
        var engine = new HunspellSpellEngine(registry);
        using var proofing = new EditorProofingService(session, engine, registry);

        proofing.RefreshAll();
        var diagnostics = proofing.GetParagraphDiagnostics(0);

        Assert.Single(diagnostics);
        var diagnostic = diagnostics[0];
        Assert.Equal(6, diagnostic.StartOffset);
        Assert.Equal(4, diagnostic.Length);
        Assert.Equal("wrld", diagnostic.Text);
    }
}
