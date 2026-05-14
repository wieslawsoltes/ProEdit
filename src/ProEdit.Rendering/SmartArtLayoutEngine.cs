using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Rendering;

public static class SmartArtLayoutEngine
{
    private const string DiagramNamespaceFragment = "diagram";
    private const string DrawingNamespaceFragment = "drawingml/2006/main";
    private const int MaxTextLength = 2048;

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Ignore
    };

    public static SmartArtLayout? TryBuildLayout(DiagramInfo diagram, float width, float height)
    {
        if (diagram is null)
        {
            return null;
        }

        var data = diagram.DataPart;
        if (data is null || data.Length == 0)
        {
            return null;
        }

        if (width <= 1f || height <= 1f)
        {
            return null;
        }

        var graph = ParseDiagramData(data);
        if (graph.Nodes.Count == 0)
        {
            return null;
        }

        var layoutDefinition = ParseLayoutDefinition(diagram.LayoutPart);
        var options = ResolveLayoutOptions(layoutDefinition);
        var style = ParseSmartArtStyle(diagram);
        return BuildLayout(graph, options, width, height, style);
    }

    private static DiagramGraph ParseDiagramData(byte[] data)
    {
        var nodes = new List<DiagramNode>();
        var connections = new List<DiagramConnection>();
        using var stream = new MemoryStream(data, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var localName = reader.LocalName;
            if (localName.Equals("pt", StringComparison.OrdinalIgnoreCase)
                && IsDiagramNamespace(reader.NamespaceURI))
            {
                var node = ReadPoint(reader);
                if (!string.IsNullOrWhiteSpace(node.Id))
                {
                    nodes.Add(node);
                }

                continue;
            }

            if (localName.Equals("cxn", StringComparison.OrdinalIgnoreCase)
                && IsDiagramNamespace(reader.NamespaceURI))
            {
                var sourceId = reader.GetAttribute("srcId");
                var targetId = reader.GetAttribute("destId");
                if (!string.IsNullOrWhiteSpace(sourceId) && !string.IsNullOrWhiteSpace(targetId))
                {
                    connections.Add(new DiagramConnection(sourceId, targetId));
                }
            }
        }

        return new DiagramGraph(nodes, connections);
    }

    private static DiagramNode ReadPoint(XmlReader reader)
    {
        var id = reader.GetAttribute("modelId") ?? reader.GetAttribute("id") ?? string.Empty;
        var type = reader.GetAttribute("type") ?? string.Empty;
        var text = ReadPointText(reader);
        var isVirtual = string.IsNullOrWhiteSpace(text)
                        || type.Equals("doc", StringComparison.OrdinalIgnoreCase);
        return new DiagramNode(id, type, text, isVirtual);
    }

    private static string ReadPointText(XmlReader reader)
    {
        var builder = new StringBuilder();
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!subtree.LocalName.Equals("t", StringComparison.OrdinalIgnoreCase)
                || !IsDrawingNamespace(subtree.NamespaceURI))
            {
                continue;
            }

            var value = subtree.ReadElementContentAsString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            var remaining = MaxTextLength - builder.Length;
            if (remaining <= 0)
            {
                break;
            }

            if (value.Length > remaining)
            {
                builder.Append(value.AsSpan(0, remaining));
                break;
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    private static SmartArtLayoutDefinition ParseLayoutDefinition(byte[]? layoutPart)
    {
        if (layoutPart is null || layoutPart.Length == 0)
        {
            return new SmartArtLayoutDefinition();
        }

        var definition = new SmartArtLayoutDefinition();
        using var stream = new MemoryStream(layoutPart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!IsDiagramNamespace(reader.NamespaceURI))
            {
                continue;
            }

            if (reader.LocalName.Equals("layoutDef", StringComparison.OrdinalIgnoreCase))
            {
                definition.Name = reader.GetAttribute("name");
                definition.Title = reader.GetAttribute("title") ?? reader.GetAttribute("desc");
                CaptureDirectionAttributes(reader, definition);
                continue;
            }

            if (reader.LocalName.Equals("cat", StringComparison.OrdinalIgnoreCase))
            {
                var name = reader.GetAttribute("name") ?? reader.GetAttribute("type");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    definition.Categories.Add(name);
                }

                CaptureDirectionAttributes(reader, definition);
                continue;
            }

            if (reader.LocalName.Equals("styleLbl", StringComparison.OrdinalIgnoreCase))
            {
                CaptureDirectionAttributes(reader, definition);
                continue;
            }

            if (reader.LocalName.Equals("alg", StringComparison.OrdinalIgnoreCase))
            {
                var algorithm = ReadAlgorithm(reader);
                if (!string.IsNullOrWhiteSpace(algorithm.Type))
                {
                    definition.Algorithms.Add(algorithm);
                }

                reader.Skip();
                continue;
            }

            CaptureDirectionAttributes(reader, definition);
        }

        if (string.IsNullOrWhiteSpace(definition.Name) && layoutPart.Length > 0)
        {
            var data = layoutPart.AsSpan();
            if (ContainsAsciiIgnoreCase(data, "cycle"))
            {
                definition.Name = "cycle";
            }
            else if (ContainsAsciiIgnoreCase(data, "process"))
            {
                definition.Name = "process";
            }
            else if (ContainsAsciiIgnoreCase(data, "hierarchy"))
            {
                definition.Name = "hierarchy";
            }
            else if (ContainsAsciiIgnoreCase(data, "matrix") || ContainsAsciiIgnoreCase(data, "grid"))
            {
                definition.Name = "matrix";
            }
            else if (ContainsAsciiIgnoreCase(data, "relationship") || ContainsAsciiIgnoreCase(data, "radial"))
            {
                definition.Name = "relationship";
            }
            else if (ContainsAsciiIgnoreCase(data, "pyramid"))
            {
                definition.Name = "pyramid";
            }
        }

        return definition;
    }

    private static SmartArtAlgorithm ReadAlgorithm(XmlReader reader)
    {
        var type = reader.GetAttribute("type") ?? string.Empty;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                if (reader.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(reader.Value))
                {
                    parameters[reader.LocalName] = reader.Value;
                }
            }

            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
        {
            return new SmartArtAlgorithm(type, parameters);
        }

        using var subtree = reader.ReadSubtree();
        if (!subtree.Read())
        {
            return new SmartArtAlgorithm(type, parameters);
        }

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!subtree.LocalName.Equals("param", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = subtree.GetAttribute("type") ?? subtree.GetAttribute("name");
            var value = subtree.GetAttribute("val") ?? subtree.GetAttribute("value");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parameters[name] = value;
        }

        return new SmartArtAlgorithm(type, parameters);
    }

    private static SmartArtLayoutKind ResolveLayoutKind(SmartArtLayoutDefinition definition)
    {
        var algorithmKind = ResolveLayoutKindFromAlgorithms(definition);
        if (algorithmKind.HasValue)
        {
            return algorithmKind.Value;
        }

        if (definition.MatchesAny("relationship", "radial", "target", "venn"))
        {
            return SmartArtLayoutKind.Relationship;
        }

        if (definition.MatchesAny("pyramid", "funnel", "inverted"))
        {
            return SmartArtLayoutKind.Pyramid;
        }

        if (definition.MatchesAny("cycle", "circular", "ring", "loop"))
        {
            return SmartArtLayoutKind.Cycle;
        }

        if (definition.MatchesAny("process", "chevron", "arrow", "timeline", "step"))
        {
            return SmartArtLayoutKind.Process;
        }

        if (definition.MatchesAny("hierarchy", "org", "organization", "tree"))
        {
            return SmartArtLayoutKind.Hierarchy;
        }

        if (definition.MatchesAny("matrix", "grid", "table"))
        {
            return SmartArtLayoutKind.Matrix;
        }

        return SmartArtLayoutKind.List;
    }

    private static SmartArtLayoutDirection ResolveLayoutDirection(SmartArtLayoutDefinition definition, SmartArtLayoutKind kind)
    {
        if (definition.Direction.HasValue)
        {
            return definition.Direction.Value;
        }

        if (TryResolveDirectionFromAlgorithms(definition, out var algorithmDirection))
        {
            return algorithmDirection;
        }

        if (definition.MatchesAny("vertical", "vert", "t2b", "b2t"))
        {
            return SmartArtLayoutDirection.Vertical;
        }

        if (definition.MatchesAny("horizontal", "horz", "ltr", "rtl"))
        {
            return SmartArtLayoutDirection.Horizontal;
        }

        return kind switch
        {
            SmartArtLayoutKind.Process => SmartArtLayoutDirection.Horizontal,
            SmartArtLayoutKind.Matrix => SmartArtLayoutDirection.Horizontal,
            _ => SmartArtLayoutDirection.Vertical
        };
    }

    private static SmartArtLayoutOptions ResolveLayoutOptions(SmartArtLayoutDefinition definition)
    {
        var kind = ResolveLayoutKind(definition);
        var direction = ResolveLayoutDirection(definition, kind);
        var flow = ResolveLayoutFlow(definition, kind);
        ResolveGridHints(definition, out var rows, out var columns);
        var invertPyramid = kind == SmartArtLayoutKind.Pyramid
                            && definition.MatchesAny("funnel", "inverted", "reverse");
        return new SmartArtLayoutOptions(kind, direction, flow, rows, columns, invertPyramid);
    }

    private static SmartArtLayoutFlow ResolveLayoutFlow(SmartArtLayoutDefinition definition, SmartArtLayoutKind kind)
    {
        if (kind is not SmartArtLayoutKind.Process and not SmartArtLayoutKind.List)
        {
            return SmartArtLayoutFlow.None;
        }

        foreach (var algorithm in definition.Algorithms)
        {
            if (ContainsKeyword(algorithm.Type, "snake")
                || ContainsKeyword(algorithm.Type, "zig")
                || ContainsKeyword(algorithm.Type, "bend"))
            {
                return SmartArtLayoutFlow.Snake;
            }
        }

        if (definition.MatchesAny("snake", "zig", "zigzag", "bent", "chevron", "alternat"))
        {
            return SmartArtLayoutFlow.Snake;
        }

        if (definition.MatchesAny("wrap", "grid", "matrix", "table"))
        {
            return SmartArtLayoutFlow.Wrap;
        }

        return SmartArtLayoutFlow.None;
    }

    private static void ResolveGridHints(
        SmartArtLayoutDefinition definition,
        out int? rows,
        out int? columns)
    {
        rows = null;
        columns = null;

        foreach (var algorithm in definition.Algorithms)
        {
            foreach (var pair in algorithm.Parameters)
            {
                if (rows is null && ContainsKeyword(pair.Key, "row") && TryParsePositiveInt(pair.Value, out var rowCount))
                {
                    rows = rowCount;
                }

                if (columns is null
                    && (ContainsKeyword(pair.Key, "col") || ContainsKeyword(pair.Key, "column"))
                    && TryParsePositiveInt(pair.Value, out var columnCount))
                {
                    columns = columnCount;
                }
            }
        }
    }

    private static SmartArtLayoutKind? ResolveLayoutKindFromAlgorithms(SmartArtLayoutDefinition definition)
    {
        foreach (var algorithm in definition.Algorithms)
        {
            if (string.IsNullOrWhiteSpace(algorithm.Type))
            {
                continue;
            }

            if (ContainsKeyword(algorithm.Type, "hier") || ContainsKeyword(algorithm.Type, "org"))
            {
                return SmartArtLayoutKind.Hierarchy;
            }

            if (ContainsKeyword(algorithm.Type, "pyr") || ContainsKeyword(algorithm.Type, "funnel"))
            {
                return SmartArtLayoutKind.Pyramid;
            }

            if (ContainsKeyword(algorithm.Type, "cycle") || ContainsKeyword(algorithm.Type, "circ"))
            {
                return SmartArtLayoutKind.Cycle;
            }

            if (ContainsKeyword(algorithm.Type, "radial")
                || ContainsKeyword(algorithm.Type, "rel")
                || ContainsKeyword(algorithm.Type, "venn")
                || ContainsKeyword(algorithm.Type, "target"))
            {
                return SmartArtLayoutKind.Relationship;
            }

            if (ContainsKeyword(algorithm.Type, "matrix") || ContainsKeyword(algorithm.Type, "grid"))
            {
                return SmartArtLayoutKind.Matrix;
            }

            if (ContainsKeyword(algorithm.Type, "process")
                || ContainsKeyword(algorithm.Type, "lin")
                || ContainsKeyword(algorithm.Type, "flow"))
            {
                return SmartArtLayoutKind.Process;
            }

            if (ContainsKeyword(algorithm.Type, "list") || ContainsKeyword(algorithm.Type, "stack"))
            {
                return SmartArtLayoutKind.List;
            }
        }

        return null;
    }

    private static bool TryResolveDirectionFromAlgorithms(SmartArtLayoutDefinition definition, out SmartArtLayoutDirection direction)
    {
        foreach (var algorithm in definition.Algorithms)
        {
            if (TryParseDirection(algorithm.Type, out direction))
            {
                return true;
            }

            foreach (var pair in algorithm.Parameters)
            {
                if (pair.Key.IndexOf("dir", StringComparison.OrdinalIgnoreCase) >= 0
                    || pair.Key.IndexOf("orient", StringComparison.OrdinalIgnoreCase) >= 0
                    || pair.Key.IndexOf("flow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (TryParseDirection(pair.Value, out direction))
                    {
                        return true;
                    }
                }
            }
        }

        direction = default;
        return false;
    }

    private static SmartArtLayout BuildLayout(
        DiagramGraph graph,
        SmartArtLayoutOptions options,
        float width,
        float height,
        SmartArtStyle? style)
    {
        var drawable = graph.Nodes.Where(node => !node.IsVirtual).ToList();
        if (drawable.Count == 0)
        {
            drawable = graph.Nodes.ToList();
        }

        var margin = MathF.Max(6f, MathF.Min(width, height) * 0.06f);
        var gap = MathF.Max(6f, MathF.Min(width, height) * 0.04f);
        var availableWidth = MathF.Max(1f, width - margin * 2f);
        var availableHeight = MathF.Max(1f, height - margin * 2f);

        var ordered = OrderNodesForLayout(graph, drawable, options.Kind);

        var nodes = options.Kind switch
        {
            SmartArtLayoutKind.Process => LayoutProcess(ordered, margin, availableWidth, availableHeight, gap, options),
            SmartArtLayoutKind.Cycle => LayoutCycle(ordered, margin, availableWidth, availableHeight),
            SmartArtLayoutKind.Hierarchy => LayoutHierarchy(graph, ordered, margin, availableWidth, availableHeight, gap, options.Direction),
            SmartArtLayoutKind.Matrix => LayoutMatrix(ordered, margin, availableWidth, availableHeight, gap, options),
            SmartArtLayoutKind.Relationship => LayoutRelationship(graph, ordered, margin, availableWidth, availableHeight, gap),
            SmartArtLayoutKind.Pyramid => LayoutPyramid(graph, ordered, margin, availableWidth, availableHeight, gap, options),
            _ => LayoutList(ordered, margin, availableWidth, availableHeight, gap, options)
        };

        var connectors = BuildConnectors(graph, nodes, options.Kind);
        return new SmartArtLayout(options.Kind, nodes, connectors, style);
    }

    private static List<DiagramNode> OrderNodesForLayout(
        DiagramGraph graph,
        List<DiagramNode> nodes,
        SmartArtLayoutKind kind)
    {
        if (nodes.Count <= 2)
        {
            return nodes;
        }

        return kind switch
        {
            SmartArtLayoutKind.Cycle => OrderNodesByConnections(graph, nodes, allowCycle: true),
            SmartArtLayoutKind.Process => OrderNodesByConnections(graph, nodes, allowCycle: false),
            SmartArtLayoutKind.List => OrderNodesByConnections(graph, nodes, allowCycle: false),
            _ => nodes
        };
    }

    private static List<DiagramNode> OrderNodesByConnections(
        DiagramGraph graph,
        List<DiagramNode> nodes,
        bool allowCycle)
    {
        if (nodes.Count <= 1)
        {
            return nodes;
        }

        var lookup = new Dictionary<string, DiagramNode>(StringComparer.OrdinalIgnoreCase);
        var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            var id = nodes[i].Id;
            if (!lookup.ContainsKey(id))
            {
                lookup[id] = nodes[i];
            }

            if (!orderIndex.ContainsKey(id))
            {
                orderIndex[id] = i;
            }
        }

        var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            incoming[node.Id] = 0;
        }

        foreach (var connection in graph.Connections)
        {
            if (!lookup.ContainsKey(connection.FromId) || !lookup.ContainsKey(connection.ToId))
            {
                continue;
            }

            if (!outgoing.TryGetValue(connection.FromId, out var list))
            {
                list = new List<string>();
                outgoing[connection.FromId] = list;
            }

            list.Add(connection.ToId);
            incoming[connection.ToId] = incoming.TryGetValue(connection.ToId, out var count) ? count + 1 : 1;
        }

        foreach (var pair in outgoing)
        {
            pair.Value.Sort((left, right) =>
            {
                var leftIndex = orderIndex.TryGetValue(left, out var l) ? l : int.MaxValue;
                var rightIndex = orderIndex.TryGetValue(right, out var r) ? r : int.MaxValue;
                return leftIndex.CompareTo(rightIndex);
            });
        }

        var startId = string.Empty;
        foreach (var node in nodes)
        {
            if (incoming.TryGetValue(node.Id, out var count) && count == 0)
            {
                startId = node.Id;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(startId) && allowCycle)
        {
            startId = nodes[0].Id;
        }

        if (string.IsNullOrWhiteSpace(startId) || !lookup.TryGetValue(startId, out _))
        {
            return nodes;
        }

        var ordered = new List<DiagramNode>(nodes.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = startId;
        while (!string.IsNullOrWhiteSpace(currentId) && !visited.Contains(currentId))
        {
            visited.Add(currentId);
            ordered.Add(lookup[currentId]);

            if (outgoing.TryGetValue(currentId, out var list))
            {
                var nextId = string.Empty;
                for (var i = 0; i < list.Count; i++)
                {
                    if (!visited.Contains(list[i]))
                    {
                        nextId = list[i];
                        break;
                    }
                }

                currentId = nextId;
            }
            else
            {
                break;
            }
        }

        if (ordered.Count < nodes.Count)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!visited.Contains(node.Id))
                {
                    ordered.Add(node);
                }
            }
        }

        return ordered;
    }

    private static List<SmartArtConnectorLayout> BuildConnectors(
        DiagramGraph graph,
        List<SmartArtNodeLayout> nodes,
        SmartArtLayoutKind kind)
    {
        var lookup = nodes.ToDictionary(node => node.Id, node => node);
        var connectors = new List<SmartArtConnectorLayout>();
        foreach (var connection in graph.Connections)
        {
            if (lookup.ContainsKey(connection.FromId) && lookup.ContainsKey(connection.ToId))
            {
                connectors.Add(new SmartArtConnectorLayout(connection.FromId, connection.ToId));
            }
        }

        if (connectors.Count == 0 && nodes.Count > 1)
        {
            if (kind == SmartArtLayoutKind.Relationship)
            {
                var center = nodes.OrderBy(node => node.Level).ThenBy(node => node.Index).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(center.Id))
                {
                    for (var i = 0; i < nodes.Count; i++)
                    {
                        if (nodes[i].Id == center.Id)
                        {
                            continue;
                        }

                        connectors.Add(new SmartArtConnectorLayout(center.Id, nodes[i].Id));
                    }
                }
            }
            else if (kind == SmartArtLayoutKind.Hierarchy || kind == SmartArtLayoutKind.Pyramid)
            {
                var grouped = nodes.GroupBy(node => node.Level).OrderBy(group => group.Key).ToList();
                for (var levelIndex = 1; levelIndex < grouped.Count; levelIndex++)
                {
                    var parents = grouped[levelIndex - 1].ToArray();
                    var children = grouped[levelIndex].ToArray();
                    if (parents.Length == 0 || children.Length == 0)
                    {
                        continue;
                    }

                    var parentCenter = AverageCenter(parents);
                    var childCenter = AverageCenter(children);
                    var deltaX = MathF.Abs(childCenter.X - parentCenter.X);
                    var deltaY = MathF.Abs(childCenter.Y - parentCenter.Y);
                    var matchByX = deltaY >= deltaX;

                    foreach (var child in children)
                    {
                        var target = matchByX ? CenterX(child) : CenterY(child);
                        var bestParent = parents[0];
                        var bestDistance = float.MaxValue;
                        for (var parentIndex = 0; parentIndex < parents.Length; parentIndex++)
                        {
                            var parent = parents[parentIndex];
                            var source = matchByX ? CenterX(parent) : CenterY(parent);
                            var distance = MathF.Abs(target - source);
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestParent = parent;
                            }
                        }

                        connectors.Add(new SmartArtConnectorLayout(bestParent.Id, child.Id));
                    }
                }
            }
            else
            {
                for (var i = 1; i < nodes.Count; i++)
                {
                    connectors.Add(new SmartArtConnectorLayout(nodes[i - 1].Id, nodes[i].Id));
                }

                if (kind == SmartArtLayoutKind.Cycle && nodes.Count > 2)
                {
                    connectors.Add(new SmartArtConnectorLayout(nodes[^1].Id, nodes[0].Id));
                }
            }
        }

        return connectors;
    }

    private static List<SmartArtNodeLayout> LayoutList(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutOptions options)
    {
        var flow = options.Flow;
        if (flow == SmartArtLayoutFlow.None && nodes.Count > 8)
        {
            flow = SmartArtLayoutFlow.Wrap;
        }

        return LayoutLinear(
            nodes,
            margin,
            availableWidth,
            availableHeight,
            gap,
            options.Direction,
            flow,
            options.Rows,
            options.Columns,
            widthScale: 0.9f,
            heightScale: options.Direction == SmartArtLayoutDirection.Horizontal ? 0.85f : 0.9f);
    }

    private static List<SmartArtNodeLayout> LayoutProcess(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutOptions options)
    {
        var flow = options.Flow;
        if (flow == SmartArtLayoutFlow.None && nodes.Count > 6)
        {
            flow = SmartArtLayoutFlow.Wrap;
        }

        var widthScale = options.Direction == SmartArtLayoutDirection.Horizontal ? 0.9f : 0.7f;
        var heightScale = options.Direction == SmartArtLayoutDirection.Horizontal ? 0.65f : 0.85f;

        return LayoutLinear(
            nodes,
            margin,
            availableWidth,
            availableHeight,
            gap,
            options.Direction,
            flow,
            options.Rows,
            options.Columns,
            widthScale,
            heightScale);
    }

    private static List<SmartArtNodeLayout> LayoutLinear(
        IReadOnlyList<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutDirection direction,
        SmartArtLayoutFlow flow,
        int? rows,
        int? columns,
        float widthScale,
        float heightScale)
    {
        var count = nodes.Count;
        var layouts = new List<SmartArtNodeLayout>(count);
        if (count == 0)
        {
            return layouts;
        }

        ResolveLinearGrid(count, direction, flow, rows, columns, out var primaryCount, out var secondaryCount);
        var columnsCount = direction == SmartArtLayoutDirection.Horizontal ? primaryCount : secondaryCount;
        var rowsCount = direction == SmartArtLayoutDirection.Horizontal ? secondaryCount : primaryCount;

        columnsCount = Math.Max(1, columnsCount);
        rowsCount = Math.Max(1, rowsCount);

        var cellWidth = (availableWidth - gap * MathF.Max(0, columnsCount - 1)) / columnsCount;
        var cellHeight = (availableHeight - gap * MathF.Max(0, rowsCount - 1)) / rowsCount;
        var nodeWidth = MathF.Max(1f, cellWidth * MathF.Min(1f, widthScale));
        var nodeHeight = MathF.Max(1f, cellHeight * MathF.Min(1f, heightScale));

        for (var i = 0; i < count; i++)
        {
            var primaryIndex = i % primaryCount;
            var secondaryIndex = i / primaryCount;
            if (flow == SmartArtLayoutFlow.Snake && secondaryIndex % 2 == 1)
            {
                primaryIndex = primaryCount - 1 - primaryIndex;
            }

            int rowIndex;
            int columnIndex;
            if (direction == SmartArtLayoutDirection.Horizontal)
            {
                rowIndex = secondaryIndex;
                columnIndex = primaryIndex;
            }
            else
            {
                rowIndex = primaryIndex;
                columnIndex = secondaryIndex;
            }

            var x = margin + columnIndex * (cellWidth + gap) + (cellWidth - nodeWidth) / 2f;
            var y = margin + rowIndex * (cellHeight + gap) + (cellHeight - nodeHeight) / 2f;
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeWidth, nodeHeight),
                rowIndex,
                i));
        }

        return layouts;
    }

    private static void ResolveLinearGrid(
        int count,
        SmartArtLayoutDirection direction,
        SmartArtLayoutFlow flow,
        int? rows,
        int? columns,
        out int primaryCount,
        out int secondaryCount)
    {
        if (count <= 0)
        {
            primaryCount = 0;
            secondaryCount = 0;
            return;
        }

        if (direction == SmartArtLayoutDirection.Horizontal)
        {
            if (columns.HasValue)
            {
                primaryCount = Math.Clamp(columns.Value, 1, count);
                secondaryCount = (int)MathF.Ceiling(count / (float)primaryCount);
                return;
            }

            if (rows.HasValue)
            {
                secondaryCount = Math.Clamp(rows.Value, 1, count);
                primaryCount = (int)MathF.Ceiling(count / (float)secondaryCount);
                return;
            }
        }
        else
        {
            if (rows.HasValue)
            {
                primaryCount = Math.Clamp(rows.Value, 1, count);
                secondaryCount = (int)MathF.Ceiling(count / (float)primaryCount);
                return;
            }

            if (columns.HasValue)
            {
                secondaryCount = Math.Clamp(columns.Value, 1, count);
                primaryCount = (int)MathF.Ceiling(count / (float)secondaryCount);
                return;
            }
        }

        primaryCount = ResolveDefaultPrimaryCount(count, flow);
        secondaryCount = (int)MathF.Ceiling(count / (float)primaryCount);
    }

    private static int ResolveDefaultPrimaryCount(int count, SmartArtLayoutFlow flow)
    {
        if (count <= 1)
        {
            return count;
        }

        if (flow == SmartArtLayoutFlow.None)
        {
            return count;
        }

        if (count <= 4)
        {
            return count;
        }

        var target = (int)MathF.Ceiling(MathF.Sqrt(count));
        return Math.Clamp(target, 3, Math.Min(8, count));
    }

    private static List<SmartArtNodeLayout> LayoutCycle(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight)
    {
        var count = nodes.Count;
        var size = MathF.Min(availableWidth, availableHeight);
        var scale = count <= 4 ? 0.25f : count <= 6 ? 0.2f : 0.16f;
        var nodeSize = MathF.Max(20f, size * scale);
        var radiusX = MathF.Max(nodeSize, availableWidth / 2f - nodeSize / 2f);
        var radiusY = MathF.Max(nodeSize, availableHeight / 2f - nodeSize / 2f);
        var centerX = margin + availableWidth / 2f;
        var centerY = margin + availableHeight / 2f;

        var layouts = new List<SmartArtNodeLayout>(count);
        for (var i = 0; i < count; i++)
        {
            var angle = -MathF.PI / 2f + i * (MathF.Tau / MathF.Max(1, count));
            var x = centerX + MathF.Cos(angle) * radiusX - nodeSize / 2f;
            var y = centerY + MathF.Sin(angle) * radiusY - nodeSize / 2f;
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeSize, nodeSize),
                0,
                i));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutHierarchy(
        DiagramGraph graph,
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutDirection direction)
    {
        if (nodes.Count == 0)
        {
            return new List<SmartArtNodeLayout>();
        }

        var forest = BuildHierarchyForest(graph, nodes, out var treeMap);
        if (forest.Count == 0)
        {
            return LayoutLinear(
                nodes,
                margin,
                availableWidth,
                availableHeight,
                gap,
                direction,
                SmartArtLayoutFlow.None,
                null,
                null,
                0.85f,
                0.85f);
        }

        var leafIndex = 0;
        for (var i = 0; i < forest.Count; i++)
        {
            leafIndex = AssignLeafCenters(forest[i], leafIndex);
        }

        var leafCount = Math.Max(1, leafIndex);
        var levelCounts = new Dictionary<int, int>();
        var maxDepth = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            var depth = 0;
            if (treeMap.TryGetValue(nodes[i].Id, out var treeNode))
            {
                depth = treeNode.Depth;
            }

            levelCounts[depth] = levelCounts.TryGetValue(depth, out var count) ? count + 1 : 1;
            if (depth > maxDepth)
            {
                maxDepth = depth;
            }
        }

        var levelCount = Math.Max(1, maxDepth + 1);
        var primaryAvailable = direction == SmartArtLayoutDirection.Vertical ? availableHeight : availableWidth;
        var secondaryAvailable = direction == SmartArtLayoutDirection.Vertical ? availableWidth : availableHeight;
        var primarySize = (primaryAvailable - gap * MathF.Max(0, levelCount - 1)) / levelCount;
        primarySize = MathF.Max(1f, primarySize);

        var leafSpacing = (secondaryAvailable - gap * MathF.Max(0, leafCount - 1)) / leafCount;
        leafSpacing = MathF.Max(1f, leafSpacing);
        var leafStep = leafSpacing + gap;

        var layouts = new List<SmartArtNodeLayout>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (!treeMap.TryGetValue(node.Id, out var treeNode))
            {
                treeNode = new TreeLayoutNode(node)
                {
                    Depth = 0,
                    Center = i
                };
            }

            var depth = treeNode.Depth;
            var centerIndex = treeNode.Center;
            var secondaryCenter = margin + centerIndex * leafStep + leafSpacing / 2f;
            var levelNodeCount = levelCounts.TryGetValue(depth, out var count) ? count : 1;
            var secondarySize = (secondaryAvailable - gap * MathF.Max(0, levelNodeCount - 1)) / levelNodeCount;
            secondarySize = MathF.Max(1f, MathF.Min(secondarySize, leafSpacing));

            var primaryPos = margin + depth * (primarySize + gap);
            float x;
            float y;
            float width;
            float height;

            if (direction == SmartArtLayoutDirection.Vertical)
            {
                width = secondarySize;
                height = primarySize;
                x = secondaryCenter - width / 2f;
                y = primaryPos;
            }
            else
            {
                width = primarySize;
                height = secondarySize;
                x = primaryPos;
                y = secondaryCenter - height / 2f;
            }

            layouts.Add(new SmartArtNodeLayout(
                node.Id,
                node.GetDisplayText(i),
                new DocRect(x, y, width, height),
                depth,
                i));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutMatrix(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutOptions options)
    {
        var count = nodes.Count;
        var layouts = new List<SmartArtNodeLayout>(count);
        if (count == 0)
        {
            return layouts;
        }

        ResolveMatrixGrid(count, options, out var rows, out var columns);
        if (options.Direction == SmartArtLayoutDirection.Vertical && rows < columns)
        {
            (rows, columns) = (columns, rows);
        }

        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        var nodeWidth = (availableWidth - gap * MathF.Max(0, columns - 1)) / columns;
        var nodeHeight = (availableHeight - gap * MathF.Max(0, rows - 1)) / rows;

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var x = margin + column * (nodeWidth + gap);
            var y = margin + row * (nodeHeight + gap);
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeWidth, nodeHeight),
                row,
                i));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutRelationship(
        DiagramGraph graph,
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var count = nodes.Count;
        if (count <= 1)
        {
            var options = new SmartArtLayoutOptions(
                SmartArtLayoutKind.Relationship,
                SmartArtLayoutDirection.Vertical,
                SmartArtLayoutFlow.None,
                null,
                null,
                false);
            return LayoutList(nodes, margin, availableWidth, availableHeight, gap, options);
        }

        var indexLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            indexLookup[nodes[i].Id] = i;
        }

        var centerNode = ResolveRelationshipCenter(graph, nodes);
        var centerIndex = indexLookup.TryGetValue(centerNode.Id, out var resolvedIndex) ? resolvedIndex : 0;
        var distances = ComputeRelationshipDistances(graph, nodes, centerNode.Id);

        var size = MathF.Min(availableWidth, availableHeight);
        var ringCount = Math.Max(1, distances.Values.Max());
        var centerSize = MathF.Max(22f, size * (ringCount > 1 ? 0.28f : 0.32f));
        var maxNodeSize = MathF.Max(18f, size * 0.18f);
        var maxRadius = MathF.Max(centerSize / 2f + maxNodeSize / 2f, size / 2f - maxNodeSize / 2f);
        var ringStep = ringCount > 0 ? maxRadius / ringCount : maxRadius;

        var centerX = margin + availableWidth / 2f;
        var centerY = margin + availableHeight / 2f;
        var layouts = new List<SmartArtNodeLayout>(count);

        layouts.Add(new SmartArtNodeLayout(
            centerNode.Id,
            centerNode.GetDisplayText(centerIndex),
            new DocRect(centerX - centerSize / 2f, centerY - centerSize / 2f, centerSize, centerSize),
            0,
            centerIndex));

        var ringGroups = new SortedDictionary<int, List<DiagramNode>>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (string.Equals(node.Id, centerNode.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var distance = distances.TryGetValue(node.Id, out var level) ? level : 1;
            if (!ringGroups.TryGetValue(distance, out var list))
            {
                list = new List<DiagramNode>();
                ringGroups[distance] = list;
            }

            list.Add(node);
        }

        foreach (var ring in ringGroups)
        {
            var ringNodes = ring.Value;
            if (ringNodes.Count == 0)
            {
                continue;
            }

            var ringIndex = ring.Key;
            var ringRadius = ringStep * ringIndex;
            var nodeSize = MathF.Max(14f, MathF.Min(maxNodeSize, MathF.Max(4f, ringStep - gap) * 0.9f));
            var angleStep = MathF.Tau / ringNodes.Count;
            for (var i = 0; i < ringNodes.Count; i++)
            {
                var node = ringNodes[i];
                var angle = -MathF.PI / 2f + i * angleStep;
                var x = centerX + MathF.Cos(angle) * ringRadius - nodeSize / 2f;
                var y = centerY + MathF.Sin(angle) * ringRadius - nodeSize / 2f;
                var index = indexLookup.TryGetValue(node.Id, out var foundIndex) ? foundIndex : 0;
                layouts.Add(new SmartArtNodeLayout(
                    node.Id,
                    node.GetDisplayText(index),
                    new DocRect(x, y, nodeSize, nodeSize),
                    ringIndex,
                    index));
            }
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutPyramid(
        DiagramGraph graph,
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutOptions options)
    {
        var count = nodes.Count;
        if (count == 0)
        {
            return new List<SmartArtNodeLayout>();
        }

        var indexLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            indexLookup[nodes[i].Id] = i;
        }

        var levelMap = BuildLevels(graph);
        var grouped = new Dictionary<int, List<DiagramNode>>();
        foreach (var node in nodes)
        {
            var level = levelMap.TryGetValue(node.Id, out var value) ? value : 0;
            if (!grouped.TryGetValue(level, out var list))
            {
                list = new List<DiagramNode>();
                grouped[level] = list;
            }

            list.Add(node);
        }

        if (grouped.Count <= 1)
        {
            return LayoutPyramidByCount(nodes, margin, availableWidth, availableHeight, gap, options.InvertPyramid);
        }

        var orderedLevels = grouped.Keys.OrderBy(level => level).ToList();
        if (options.InvertPyramid)
        {
            orderedLevels.Reverse();
        }

        var levelCount = Math.Max(1, orderedLevels.Count);
        var levelHeight = (availableHeight - gap * MathF.Max(0, levelCount - 1)) / levelCount;
        var layouts = new List<SmartArtNodeLayout>(count);

        for (var levelIndex = 0; levelIndex < orderedLevels.Count; levelIndex++)
        {
            var level = orderedLevels[levelIndex];
            var items = grouped[level];
            var progress = options.InvertPyramid
                ? (levelCount - levelIndex) / (float)levelCount
                : (levelIndex + 1) / (float)levelCount;
            var widthFactor = MathF.Max(0.3f, progress);
            var rowWidth = availableWidth * MathF.Max(0.35f, widthFactor);
            var nodeWidth = (rowWidth - gap * MathF.Max(0, items.Count - 1)) / Math.Max(1, items.Count);
            var x = margin + (availableWidth - rowWidth) / 2f;
            var y = margin + levelIndex * (levelHeight + gap);
            for (var i = 0; i < items.Count; i++)
            {
                var nodeX = x + i * (nodeWidth + gap);
                var index = indexLookup.TryGetValue(items[i].Id, out var resolved) ? resolved : 0;
                layouts.Add(new SmartArtNodeLayout(
                    items[i].Id,
                    items[i].GetDisplayText(index),
                    new DocRect(nodeX, y, nodeWidth, levelHeight),
                    levelIndex,
                    index));
            }
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutPyramidByCount(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        bool invert)
    {
        var count = nodes.Count;
        if (count == 0)
        {
            return new List<SmartArtNodeLayout>();
        }

        var levelSizes = new List<int>();
        var remaining = count;
        var level = 0;
        while (remaining > 0)
        {
            level++;
            var take = Math.Min(level, remaining);
            levelSizes.Add(take);
            remaining -= take;
        }

        if (invert)
        {
            levelSizes.Reverse();
        }

        var levels = levelSizes.Count;
        var levelHeight = (availableHeight - gap * MathF.Max(0, levels - 1)) / levels;
        var layouts = new List<SmartArtNodeLayout>(count);
        var index = 0;

        for (var levelIndex = 0; levelIndex < levels; levelIndex++)
        {
            var items = levelSizes[levelIndex];
            var progress = invert
                ? (levels - levelIndex) / (float)levels
                : (levelIndex + 1) / (float)levels;
            var widthFactor = MathF.Max(0.3f, progress);
            var rowWidth = availableWidth * MathF.Max(0.35f, widthFactor);
            var nodeWidth = (rowWidth - gap * MathF.Max(0, items - 1)) / Math.Max(1, items);
            var x = margin + (availableWidth - rowWidth) / 2f;
            var y = margin + levelIndex * (levelHeight + gap);
            for (var i = 0; i < items; i++)
            {
                if (index >= count)
                {
                    break;
                }

                var nodeX = x + i * (nodeWidth + gap);
                layouts.Add(new SmartArtNodeLayout(
                    nodes[index].Id,
                    nodes[index].GetDisplayText(index),
                    new DocRect(nodeX, y, nodeWidth, levelHeight),
                    levelIndex,
                    index));
                index++;
            }
        }

        return layouts;
    }

    private static void ResolveMatrixGrid(int count, SmartArtLayoutOptions options, out int rows, out int columns)
    {
        rows = options.Rows ?? 0;
        columns = options.Columns ?? 0;

        if (rows > 0 && columns > 0)
        {
            if (rows * columns < count)
            {
                columns = (int)MathF.Ceiling(count / (float)rows);
            }

            return;
        }

        if (rows > 0)
        {
            columns = (int)MathF.Ceiling(count / (float)rows);
            return;
        }

        if (columns > 0)
        {
            rows = (int)MathF.Ceiling(count / (float)columns);
            return;
        }

        columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        rows = Math.Max(1, (int)MathF.Ceiling(count / (float)columns));
    }

    private static List<TreeLayoutNode> BuildHierarchyForest(
        DiagramGraph graph,
        IReadOnlyList<DiagramNode> nodes,
        out Dictionary<string, TreeLayoutNode> map)
    {
        map = new Dictionary<string, TreeLayoutNode>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            if (!map.ContainsKey(node.Id))
            {
                map[node.Id] = new TreeLayoutNode(node);
            }
        }

        var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            orderIndex[graph.Nodes[i].Id] = i;
        }

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in graph.Connections)
        {
            if (!map.ContainsKey(connection.FromId) || !map.ContainsKey(connection.ToId))
            {
                continue;
            }

            if (!adjacency.TryGetValue(connection.FromId, out var list))
            {
                list = new List<string>();
                adjacency[connection.FromId] = list;
            }

            list.Add(connection.ToId);
        }

        foreach (var pair in adjacency)
        {
            pair.Value.Sort((left, right) =>
            {
                var leftIndex = orderIndex.TryGetValue(left, out var l) ? l : int.MaxValue;
                var rightIndex = orderIndex.TryGetValue(right, out var r) ? r : int.MaxValue;
                return leftIndex.CompareTo(rightIndex);
            });
        }

        var roots = ResolveHierarchyRootIds(graph, nodes, orderIndex);
        var forest = new List<TreeLayoutNode>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < roots.Count; i++)
        {
            if (!map.TryGetValue(roots[i], out var root))
            {
                continue;
            }

            BuildHierarchyTree(root, 0, adjacency, map, visited);
            forest.Add(root);
        }

        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            if (visited.Contains(node.Id))
            {
                continue;
            }

            if (!map.TryGetValue(node.Id, out var root))
            {
                continue;
            }

            BuildHierarchyTree(root, 0, adjacency, map, visited);
            forest.Add(root);
        }

        return forest;
    }

    private static List<string> ResolveHierarchyRootIds(
        DiagramGraph graph,
        IReadOnlyList<DiagramNode> nodes,
        Dictionary<string, int> orderIndex)
    {
        var roots = new List<string>();
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            if (ContainsKeyword(node.Type, "root"))
            {
                roots.Add(node.Id);
            }
        }

        if (roots.Count == 0)
        {
            var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in graph.Nodes)
            {
                incoming[node.Id] = 0;
            }

            foreach (var connection in graph.Connections)
            {
                if (incoming.ContainsKey(connection.ToId))
                {
                    incoming[connection.ToId]++;
                }
            }

            foreach (var node in graph.Nodes)
            {
                if (incoming.TryGetValue(node.Id, out var count) && count == 0)
                {
                    roots.Add(node.Id);
                }
            }
        }

        if (roots.Count == 0 && nodes.Count > 0)
        {
            roots.Add(nodes[0].Id);
        }

        roots.Sort((left, right) =>
        {
            var leftIndex = orderIndex.TryGetValue(left, out var l) ? l : int.MaxValue;
            var rightIndex = orderIndex.TryGetValue(right, out var r) ? r : int.MaxValue;
            return leftIndex.CompareTo(rightIndex);
        });

        return roots;
    }

    private static void BuildHierarchyTree(
        TreeLayoutNode node,
        int depth,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, TreeLayoutNode> map,
        HashSet<string> visited)
    {
        if (!visited.Add(node.Node.Id))
        {
            return;
        }

        node.Depth = depth;
        if (!adjacency.TryGetValue(node.Node.Id, out var children))
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (!map.TryGetValue(children[i], out var child))
            {
                continue;
            }

            if (visited.Contains(child.Node.Id))
            {
                continue;
            }

            node.Children.Add(child);
            var nextDepth = node.Node.IsVirtual ? depth : depth + 1;
            BuildHierarchyTree(child, nextDepth, adjacency, map, visited);
        }
    }

    private static int AssignLeafCenters(TreeLayoutNode node, int leafIndex)
    {
        if (node.Children.Count == 0)
        {
            node.Center = leafIndex;
            return leafIndex + 1;
        }

        var start = leafIndex;
        for (var i = 0; i < node.Children.Count; i++)
        {
            leafIndex = AssignLeafCenters(node.Children[i], leafIndex);
        }

        node.Center = (start + leafIndex - 1) / 2f;
        return leafIndex;
    }

    private static DiagramNode ResolveRelationshipCenter(DiagramGraph graph, IReadOnlyList<DiagramNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (ContainsKeyword(node.Type, "root") || ContainsKeyword(node.Type, "center"))
            {
                return node;
            }
        }

        var degrees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            degrees[node.Id] = 0;
        }

        foreach (var connection in graph.Connections)
        {
            if (!degrees.ContainsKey(connection.FromId) || !degrees.ContainsKey(connection.ToId))
            {
                continue;
            }

            degrees[connection.FromId]++;
            degrees[connection.ToId]++;
        }

        var best = nodes[0];
        var bestScore = -1;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var score = degrees.TryGetValue(node.Id, out var value) ? value : 0;
            if (score > bestScore)
            {
                bestScore = score;
                best = node;
            }
        }

        return best;
    }

    private static Dictionary<string, int> ComputeRelationshipDistances(
        DiagramGraph graph,
        IReadOnlyList<DiagramNode> nodes,
        string centerId)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            allowed.Add(node.Id);
        }

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in graph.Connections)
        {
            if (!allowed.Contains(connection.FromId) || !allowed.Contains(connection.ToId))
            {
                continue;
            }

            if (!adjacency.TryGetValue(connection.FromId, out var list))
            {
                list = new List<string>();
                adjacency[connection.FromId] = list;
            }

            list.Add(connection.ToId);

            if (!adjacency.TryGetValue(connection.ToId, out var reverse))
            {
                reverse = new List<string>();
                adjacency[connection.ToId] = reverse;
            }

            reverse.Add(connection.FromId);
        }

        var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        if (!string.IsNullOrWhiteSpace(centerId))
        {
            distances[centerId] = 0;
            queue.Enqueue(centerId);
        }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var list))
            {
                continue;
            }

            var currentDistance = distances[current];
            for (var i = 0; i < list.Count; i++)
            {
                var next = list[i];
                if (distances.ContainsKey(next))
                {
                    continue;
                }

                distances[next] = currentDistance + 1;
                queue.Enqueue(next);
            }
        }

        foreach (var node in nodes)
        {
            if (!distances.ContainsKey(node.Id))
            {
                distances[node.Id] = 1;
            }
        }

        return distances;
    }

    private static float CenterX(SmartArtNodeLayout node)
    {
        return node.Bounds.X + node.Bounds.Width / 2f;
    }

    private static float CenterY(SmartArtNodeLayout node)
    {
        return node.Bounds.Y + node.Bounds.Height / 2f;
    }

    private static DocPoint AverageCenter(SmartArtNodeLayout[] nodes)
    {
        if (nodes.Length == 0)
        {
            return new DocPoint(0f, 0f);
        }

        var sumX = 0f;
        var sumY = 0f;
        for (var i = 0; i < nodes.Length; i++)
        {
            sumX += CenterX(nodes[i]);
            sumY += CenterY(nodes[i]);
        }

        return new DocPoint(sumX / nodes.Length, sumY / nodes.Length);
    }

    private static bool ContainsKeyword(string? value, string keyword)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        return value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryParsePositiveInt(string? value, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim();
        if (int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed <= 0)
            {
                return false;
            }

            result = parsed;
            return true;
        }

        if (float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            parsed = (int)MathF.Round(floatValue);
            if (parsed <= 0)
            {
                return false;
            }

            result = parsed;
            return true;
        }

        return false;
    }

    private static Dictionary<string, int> BuildLevels(DiagramGraph graph)
    {
        var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes)
        {
            incoming[node.Id] = 0;
        }

        foreach (var connection in graph.Connections)
        {
            if (incoming.ContainsKey(connection.ToId))
            {
                incoming[connection.ToId]++;
            }
        }

        var rootId = string.Empty;
        foreach (var node in graph.Nodes)
        {
            if (node.Type.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                rootId = node.Id;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(rootId))
        {
            foreach (var node in graph.Nodes)
            {
                if (incoming.TryGetValue(node.Id, out var count) && count == 0)
                {
                    rootId = node.Id;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(rootId))
        {
            rootId = graph.Nodes[0].Id;
        }

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in graph.Connections)
        {
            if (!adjacency.TryGetValue(connection.FromId, out var list))
            {
                list = new List<string>();
                adjacency[connection.FromId] = list;
            }

            list.Add(connection.ToId);
        }

        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [rootId] = 0
        };
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var level = levels[current];
            if (!adjacency.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (levels.ContainsKey(child))
                {
                    continue;
                }

                levels[child] = level + 1;
                queue.Enqueue(child);
            }
        }

        return levels;
    }

    private static SmartArtStyle? ParseSmartArtStyle(DiagramInfo diagram)
    {
        var palette = ParseSmartArtPalette(diagram.ColorStylePart);
        var style = new SmartArtStyle(palette);

        ApplySmartArtColorStyle(diagram.ColorStylePart, style);
        ApplySmartArtQuickStyle(diagram.QuickStylePart, style);

        if (style.NodeFillPalette.Count == 0
            && !style.NodeLineColor.HasValue
            && !style.TextColor.HasValue
            && !style.NodeLineWidth.HasValue
            && !style.ConnectorColor.HasValue
            && !style.ConnectorWidth.HasValue
            && !style.TextSize.HasValue)
        {
            return null;
        }

        return style;
    }

    private static IReadOnlyList<DocColor> ParseSmartArtPalette(byte[]? colorStylePart)
    {
        var colors = new List<DocColor>();
        if (colorStylePart is null || colorStylePart.Length == 0)
        {
            return colors;
        }

        using var stream = new MemoryStream(colorStylePart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        var role = SmartArtColorRole.None;
        var roleDepth = -1;
        var currentLabel = string.Empty;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName.Equals("styleLbl", StringComparison.OrdinalIgnoreCase)
                    && IsDiagramNamespace(reader.NamespaceURI))
                {
                    currentLabel = reader.GetAttribute("name") ?? string.Empty;
                }

                if (reader.LocalName.Equals("fillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Fill;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("linClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Line;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("txFillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Text;
                    roleDepth = reader.Depth;
                }

                if (TryReadColor(reader, out var color))
                {
                    if (role == SmartArtColorRole.Fill && !IsConnectorLabel(currentLabel))
                    {
                        colors.Add(color);
                    }
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && roleDepth == reader.Depth)
            {
                role = SmartArtColorRole.None;
                roleDepth = -1;
            }
        }

        if (colors.Count == 0)
        {
            colors.AddRange(GetDefaultSmartArtPalette());
        }

        return colors;
    }

    private static void ApplySmartArtColorStyle(byte[]? colorStylePart, SmartArtStyle style)
    {
        if (colorStylePart is null || colorStylePart.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(colorStylePart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        var role = SmartArtColorRole.None;
        var roleDepth = -1;
        var currentLabel = string.Empty;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName.Equals("styleLbl", StringComparison.OrdinalIgnoreCase)
                    && IsDiagramNamespace(reader.NamespaceURI))
                {
                    currentLabel = reader.GetAttribute("name") ?? string.Empty;
                }

                if (reader.LocalName.Equals("fillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Fill;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("linClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Line;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("txFillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Text;
                    roleDepth = reader.Depth;
                }

                if (!TryReadColor(reader, out var color))
                {
                    continue;
                }

                var isConnector = IsConnectorLabel(currentLabel);
                if (role == SmartArtColorRole.Line)
                {
                    if (isConnector)
                    {
                        style.ConnectorColor ??= color;
                    }
                    else
                    {
                        style.NodeLineColor ??= color;
                    }
                }
                else if (role == SmartArtColorRole.Text)
                {
                    style.TextColor ??= color;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && roleDepth == reader.Depth)
            {
                role = SmartArtColorRole.None;
                roleDepth = -1;
            }
        }
    }

    private static void ApplySmartArtQuickStyle(byte[]? quickStylePart, SmartArtStyle style)
    {
        if (quickStylePart is null || quickStylePart.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(quickStylePart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        var role = SmartArtColorRole.None;
        var roleDepth = -1;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                if (reader.NodeType == XmlNodeType.EndElement && roleDepth == reader.Depth)
                {
                    role = SmartArtColorRole.None;
                    roleDepth = -1;
                }

                continue;
            }

            if (reader.LocalName.Equals("ln", StringComparison.OrdinalIgnoreCase))
            {
                var widthText = reader.GetAttribute("w");
                if (style.NodeLineWidth is null && TryParseEmu(widthText, out var emu))
                {
                    style.NodeLineWidth = EmuToDip(emu);
                }
                role = SmartArtColorRole.Line;
                roleDepth = reader.Depth;
                continue;
            }

            if (reader.LocalName.Equals("defRPr", StringComparison.OrdinalIgnoreCase))
            {
                var sizeText = reader.GetAttribute("sz");
                if (style.TextSize is null && int.TryParse(sizeText, out var size))
                {
                    style.TextSize = SmartArtFontSizeToDip(size);
                }
                role = SmartArtColorRole.Text;
                roleDepth = reader.Depth;
                continue;
            }

            if (role != SmartArtColorRole.None && TryReadColor(reader, out var color))
            {
                if (role == SmartArtColorRole.Line)
                {
                    style.NodeLineColor ??= color;
                }
                else if (role == SmartArtColorRole.Text)
                {
                    style.TextColor ??= color;
                }
            }
        }
    }

    private static bool TryReadColor(XmlReader reader, out DocColor color)
    {
        color = default;
        if (!IsDrawingNamespace(reader.NamespaceURI))
        {
            return false;
        }

        if (reader.LocalName.Equals("srgbClr", StringComparison.OrdinalIgnoreCase))
        {
            var hex = reader.GetAttribute("val");
            return TryParseHexColor(hex, out color);
        }

        if (reader.LocalName.Equals("schemeClr", StringComparison.OrdinalIgnoreCase))
        {
            var scheme = reader.GetAttribute("val");
            if (TryParseSchemeColor(scheme, out var themeColor))
            {
                color = DocumentThemeColorMap.GetDefault(themeColor);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHexColor(string? value, out DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();
        if (span.Length != 6)
        {
            return false;
        }

        if (!TryParseHexByte(span.Slice(0, 2), out var r)
            || !TryParseHexByte(span.Slice(2, 2), out var g)
            || !TryParseHexByte(span.Slice(4, 2), out var b))
        {
            return false;
        }

        color = new DocColor(r, g, b);
        return true;
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> value, out byte result)
    {
        result = 0;
        if (value.Length != 2)
        {
            return false;
        }

        var upper = HexValue(value[0]);
        var lower = HexValue(value[1]);
        if (upper < 0 || lower < 0)
        {
            return false;
        }

        result = (byte)((upper << 4) | lower);
        return true;
    }

    private static int HexValue(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return value - '0';
        }

        if (value is >= 'A' and <= 'F')
        {
            return value - 'A' + 10;
        }

        if (value is >= 'a' and <= 'f')
        {
            return value - 'a' + 10;
        }

        return -1;
    }

    private static bool TryParseSchemeColor(string? value, out DocThemeColor themeColor)
    {
        themeColor = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "accent1" => (themeColor = DocThemeColor.Accent1) == DocThemeColor.Accent1,
            "accent2" => (themeColor = DocThemeColor.Accent2) == DocThemeColor.Accent2,
            "accent3" => (themeColor = DocThemeColor.Accent3) == DocThemeColor.Accent3,
            "accent4" => (themeColor = DocThemeColor.Accent4) == DocThemeColor.Accent4,
            "accent5" => (themeColor = DocThemeColor.Accent5) == DocThemeColor.Accent5,
            "accent6" => (themeColor = DocThemeColor.Accent6) == DocThemeColor.Accent6,
            "dk1" => (themeColor = DocThemeColor.Dark1) == DocThemeColor.Dark1,
            "lt1" => (themeColor = DocThemeColor.Light1) == DocThemeColor.Light1,
            "dk2" => (themeColor = DocThemeColor.Dark2) == DocThemeColor.Dark2,
            "lt2" => (themeColor = DocThemeColor.Light2) == DocThemeColor.Light2,
            "hlink" => (themeColor = DocThemeColor.Hyperlink) == DocThemeColor.Hyperlink,
            "folHlink" => (themeColor = DocThemeColor.FollowedHyperlink) == DocThemeColor.FollowedHyperlink,
            _ => false
        };
    }

    private static IReadOnlyList<DocColor> GetDefaultSmartArtPalette()
    {
        return new[]
        {
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent1),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent2),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent3),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent4),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent5),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent6)
        };
    }

    private static bool TryParseEmu(string? value, out long emu)
    {
        emu = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return long.TryParse(value, out emu);
    }

    private static float EmuToDip(long emu)
    {
        return emu / 914400f * 96f;
    }

    private static float SmartArtFontSizeToDip(int value)
    {
        var points = value / 100f;
        return points * 96f / 72f;
    }

    private static bool IsConnectorLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        return label.Contains("conn", StringComparison.OrdinalIgnoreCase)
               || label.Contains("line", StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureDirectionAttributes(XmlReader reader, SmartArtLayoutDefinition definition)
    {
        if (definition.Direction.HasValue || !reader.HasAttributes)
        {
            return;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            var name = reader.LocalName;
            if (name.Equals("dir", StringComparison.OrdinalIgnoreCase)
                || name.Equals("linDir", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orient", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseDirection(reader.Value, out var direction))
                {
                    definition.Direction = direction;
                    break;
                }
            }
        }

        reader.MoveToElement();
    }

    private static bool TryParseDirection(string? value, out SmartArtLayoutDirection direction)
    {
        direction = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var token = value.Trim().ToLowerInvariant();
        if (token.Contains("hor") || token.Contains("ltr") || token.Contains("rtl"))
        {
            direction = SmartArtLayoutDirection.Horizontal;
            return true;
        }

        if (token.Contains("ver") || token.Contains("t2b") || token.Contains("b2t"))
        {
            direction = SmartArtLayoutDirection.Vertical;
            return true;
        }

        return false;
    }

    private static bool IsDiagramNamespace(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.IndexOf(DiagramNamespaceFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsDrawingNamespace(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.IndexOf(DrawingNamespaceFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> data, ReadOnlySpan<char> keyword)
    {
        if (keyword.IsEmpty || data.IsEmpty)
        {
            return false;
        }

        var lower0 = (byte)char.ToLowerInvariant(keyword[0]);
        for (var i = 0; i < data.Length; i++)
        {
            if (ToLowerAscii(data[i]) != lower0)
            {
                continue;
            }

            if (MatchesAsciiIgnoreCase(data.Slice(i), keyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAsciiIgnoreCase(ReadOnlySpan<byte> data, ReadOnlySpan<char> keyword)
    {
        if (data.Length < keyword.Length)
        {
            return false;
        }

        for (var i = 0; i < keyword.Length; i++)
        {
            if (ToLowerAscii(data[i]) != (byte)char.ToLowerInvariant(keyword[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToLowerAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }

    private sealed record DiagramGraph(
        List<DiagramNode> Nodes,
        List<DiagramConnection> Connections);

    private readonly record struct DiagramConnection(string FromId, string ToId);

    private readonly record struct DiagramNode(string Id, string Type, string Text, bool IsVirtual)
    {
        public string GetDisplayText(int index)
        {
            return string.IsNullOrWhiteSpace(Text) ? $"Item {index + 1}" : Text;
        }
    }

    private sealed class TreeLayoutNode
    {
        public TreeLayoutNode(DiagramNode node)
        {
            Node = node;
        }

        public DiagramNode Node { get; }
        public List<TreeLayoutNode> Children { get; } = new();
        public int Depth { get; set; }
        public float Center { get; set; }
    }

    private sealed class SmartArtAlgorithm
    {
        public SmartArtAlgorithm(string type, Dictionary<string, string> parameters)
        {
            Type = type;
            Parameters = parameters;
        }

        public string Type { get; }
        public Dictionary<string, string> Parameters { get; }
    }

    private sealed class SmartArtLayoutDefinition
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public List<string> Categories { get; } = new();
        public SmartArtLayoutDirection? Direction { get; set; }
        public List<SmartArtAlgorithm> Algorithms { get; } = new();

        public bool Matches(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(Name)
                && Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Title)
                && Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var category in Categories)
            {
                if (category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool MatchesAny(params string[] keywords)
        {
            if (keywords is null || keywords.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < keywords.Length; i++)
            {
                if (Matches(keywords[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly record struct SmartArtLayoutOptions(
        SmartArtLayoutKind Kind,
        SmartArtLayoutDirection Direction,
        SmartArtLayoutFlow Flow,
        int? Rows,
        int? Columns,
        bool InvertPyramid);

    private enum SmartArtLayoutDirection
    {
        Horizontal,
        Vertical
    }

    private enum SmartArtLayoutFlow
    {
        None,
        Wrap,
        Snake
    }

    private enum SmartArtColorRole
    {
        None,
        Fill,
        Line,
        Text
    }
}
