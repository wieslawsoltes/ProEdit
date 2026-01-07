using System.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed class MathLayoutEngine
{
    public MathLayout Layout(MathElement root, TextStyle baseStyle, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(baseStyle);
        ArgumentNullException.ThrowIfNull(measurer);

        var context = new MathLayoutContext(baseStyle, measurer);
        var box = LayoutElement(root, context, 0);
        return new MathLayout(box);
    }

    private MathBox LayoutElement(MathElement element, MathLayoutContext context, int level)
    {
        return element switch
        {
            MathRun run => LayoutRun(run, context, level),
            MathRow row => LayoutRow(row, context, level),
            MathFraction fraction => LayoutFraction(fraction, context, level),
            MathAccent accent => LayoutAccent(accent, context, level),
            MathDelimiter delimiter => LayoutDelimiter(delimiter, context, level),
            MathNary nary => LayoutNary(nary, context, level),
            MathMatrix matrix => LayoutMatrix(matrix, context, level),
            MathScript script => LayoutScript(script, context, level),
            MathRadical radical => LayoutRadical(radical, context, level),
            _ => LayoutFallback(element, context, level)
        };
    }

    private MathBox LayoutRun(MathRun run, MathLayoutContext context, int level)
    {
        var style = ResolveRunStyle(run, context.BaseStyle, level);
        var text = run.Text ?? string.Empty;
        if (text.Length == 0)
        {
            var runHeight = style.FontSize;
            return new MathBox(run, 0f, runHeight, runHeight * 0.8f, string.Empty, style);
        }

        var metrics = context.Measurer.MeasureText(text, style);
        var width = MathF.Max(0f, metrics.Width);
        var height = MathF.Max(1f, metrics.Height);
        var baseline = MathF.Max(1f, metrics.Ascent);
        return new MathBox(run, width, height, baseline, text, style);
    }

    private MathBox LayoutRow(MathRow row, MathLayoutContext context, int level)
    {
        if (row.Elements.Count == 0)
        {
            var style = ScaleStyle(context.BaseStyle, level);
            var rowHeight = MathF.Max(1f, style.FontSize);
            return new MathBox(row, 0f, rowHeight, rowHeight * 0.8f, null, style);
        }

        var children = new List<MathBoxChild>(row.Elements.Count);
        var baseline = 0f;
        foreach (var element in row.Elements)
        {
            var child = LayoutElement(element, context, level);
            baseline = MathF.Max(baseline, child.Baseline);
            children.Add(new MathBoxChild(child, 0f, 0f));
        }

        var gap = MathF.Max(0f, context.BaseStyle.FontSize * MathLayoutMetrics.RowGapScale);
        var x = 0f;
        var height = 0f;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i].Box;
            var y = baseline - child.Baseline;
            children[i] = new MathBoxChild(child, x, y);
            x += child.Width;
            if (i < children.Count - 1)
            {
                x += gap;
            }

            height = MathF.Max(height, y + child.Height);
        }

        return new MathBox(row, x, height, baseline, null, null, children);
    }

    private MathBox LayoutFraction(MathFraction fraction, MathLayoutContext context, int level)
    {
        var numerator = LayoutElement(fraction.Numerator, context, level + 1);
        var denominator = LayoutElement(fraction.Denominator, context, level + 1);
        var style = ScaleStyle(context.BaseStyle, level);
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.FractionGapScale);
        var padding = MathF.Max(1f, style.FontSize * MathLayoutMetrics.FractionPaddingScale);
        var barThickness = MathF.Max(1f, style.FontSize * MathLayoutMetrics.FractionBarScale);

        var width = MathF.Max(numerator.Width, denominator.Width) + padding * 2f;
        var numeratorX = (width - numerator.Width) / 2f;
        var denominatorX = (width - denominator.Width) / 2f;

        var numeratorY = 0f;
        var barY = numeratorY + numerator.Height + gap;
        var denominatorY = barY + (fraction.HasBar ? barThickness : 0f) + gap;

        var height = denominatorY + denominator.Height;
        var baseline = denominatorY + denominator.Baseline;

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(numerator, numeratorX, numeratorY),
            new MathBoxChild(denominator, denominatorX, denominatorY)
        };

        return new MathBox(fraction, width, height, baseline, null, style, children);
    }

    private MathBox LayoutAccent(MathAccent accent, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(accent.Base, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var accentChar = string.IsNullOrWhiteSpace(accent.AccentChar) ? "^" : accent.AccentChar;
        var accentStyle = ScaleStyle(style, level + 1);
        var accentBox = LayoutTextBox(new MathRun { Text = accentChar }, accentChar, accentStyle, context.Measurer);
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.AccentGapScale);

        var width = MathF.Max(baseBox.Width, accentBox.Width);
        var accentX = (width - accentBox.Width) / 2f;
        var baseX = (width - baseBox.Width) / 2f;
        var baseY = accentBox.Height + gap;
        var height = baseY + baseBox.Height;
        var baseline = baseY + baseBox.Baseline;

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(accentBox, accentX, 0f),
            new MathBoxChild(baseBox, baseX, baseY)
        };

        return new MathBox(accent, width, height, baseline, null, style, children);
    }

    private MathBox LayoutDelimiter(MathDelimiter delimiter, MathLayoutContext context, int level)
    {
        var style = ScaleStyle(context.BaseStyle, level);
        var bodyBox = LayoutElement(delimiter.Body, context, level);
        var beginChar = string.IsNullOrEmpty(delimiter.BeginChar) ? "(" : delimiter.BeginChar;
        var endChar = string.IsNullOrEmpty(delimiter.EndChar) ? ")" : delimiter.EndChar;
        var leftBox = LayoutTextBox(new MathRun { Text = beginChar }, beginChar, style, context.Measurer);
        var rightBox = LayoutTextBox(new MathRun { Text = endChar }, endChar, style, context.Measurer);
        var gap = MathF.Max(0f, style.FontSize * MathLayoutMetrics.DelimiterGapScale);

        var baseline = MathF.Max(bodyBox.Baseline, MathF.Max(leftBox.Baseline, rightBox.Baseline));
        var leftY = baseline - leftBox.Baseline;
        var bodyY = baseline - bodyBox.Baseline;
        var rightY = baseline - rightBox.Baseline;

        var width = leftBox.Width + gap + bodyBox.Width + gap + rightBox.Width;
        var height = MathF.Max(leftY + leftBox.Height, MathF.Max(bodyY + bodyBox.Height, rightY + rightBox.Height));

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(leftBox, 0f, leftY),
            new MathBoxChild(bodyBox, leftBox.Width + gap, bodyY),
            new MathBoxChild(rightBox, leftBox.Width + gap + bodyBox.Width + gap, rightY)
        };

        return new MathBox(delimiter, width, height, baseline, null, style, children);
    }

    private MathBox LayoutNary(MathNary nary, MathLayoutContext context, int level)
    {
        var style = ScaleStyle(context.BaseStyle, level);
        var operatorStyle = style.Clone();
        operatorStyle.FontSize = MathF.Max(1f, operatorStyle.FontSize * MathLayoutMetrics.NaryOperatorScale);
        var operatorChar = string.IsNullOrWhiteSpace(nary.OperatorChar) ? "SUM" : nary.OperatorChar;
        var operatorBox = LayoutTextBox(new MathRun { Text = operatorChar }, operatorChar, operatorStyle, context.Measurer);

        var subBox = !nary.HideSub && nary.Subscript is not null
            ? LayoutElement(nary.Subscript, context, level + 1)
            : null;
        var supBox = !nary.HideSup && nary.Superscript is not null
            ? LayoutElement(nary.Superscript, context, level + 1)
            : null;

        var limitGap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.NaryLimitGapScale);

        var limitsWidth = MathF.Max(operatorBox.Width, MathF.Max(subBox?.Width ?? 0f, supBox?.Width ?? 0f));
        var y = 0f;
        var children = new List<MathBoxChild>();

        if (supBox is not null)
        {
            var supX = (limitsWidth - supBox.Width) / 2f;
            children.Add(new MathBoxChild(supBox, supX, 0f));
            y = supBox.Height + limitGap;
        }

        var opX = (limitsWidth - operatorBox.Width) / 2f;
        var opY = y;
        children.Add(new MathBoxChild(operatorBox, opX, opY));
        y = opY + operatorBox.Height;

        if (subBox is not null)
        {
            var subX = (limitsWidth - subBox.Width) / 2f;
            var subY = y + limitGap;
            children.Add(new MathBoxChild(subBox, subX, subY));
            y = subY + subBox.Height;
        }

        var limitsHeight = MathF.Max(y, operatorBox.Height);
        var limitsBaseline = opY + operatorBox.Baseline;

        var baseBox = LayoutElement(nary.Base, context, level);
        var bodyGap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.NaryBodyGapScale);
        var width = limitsWidth + bodyGap + baseBox.Width;
        var baseline = MathF.Max(limitsBaseline, baseBox.Baseline);
        var limitsY = baseline - limitsBaseline;
        var baseY = baseline - baseBox.Baseline;

        var height = MathF.Max(limitsY + limitsHeight, baseY + baseBox.Height);
        var rootChildren = new List<MathBoxChild>(children.Count + 1);
        foreach (var child in children)
        {
            rootChildren.Add(new MathBoxChild(child.Box, child.X, child.Y + limitsY));
        }

        rootChildren.Add(new MathBoxChild(baseBox, limitsWidth + bodyGap, baseY));

        return new MathBox(nary, width, height, baseline, null, style, rootChildren);
    }

    private MathBox LayoutMatrix(MathMatrix matrix, MathLayoutContext context, int level)
    {
        var style = ScaleStyle(context.BaseStyle, level);
        var rowGap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.MatrixRowGapScale);
        var columnGap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.MatrixColumnGapScale);

        var rows = matrix.Rows;
        if (rows.Count == 0)
        {
            var emptyHeight = MathF.Max(1f, style.FontSize);
            return new MathBox(matrix, 0f, emptyHeight, emptyHeight * 0.8f, null, style);
        }

        var columnCount = rows.Max(row => row.Count);
        if (columnCount == 0)
        {
            var emptyHeight = MathF.Max(1f, style.FontSize);
            return new MathBox(matrix, 0f, emptyHeight, emptyHeight * 0.8f, null, style);
        }

        var cellBoxes = new MathBox[rows.Count, columnCount];
        var columnWidths = new float[columnCount];
        var rowHeights = new float[rows.Count];
        var rowBaselines = new float[rows.Count];

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < columnCount; c++)
            {
                var cellElement = c < row.Count ? row[c] : new MathRun();
                var cellBox = LayoutElement(cellElement, context, level);
                cellBoxes[r, c] = cellBox;
                columnWidths[c] = MathF.Max(columnWidths[c], cellBox.Width);
                rowBaselines[r] = MathF.Max(rowBaselines[r], cellBox.Baseline);
            }
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var maxHeight = 0f;
            for (var c = 0; c < columnCount; c++)
            {
                var cell = cellBoxes[r, c];
                var cellTop = rowBaselines[r] - cell.Baseline;
                maxHeight = MathF.Max(maxHeight, cellTop + cell.Height);
            }

            rowHeights[r] = maxHeight;
        }

        var totalWidth = columnWidths.Sum() + columnGap * MathF.Max(0, columnCount - 1);
        var totalHeight = rowHeights.Sum() + rowGap * MathF.Max(0, rows.Count - 1);

        var children = new List<MathBoxChild>();
        var y = 0f;
        for (var r = 0; r < rows.Count; r++)
        {
            var x = 0f;
            for (var c = 0; c < columnCount; c++)
            {
                var cell = cellBoxes[r, c];
                var cellY = y + (rowBaselines[r] - cell.Baseline);
                children.Add(new MathBoxChild(cell, x, cellY));
                x += columnWidths[c] + columnGap;
            }

            y += rowHeights[r] + rowGap;
        }

        var baseline = rows.Count > 0 ? rowBaselines[0] : totalHeight * 0.5f;
        return new MathBox(matrix, totalWidth, totalHeight, baseline, null, style, children);
    }

    private MathBox LayoutScript(MathScript script, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(script.Base, context, level);
        var supBox = script.Superscript is not null ? LayoutElement(script.Superscript, context, level + 1) : null;
        var subBox = script.Subscript is not null ? LayoutElement(script.Subscript, context, level + 1) : null;
        var style = ScaleStyle(context.BaseStyle, level);
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.ScriptGapScale);

        var scriptWidth = MathF.Max(supBox?.Width ?? 0f, subBox?.Width ?? 0f);
        var width = baseBox.Width + (scriptWidth > 0f ? gap + scriptWidth : 0f);

        var baseline = baseBox.Baseline;
        var height = baseBox.Height;

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(baseBox, 0f, 0f)
        };

        var scriptX = baseBox.Width + gap;
        if (supBox is not null)
        {
            var supY = MathF.Max(0f, baseline - supBox.Height - gap);
            children.Add(new MathBoxChild(supBox, scriptX, supY));
            height = MathF.Max(height, supY + supBox.Height);
        }

        if (subBox is not null)
        {
            var subY = baseline + gap;
            children.Add(new MathBoxChild(subBox, scriptX, subY));
            height = MathF.Max(height, subY + subBox.Height);
        }

        return new MathBox(script, width, height, baseline, null, style, children);
    }

    private MathBox LayoutRadical(MathRadical radical, MathLayoutContext context, int level)
    {
        var radicand = LayoutElement(radical.Radicand, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.RadicalGapScale);
        var symbolWidth = MathF.Max(1f, style.FontSize * MathLayoutMetrics.RadicalWidthScale);

        var width = symbolWidth + gap + radicand.Width;
        var height = radicand.Height + gap;
        var baseline = radicand.Baseline + gap * 0.5f;

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(radicand, symbolWidth + gap, gap)
        };

        if (radical.Degree is not null)
        {
            var degree = LayoutElement(radical.Degree, context, level + 1);
            children.Add(new MathBoxChild(degree, 0f, 0f));
        }

        return new MathBox(radical, width, height, baseline, null, style, children);
    }

    private MathBox LayoutTextBox(MathElement element, string text, TextStyle style, ITextMeasurer measurer)
    {
        if (text.Length == 0)
        {
            var height = MathF.Max(1f, style.FontSize);
            return new MathBox(element, 0f, height, height * 0.8f, string.Empty, style);
        }

        var metrics = measurer.MeasureText(text, style);
        var width = MathF.Max(0f, metrics.Width);
        var heightValue = MathF.Max(1f, metrics.Height);
        var baseline = MathF.Max(1f, metrics.Ascent);
        return new MathBox(element, width, heightValue, baseline, text, style);
    }

    private MathBox LayoutFallback(MathElement element, MathLayoutContext context, int level)
    {
        var style = ScaleStyle(context.BaseStyle, level);
        var height = MathF.Max(1f, style.FontSize);
        return new MathBox(element, 0f, height, height * 0.8f, null, style);
    }

    private static TextStyle ResolveRunStyle(MathRun run, TextStyle baseStyle, int level)
    {
        var style = baseStyle.Clone();
        if (run.Style is not null)
        {
            run.Style.ApplyTo(style);
        }
        style.FontSize = MathF.Max(1f, style.FontSize * MathF.Pow(MathLayoutMetrics.ScriptScale, level));
        return style;
    }

    private static TextStyle ScaleStyle(TextStyle baseStyle, int level)
    {
        var style = baseStyle.Clone();
        style.FontSize = MathF.Max(1f, style.FontSize * MathF.Pow(MathLayoutMetrics.ScriptScale, level));
        return style;
    }

    private sealed class MathLayoutContext
    {
        public TextStyle BaseStyle { get; }
        public ITextMeasurer Measurer { get; }

        public MathLayoutContext(TextStyle baseStyle, ITextMeasurer measurer)
        {
            BaseStyle = baseStyle;
            Measurer = measurer;
        }
    }
}
