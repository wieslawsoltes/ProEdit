using System.Linq;
using System.Xml.Linq;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Layout.Tests;

public sealed class ShapePresetCatalogTests
{
    [Fact]
    public void GetPresets_MatchesEmbeddedPresetDefinitions()
    {
        var presets = ShapePresetCatalog.GetPresets();
        var expectedIds = GetEmbeddedPresetIds();

        Assert.Equal(expectedIds.Count, presets.Count);
        for (var i = 0; i < expectedIds.Count; i++)
        {
            Assert.Equal(expectedIds[i], presets[i].Id);
            Assert.False(string.IsNullOrWhiteSpace(presets[i].DisplayName));
        }
    }

    [Fact]
    public void GetPresets_ContainsCoreShapePrimitives()
    {
        var presets = ShapePresetCatalog.GetPresets()
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var expected = new[]
        {
            "rect",
            "roundrect",
            "ellipse",
            "line",
            "triangle",
            "diamond",
            "rightArrow"
        };

        foreach (var id in expected)
        {
            Assert.True(presets.ContainsKey(id), $"Missing shape preset '{id}'.");
            var geometry = ShapeGeometryEvaluator.ResolveGeometry(new ShapeProperties
            {
                PresetGeometry = id
            });
            Assert.NotNull(geometry);
        }
    }

    private static List<string> GetEmbeddedPresetIds()
    {
        var assembly = typeof(ShapeInline).Assembly;
        using var stream = assembly.GetManifestResourceStream("ProEdit.Documents.Shapes.presetShapeDefinitions.xml");
        Assert.NotNull(stream);

        var document = XDocument.Load(stream!);
        var root = Assert.IsType<XElement>(document.Root);
        return root.Elements()
            .Select(element => element.Name.LocalName)
            .ToList();
    }
}
