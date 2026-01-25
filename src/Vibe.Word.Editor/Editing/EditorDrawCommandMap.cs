using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorDrawCommandMap
{
    private const string InkStrokeProgId = "InkStroke";
    private const float InkClosedThreshold = 0.15f;
    private const float InkLineDeviationScale = 1.5f;
    private const float InkSelectionPadding = 10f;
    private const float MinPenThickness = 0.5f;
    private const float MaxPenThickness = 30f;

    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private readonly EditorServices _services;

    private enum InkShapeKind
    {
        Unknown,
        Line,
        Rectangle,
        Ellipse
    }

    private enum InkOrientation
    {
        Unknown,
        Horizontal,
        Vertical,
        Diagonal
    }

    private readonly record struct InkStrokeSelection(
        FloatingLayoutObject Layout,
        ParagraphBlock Paragraph,
        int Index,
        FloatingObject Floating,
        ImageInline Image,
        InkStrokeData Stroke,
        DocRect Bounds,
        float AngleDegrees,
        InkOrientation Orientation,
        InkShapeKind ShapeKind);

    public EditorDrawCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Register()
    {
        _router.RegisterAction(EditorDrawCommandIds.Tools.Select, (_, __) => SetTool(EditorDrawTool.Select), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.LassoSelect, (_, __) => SetTool(EditorDrawTool.LassoSelect), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Pen, (_, __) => SetTool(EditorDrawTool.Pen), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Pencil, (_, __) => SetTool(EditorDrawTool.Pencil), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Highlighter, (_, __) => SetTool(EditorDrawTool.Highlighter), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Eraser, (_, __) => SetTool(EditorDrawTool.Eraser), CanUseDrawTools, isUndoable: false);

        _router.RegisterAction(EditorDrawCommandIds.Convert.InkToShape, (_, __) => ConvertInkToShape(), CanConvertInk, isUndoable: true);
        _router.RegisterAction(EditorDrawCommandIds.Convert.InkToMath, (_, __) => ConvertInkToMath(), CanConvertInk, isUndoable: true);
        _router.RegisterAction(EditorDrawCommandIds.Convert.InkReplay, (_, __) => ReplayInk(), CanReplayInk, isUndoable: false);

        _router.RegisterAction(EditorDrawCommandIds.AddPen.Add, (_, __) => AddPenAsync(), CanUseDrawTools, isUndoable: false);
    }

    private bool CanUseDrawTools(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IDrawToolService>(out _);
    }

    private bool CanConvertInk(RibbonContextSnapshot? context, object? payload)
    {
        return HasInkStrokes();
    }

    private bool CanReplayInk(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IInkReplayService>(out _) && HasInkStrokes();
    }

    private void SetTool(EditorDrawTool tool)
    {
        if (_services.TryGet<IDrawToolService>(out var drawTool))
        {
            drawTool.ActiveTool = tool;
        }
    }

    private bool HasInkStrokes()
    {
        foreach (var floating in _session.Layout.FloatingObjects)
        {
            if (IsInkFloatingObject(floating.Object))
            {
                return true;
            }
        }

        return false;
    }

    private async void AddPenAsync()
    {
        if (!_services.TryGet<IDrawToolService>(out var drawTool))
        {
            return;
        }

        if (!_services.TryGet<IEditorDialogService>(out var dialog))
        {
            drawTool.ActiveTool = EditorDrawTool.Pen;
            return;
        }

        var colorText = FormatColor(drawTool.PenColor);
        var colorInput = await dialog.PromptAsync("Add Pen", "Pen color (#RRGGBB or rgb):", colorText);
        if (string.IsNullOrWhiteSpace(colorInput))
        {
            return;
        }

        if (!TryParseColor(colorInput, out var color))
        {
            await dialog.ShowMessageAsync("Add Pen", "Invalid color format.");
            return;
        }

        var thicknessInput = await dialog.PromptAsync("Add Pen", "Pen thickness:", drawTool.PenThickness.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(thicknessInput))
        {
            return;
        }

        if (!float.TryParse(thicknessInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness))
        {
            await dialog.ShowMessageAsync("Add Pen", "Invalid thickness value.");
            return;
        }

        drawTool.PenColor = color;
        drawTool.PenThickness = Math.Clamp(thickness, MinPenThickness, MaxPenThickness);
        drawTool.ActiveTool = EditorDrawTool.Pen;
    }

    private void ConvertInkToShape()
    {
        if (!TryGetInkStrokeSelection(out var stroke))
        {
            ShowMessage("Ink to Shape", "Select an ink stroke to convert.");
            return;
        }

        var kind = stroke.ShapeKind;
        if (kind == InkShapeKind.Unknown)
        {
            ShowMessage("Ink to Shape", "Unable to recognize a shape from the ink stroke.");
            return;
        }

        var shape = new ShapeInline(stroke.Image.Width, stroke.Image.Height);
        var properties = shape.Properties;
        properties.PresetGeometry = kind switch
        {
            InkShapeKind.Line => "line",
            InkShapeKind.Ellipse => "ellipse",
            _ => "rect"
        };

        if (kind == InkShapeKind.Line && MathF.Abs(stroke.AngleDegrees) > 0.5f)
        {
            properties.Rotation = stroke.AngleDegrees;
        }

        properties.Outline = new BorderLine
        {
            Color = stroke.Stroke.Color,
            Thickness = MathF.Max(0.5f, stroke.Stroke.Thickness),
            Style = DocBorderStyle.Single
        };

        var replacement = new FloatingObject(shape);
        CopyAnchor(stroke.Floating.Anchor, replacement.Anchor);
        stroke.Paragraph.FloatingObjects[stroke.Index] = replacement;
        _session.RefreshLayout();
    }

    private void ConvertInkToMath()
    {
        if (!TryGetInkStrokeSelection(out var selected))
        {
            ShowMessage("Ink to Math", "Select an ink stroke to convert.");
            return;
        }

        var strokes = CollectNearbyInkStrokes(selected);
        var symbol = ResolveMathSymbol(strokes);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            ShowMessage("Ink to Math", "Unable to recognize a math symbol from the ink stroke.");
            return;
        }

        RemoveInkStrokes(strokes);
        InsertMathSymbol(selected, symbol);
        _session.RefreshLayout();
    }

    private void ReplayInk()
    {
        if (_services.TryGet<IInkReplayService>(out var replayService))
        {
            _ = replayService.ReplaySelectedInkAsync();
        }
    }

    private void InsertMathSymbol(InkStrokeSelection selection, string symbol)
    {
        var paragraphIndex = selection.Layout.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= _session.Document.ParagraphCount)
        {
            return;
        }

        var paragraph = _session.Document.GetParagraph(paragraphIndex);
        var anchorOffset = selection.Floating.Anchor.AnchorOffset;
        var maxOffset = DocumentEditHelpers.GetParagraphLength(paragraph);
        var offset = anchorOffset.HasValue ? Math.Clamp(anchorOffset.Value, 0, maxOffset) : maxOffset;

        var caret = new TextPosition(paragraphIndex, offset);
        _session.SetSelection(new TextRange(caret, caret));

        var row = new MathRow();
        row.Elements.Add(new MathRun { Text = symbol });
        _session.InsertEquation(row);
    }

    private void RemoveInkStrokes(IReadOnlyList<InkStrokeSelection> strokes)
    {
        if (strokes.Count == 0)
        {
            return;
        }

        for (var i = 0; i < strokes.Count; i++)
        {
            var stroke = strokes[i];
            var list = stroke.Paragraph.FloatingObjects;
            if (stroke.Index >= 0 && stroke.Index < list.Count && list[stroke.Index].Id == stroke.Floating.Id)
            {
                list.RemoveAt(stroke.Index);
                continue;
            }

            var index = list.FindIndex(item => item.Id == stroke.Floating.Id);
            if (index >= 0)
            {
                list.RemoveAt(index);
            }
        }
    }

    private IReadOnlyList<InkStrokeSelection> CollectNearbyInkStrokes(InkStrokeSelection selected)
    {
        var strokes = new List<InkStrokeSelection> { selected };
        var targetBounds = Inflate(selected.Bounds, InkSelectionPadding);

        foreach (var layout in _session.Layout.FloatingObjects)
        {
            if (layout.Object.Id == selected.Floating.Id)
            {
                continue;
            }

            if (layout.ParagraphIndex != selected.Layout.ParagraphIndex)
            {
                continue;
            }

            if (!IsInkFloatingObject(layout.Object))
            {
                continue;
            }

            if (!Intersects(targetBounds, layout.Bounds))
            {
                continue;
            }

            if (!TryBuildInkStrokeSelection(layout, out var stroke))
            {
                continue;
            }

            strokes.Add(stroke);
            if (strokes.Count >= 3)
            {
                break;
            }
        }

        return strokes;
    }

    private static string? ResolveMathSymbol(IReadOnlyList<InkStrokeSelection> strokes)
    {
        if (strokes.Count == 0)
        {
            return null;
        }

        if (strokes.Count == 1)
        {
            var stroke = strokes[0];
            return stroke.ShapeKind switch
            {
                InkShapeKind.Ellipse => "○",
                InkShapeKind.Rectangle => "□",
                InkShapeKind.Line => stroke.Orientation switch
                {
                    InkOrientation.Horizontal => "−",
                    InkOrientation.Vertical => "∣",
                    InkOrientation.Diagonal => "/",
                    _ => null
                },
                _ => null
            };
        }

        var horizontalIndex = -1;
        var verticalIndex = -1;
        var diagonalFirst = -1;
        var diagonalSecond = -1;
        for (var i = 0; i < strokes.Count; i++)
        {
            switch (strokes[i].Orientation)
            {
                case InkOrientation.Horizontal:
                    if (horizontalIndex < 0)
                    {
                        horizontalIndex = i;
                    }
                    break;
                case InkOrientation.Vertical:
                    if (verticalIndex < 0)
                    {
                        verticalIndex = i;
                    }
                    break;
                case InkOrientation.Diagonal:
                    if (diagonalFirst < 0)
                    {
                        diagonalFirst = i;
                    }
                    else if (diagonalSecond < 0)
                    {
                        diagonalSecond = i;
                    }
                    break;
            }
        }

        if (horizontalIndex >= 0 && verticalIndex >= 0)
        {
            var horizontal = strokes[horizontalIndex];
            var vertical = strokes[verticalIndex];
            if (Intersects(horizontal.Bounds, vertical.Bounds))
            {
                return "＋";
            }
        }

        if (diagonalFirst >= 0 && diagonalSecond >= 0)
        {
            var first = strokes[diagonalFirst];
            var second = strokes[diagonalSecond];
            var angleDelta = MathF.Abs(first.AngleDegrees - second.AngleDegrees);
            if (angleDelta > 180f)
            {
                angleDelta = 360f - angleDelta;
            }

            if (angleDelta > 45f && angleDelta < 135f && Intersects(first.Bounds, second.Bounds))
            {
                return "×";
            }
        }

        return null;
    }

    private bool TryGetInkStrokeSelection(out InkStrokeSelection selection)
    {
        selection = default;
        if (_session.SelectedFloatingObjectId.HasValue
            && TryFindInkStrokeById(_session.SelectedFloatingObjectId.Value, out selection))
        {
            return true;
        }

        return TryGetLastInkStroke(out selection);
    }

    private bool TryFindInkStrokeById(Guid id, out InkStrokeSelection selection)
    {
        selection = default;
        foreach (var layout in _session.Layout.FloatingObjects)
        {
            if (layout.Object.Id != id)
            {
                continue;
            }

            if (!IsInkFloatingObject(layout.Object))
            {
                return false;
            }

            return TryBuildInkStrokeSelection(layout, out selection);
        }

        return false;
    }

    private bool TryGetLastInkStroke(out InkStrokeSelection selection)
    {
        selection = default;
        for (var i = _session.Layout.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var layout = _session.Layout.FloatingObjects[i];
            if (!IsInkFloatingObject(layout.Object))
            {
                continue;
            }

            if (TryBuildInkStrokeSelection(layout, out selection))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryBuildInkStrokeSelection(FloatingLayoutObject layout, out InkStrokeSelection selection)
    {
        selection = default;
        if (layout.Object.Content is not ImageInline image)
        {
            return false;
        }

        if (!InkStrokeParser.TryParse(image, out var stroke))
        {
            return false;
        }

        var paragraphIndex = layout.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= _session.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _session.Document.GetParagraph(paragraphIndex);
        var index = paragraph.FloatingObjects.FindIndex(item => item.Id == layout.Object.Id);
        if (index < 0)
        {
            return false;
        }

        var metrics = AnalyzeInkStroke(stroke);
        selection = new InkStrokeSelection(
            layout,
            paragraph,
            index,
            paragraph.FloatingObjects[index],
            image,
            stroke,
            layout.Bounds,
            metrics.AngleDegrees,
            metrics.Orientation,
            metrics.ShapeKind);
        return true;
    }

    private static InkStrokeMetrics AnalyzeInkStroke(InkStrokeData stroke)
    {
        var points = stroke.Points;
        if (points.Count < 2)
        {
            return InkStrokeMetrics.Empty;
        }

        var bounds = ComputeStrokeBounds(points);
        var start = points[0];
        var end = points[^1];
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var angle = MathF.Atan2(dy, dx) * (180f / MathF.PI);
        var length = MathF.Sqrt(dx * dx + dy * dy);
        var diagonal = MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
        var closed = length <= MathF.Max(1f, diagonal * InkClosedThreshold);
        var maxDeviation = ComputeMaxDeviation(points, start, end);
        var isLine = maxDeviation <= MathF.Max(1f, stroke.Thickness * InkLineDeviationScale);
        var shapeKind = InkShapeKind.Unknown;
        if (isLine)
        {
            shapeKind = InkShapeKind.Line;
        }
        else if (closed)
        {
            shapeKind = ResolveClosedShape(points, bounds);
        }

        var orientation = ResolveOrientation(angle);
        return new InkStrokeMetrics(bounds, angle, orientation, shapeKind);
    }

    private static InkShapeKind ResolveClosedShape(IReadOnlyList<DocPoint> points, DocRect bounds)
    {
        var width = MathF.Max(1f, bounds.Width);
        var height = MathF.Max(1f, bounds.Height);
        var pathLength = ComputePathLength(points);
        var rectPerimeter = 2f * (width + height);
        var ellipsePerimeter = EstimateEllipsePerimeter(width * 0.5f, height * 0.5f);
        var rectDelta = MathF.Abs(pathLength - rectPerimeter);
        var ellipseDelta = MathF.Abs(pathLength - ellipsePerimeter);
        return ellipseDelta <= rectDelta ? InkShapeKind.Ellipse : InkShapeKind.Rectangle;
    }

    private static float EstimateEllipsePerimeter(float radiusX, float radiusY)
    {
        var a = MathF.Max(0.1f, radiusX);
        var b = MathF.Max(0.1f, radiusY);
        var h = MathF.Pow((a - b), 2f) / MathF.Pow((a + b), 2f);
        return MathF.PI * (a + b) * (1f + (3f * h) / (10f + MathF.Sqrt(4f - 3f * h)));
    }

    private static float ComputePathLength(IReadOnlyList<DocPoint> points)
    {
        var length = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            length += MathF.Sqrt(dx * dx + dy * dy);
        }

        return length;
    }

    private static float ComputeMaxDeviation(IReadOnlyList<DocPoint> points, DocPoint start, DocPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSq = dx * dx + dy * dy;
        if (lengthSq <= 0.0001f)
        {
            return 0f;
        }

        var length = MathF.Sqrt(lengthSq);
        var max = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var distance = MathF.Abs(dy * point.X - dx * point.Y + end.X * start.Y - end.Y * start.X) / length;
            if (distance > max)
            {
                max = distance;
            }
        }

        return max;
    }

    private static DocRect ComputeStrokeBounds(IReadOnlyList<DocPoint> points)
    {
        var minX = points[0].X;
        var maxX = points[0].X;
        var minY = points[0].Y;
        var maxY = points[0].Y;
        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            if (point.X < minX)
            {
                minX = point.X;
            }
            else if (point.X > maxX)
            {
                maxX = point.X;
            }

            if (point.Y < minY)
            {
                minY = point.Y;
            }
            else if (point.Y > maxY)
            {
                maxY = point.Y;
            }
        }

        return new DocRect(minX, minY, MathF.Max(0f, maxX - minX), MathF.Max(0f, maxY - minY));
    }

    private static InkOrientation ResolveOrientation(float angleDegrees)
    {
        var angle = NormalizeAngle(angleDegrees);
        var abs = MathF.Abs(angle);
        if (abs <= 20f || abs >= 160f)
        {
            return InkOrientation.Horizontal;
        }

        if (abs >= 70f && abs <= 110f)
        {
            return InkOrientation.Vertical;
        }

        return InkOrientation.Diagonal;
    }

    private static float NormalizeAngle(float angleDegrees)
    {
        var angle = angleDegrees % 360f;
        if (angle > 180f)
        {
            angle -= 360f;
        }
        else if (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }

    private static bool IsInkFloatingObject(FloatingObject floating)
    {
        return floating.Content is ImageInline image
            && string.Equals(image.EmbeddedObject?.ProgId, InkStrokeProgId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Intersects(DocRect first, DocRect second)
    {
        return first.Left <= second.Right
            && first.Right >= second.Left
            && first.Top <= second.Bottom
            && first.Bottom >= second.Top;
    }

    private static DocRect Inflate(DocRect rect, float padding)
    {
        return new DocRect(rect.X - padding, rect.Y - padding, rect.Width + padding * 2f, rect.Height + padding * 2f);
    }

    private static void CopyAnchor(FloatingAnchor source, FloatingAnchor target)
    {
        target.HorizontalReference = source.HorizontalReference;
        target.VerticalReference = source.VerticalReference;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.OffsetX = source.OffsetX;
        target.OffsetY = source.OffsetY;
        target.WrapStyle = source.WrapStyle;
        target.WrapSide = source.WrapSide;
        target.WrapPolygon = source.WrapPolygon;
        target.BehindText = source.BehindText;
        target.Distance = source.Distance;
        target.AnchorOffset = source.AnchorOffset;
    }

    private static string FormatColor(DocColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static bool TryParseColor(string? text, out DocColor color)
    {
        color = DocColor.Black;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = trimmed.AsSpan(1);
            if (hex.Length == 6
                && byte.TryParse(hex.Slice(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && byte.TryParse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && byte.TryParse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                color = new DocColor(r, g, b);
                return true;
            }

            return false;
        }

        if (trimmed.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var span = trimmed.AsSpan(4, trimmed.Length - 5);
            var first = span.IndexOf(',');
            if (first <= 0)
            {
                return false;
            }

            var second = span.Slice(first + 1).IndexOf(',');
            if (second <= 0)
            {
                return false;
            }

            if (!byte.TryParse(span[..first], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                return false;
            }

            var secondStart = first + 1;
            if (!byte.TryParse(span.Slice(secondStart, second), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g))
            {
                return false;
            }

            var bSpan = span.Slice(secondStart + second + 1);
            if (!byte.TryParse(bSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return false;
            }

            color = new DocColor(r, g, b);
            return true;
        }

        return false;
    }

    private void ShowMessage(string title, string message)
    {
        if (_services.TryGet<IEditorDialogService>(out var dialog))
        {
            _ = dialog.ShowMessageAsync(title, message);
        }
    }

    private readonly record struct InkStrokeMetrics(DocRect Bounds, float AngleDegrees, InkOrientation Orientation, InkShapeKind ShapeKind)
    {
        public static InkStrokeMetrics Empty => new InkStrokeMetrics(new DocRect(0f, 0f, 0f, 0f), 0f, InkOrientation.Unknown, InkShapeKind.Unknown);
    }
}
