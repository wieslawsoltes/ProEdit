using System;
using System.Linq;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Rendering;
using Xunit;

namespace Vibe.Office.OpenXml.Tests;

public sealed class SmartArtLayoutTests
{
    [Fact]
    public void Layout_UsesHierarchyAlgorithmDirection()
    {
        var diagram = BuildDiagram(
            "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<dgm:pt modelId=\"n1\" type=\"root\"><dgm:t><a:t>Root</a:t></dgm:t></dgm:pt>" +
            "<dgm:pt modelId=\"n2\"><dgm:t><a:t>Child 1</a:t></dgm:t></dgm:pt>" +
            "<dgm:pt modelId=\"n3\"><dgm:t><a:t>Child 2</a:t></dgm:t></dgm:pt>" +
            "<dgm:cxn srcId=\"n1\" destId=\"n2\"/>" +
            "<dgm:cxn srcId=\"n1\" destId=\"n3\"/>" +
            "</dgm:dataModel>",
            "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" name=\"orgChart\">" +
            "<dgm:layoutNode>" +
            "<dgm:alg type=\"hierarchy\"><dgm:param type=\"dir\" val=\"hor\"/></dgm:alg>" +
            "</dgm:layoutNode>" +
            "</dgm:layoutDef>");

        var layout = SmartArtLayoutEngine.TryBuildLayout(diagram, 420f, 180f);

        Assert.NotNull(layout);
        Assert.Equal(SmartArtLayoutKind.Hierarchy, layout!.Kind);
        Assert.Equal(3, layout.Nodes.Count);

        var root = layout.Nodes.Single(node => node.Id == "n1");
        var child = layout.Nodes.Single(node => node.Id == "n2");
        Assert.True(root.Bounds.X < child.Bounds.X);
        Assert.True(root.Level <= child.Level);
    }

    [Fact]
    public void Layout_RespectsMatrixRowHint()
    {
        var diagram = BuildDiagram(
            "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<dgm:pt modelId=\"n1\"><dgm:t><a:t>A</a:t></dgm:t></dgm:pt>" +
            "<dgm:pt modelId=\"n2\"><dgm:t><a:t>B</a:t></dgm:t></dgm:pt>" +
            "<dgm:pt modelId=\"n3\"><dgm:t><a:t>C</a:t></dgm:t></dgm:pt>" +
            "<dgm:pt modelId=\"n4\"><dgm:t><a:t>D</a:t></dgm:t></dgm:pt>" +
            "</dgm:dataModel>",
            "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" name=\"matrix\">" +
            "<dgm:layoutNode>" +
            "<dgm:alg type=\"matrix\"><dgm:param type=\"rows\" val=\"2\"/></dgm:alg>" +
            "</dgm:layoutNode>" +
            "</dgm:layoutDef>");

        var layout = SmartArtLayoutEngine.TryBuildLayout(diagram, 300f, 240f);

        Assert.NotNull(layout);
        Assert.Equal(SmartArtLayoutKind.Matrix, layout!.Kind);
        Assert.Equal(4, layout.Nodes.Count);

        var y0 = layout.Nodes[0].Bounds.Y;
        var y1 = layout.Nodes[1].Bounds.Y;
        var y2 = layout.Nodes[2].Bounds.Y;

        Assert.InRange(MathF.Abs(y0 - y1), 0f, 0.01f);
        Assert.True(y2 > y0);
    }

    private static DiagramInfo BuildDiagram(string dataXml, string layoutXml)
    {
        return new DiagramInfo
        {
            DataPart = Encoding.UTF8.GetBytes(dataXml),
            LayoutPart = Encoding.UTF8.GetBytes(layoutXml)
        };
    }
}
