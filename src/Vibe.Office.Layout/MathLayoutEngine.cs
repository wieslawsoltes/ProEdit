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
            MathFunction function => LayoutFunction(function, context, level),
            MathFraction fraction => LayoutFraction(fraction, context, level),
            MathAccent accent => LayoutAccent(accent, context, level),
            MathDelimiter delimiter => LayoutDelimiter(delimiter, context, level),
            MathBar bar => LayoutBar(bar, context, level),
            MathBoxElement box => LayoutBoxElement(box, context, level),
            MathBorderBox border => LayoutBorderBox(border, context, level),
            MathNary nary => LayoutNary(nary, context, level),
            MathMatrix matrix => LayoutMatrix(matrix, context, level),
            MathLimit limit => LayoutLimit(limit, context, level),
            MathScript script => LayoutScript(script, context, level),
            MathPreScript preScript => LayoutPreScript(preScript, context, level),
            MathRadical radical => LayoutRadical(radical, context, level),
            MathGroupCharacter group => LayoutGroupCharacter(group, context, level),
            MathPhantom phantom => LayoutPhantom(phantom, context, level),
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
        var bodyBox = LayoutDelimiterBody(delimiter, context, level);
        var beginChar = string.IsNullOrEmpty(delimiter.BeginChar) ? "(" : delimiter.BeginChar;
        var endChar = string.IsNullOrEmpty(delimiter.EndChar) ? ")" : delimiter.EndChar;
        var targetHeight = MathF.Max(bodyBox.Height, style.FontSize);
        var leftBox = LayoutStretchTextBox(new MathRun { Text = beginChar }, beginChar, style, context.Measurer, targetHeight);
        var rightBox = LayoutStretchTextBox(new MathRun { Text = endChar }, endChar, style, context.Measurer, targetHeight);
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

    private MathBox LayoutBar(MathBar bar, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(bar.Base, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BarGapScale);
        var thickness = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BarThicknessScale);

        var baseY = bar.Position == MathBarPosition.Top ? thickness + gap : 0f;
        var height = baseY + baseBox.Height;
        if (bar.Position == MathBarPosition.Bottom)
        {
            height += gap + thickness;
        }

        var baseline = baseY + baseBox.Baseline;
        var children = new List<MathBoxChild>
        {
            new MathBoxChild(baseBox, 0f, baseY)
        };

        return new MathBox(bar, baseBox.Width, height, baseline, null, style, children);
    }

    private MathBox LayoutBoxElement(MathBoxElement box, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(box.Base, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var padding = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BoxPaddingScale);
        var width = baseBox.Width + padding * 2f;
        var height = baseBox.Height + padding * 2f;
        var baseline = baseBox.Baseline + padding;
        var children = new List<MathBoxChild>
        {
            new MathBoxChild(baseBox, padding, padding)
        };

        return new MathBox(box, width, height, baseline, null, style, children);
    }

    private MathBox LayoutBorderBox(MathBorderBox box, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(box.Base, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var padding = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BoxPaddingScale);
        var border = MathF.Max(1f, style.FontSize * MathLayoutMetrics.BorderThicknessScale);
        var inset = padding + border;
        var width = baseBox.Width + inset * 2f;
        var height = baseBox.Height + inset * 2f;
        var baseline = baseBox.Baseline + inset;
        var children = new List<MathBoxChild>
        {
            new MathBoxChild(baseBox, inset, inset)
        };

        return new MathBox(box, width, height, baseline, null, style, children);
    }

    private MathBox LayoutFunction(MathFunction function, MathLayoutContext context, int level)
    {
        var nameBox = LayoutElement(function.Name, context, level);
        var argumentBox = LayoutElement(function.Argument, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var gap = MathF.Max(0f, style.FontSize * MathLayoutMetrics.FunctionGapScale);

        var baseline = MathF.Max(nameBox.Baseline, argumentBox.Baseline);
        var nameY = baseline - nameBox.Baseline;
        var argumentY = baseline - argumentBox.Baseline;
        var width = nameBox.Width + gap + argumentBox.Width;
        var height = MathF.Max(nameY + nameBox.Height, argumentY + argumentBox.Height);
        var children = new List<MathBoxChild>
        {
            new MathBoxChild(nameBox, 0f, nameY),
            new MathBoxChild(argumentBox, nameBox.Width + gap, argumentY)
        };

        return new MathBox(function, width, height, baseline, null, style, children);
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

    private MathBox LayoutLimit(MathLimit limit, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(limit.Base, context, level);
        var limitBox = LayoutElement(limit.Limit, context, level + 1);
        var style = ScaleStyle(context.BaseStyle, level);
        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.LimitGapScale);

        var width = MathF.Max(baseBox.Width, limitBox.Width);
        var baseX = (width - baseBox.Width) / 2f;
        var limitX = (width - limitBox.Width) / 2f;

        float baseY;
        float limitY;
        float baseline;
        float height;
        if (limit.Position == MathLimitPosition.Upper)
        {
            limitY = 0f;
            baseY = limitBox.Height + gap;
            height = baseY + baseBox.Height;
            baseline = baseY + baseBox.Baseline;
        }
        else
        {
            baseY = 0f;
            limitY = baseBox.Height + gap;
            height = limitY + limitBox.Height;
            baseline = baseBox.Baseline;
        }

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(baseBox, baseX, baseY),
            new MathBoxChild(limitBox, limitX, limitY)
        };

        return new MathBox(limit, width, height, baseline, null, style, children);
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

    private MathBox LayoutPreScript(MathPreScript script, MathLayoutContext context, int level)
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

        var children = new List<MathBoxChild>();
        var baseX = scriptWidth > 0f ? scriptWidth + gap : 0f;
        children.Add(new MathBoxChild(baseBox, baseX, 0f));

        if (supBox is not null)
        {
            var supY = MathF.Max(0f, baseline - supBox.Height - gap);
            children.Add(new MathBoxChild(supBox, 0f, supY));
            height = MathF.Max(height, supY + supBox.Height);
        }

        if (subBox is not null)
        {
            var subY = baseline + gap;
            children.Add(new MathBoxChild(subBox, 0f, subY));
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

    private MathBox LayoutGroupCharacter(MathGroupCharacter group, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(group.Base, context, level);
        var style = ScaleStyle(context.BaseStyle, level);
        var groupText = string.IsNullOrWhiteSpace(group.Character) ? "^" : group.Character;
        var groupStyle = style.Clone();
        var groupBox = LayoutTextBox(new MathRun { Text = groupText }, groupText, groupStyle, context.Measurer);

        if (groupBox.Width > 0f && baseBox.Width > groupBox.Width)
        {
            var scale = MathF.Min(MathLayoutMetrics.GroupCharMaxScale, baseBox.Width / groupBox.Width);
            if (scale > 1f)
            {
                groupStyle = style.Clone();
                groupStyle.HorizontalScale = MathF.Max(0.1f, groupStyle.HorizontalScale * scale);
                groupBox = LayoutTextBox(new MathRun { Text = groupText }, groupText, groupStyle, context.Measurer);
            }
        }

        var gap = MathF.Max(1f, style.FontSize * MathLayoutMetrics.GroupCharGapScale);
        var width = MathF.Max(baseBox.Width, groupBox.Width);
        var baseX = (width - baseBox.Width) / 2f;
        var groupX = (width - groupBox.Width) / 2f;

        float baseY;
        float groupY;
        float baseline;
        float height;
        if (group.Position == MathGroupCharacterPosition.Bottom)
        {
            baseY = 0f;
            groupY = baseBox.Height + gap;
            height = groupY + groupBox.Height;
            baseline = baseBox.Baseline;
        }
        else
        {
            groupY = 0f;
            baseY = groupBox.Height + gap;
            height = baseY + baseBox.Height;
            baseline = baseY + baseBox.Baseline;
        }

        var children = new List<MathBoxChild>
        {
            new MathBoxChild(groupBox, groupX, groupY),
            new MathBoxChild(baseBox, baseX, baseY)
        };

        return new MathBox(group, width, height, baseline, null, style, children);
    }

    private MathBox LayoutPhantom(MathPhantom phantom, MathLayoutContext context, int level)
    {
        var baseBox = LayoutElement(phantom.Base, context, level);
        var ascent = phantom.ZeroAscent ? 0f : baseBox.Baseline;
        var descent = phantom.ZeroDescent ? 0f : MathF.Max(0f, baseBox.Height - baseBox.Baseline);
        var width = phantom.ZeroWidth ? 0f : baseBox.Width;
        var height = ascent + descent;
        var baseline = ascent;
        var style = ScaleStyle(context.BaseStyle, level);

        var isHidden = !phantom.Show || phantom.Transparent;
        return new MathBox(phantom, width, height, baseline, null, style, null, isHidden);
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

    private MathBox LayoutStretchTextBox(
        MathElement element,
        string text,
        TextStyle style,
        ITextMeasurer measurer,
        float targetHeight)
    {
        if (text.Length == 0)
        {
            var height = MathF.Max(1f, style.FontSize);
            return new MathBox(element, 0f, height, height * 0.8f, string.Empty, style);
        }

        var metrics = measurer.MeasureText(text, style);
        var heightValue = MathF.Max(1f, metrics.Height);
        if (targetHeight <= heightValue)
        {
            var baseline = MathF.Max(1f, metrics.Ascent);
            return new MathBox(element, MathF.Max(0f, metrics.Width), heightValue, baseline, text, style);
        }

        var scale = MathF.Min(MathLayoutMetrics.DelimiterMaxScale, targetHeight / heightValue);
        if (scale <= 1f)
        {
            var baseline = MathF.Max(1f, metrics.Ascent);
            return new MathBox(element, MathF.Max(0f, metrics.Width), heightValue, baseline, text, style);
        }

        var stretchedStyle = style.Clone();
        stretchedStyle.FontSize = MathF.Max(1f, style.FontSize * scale);
        var stretched = measurer.MeasureText(text, stretchedStyle);
        var stretchedHeight = MathF.Max(1f, stretched.Height);
        var stretchedBaseline = MathF.Max(1f, stretched.Ascent);
        return new MathBox(
            element,
            MathF.Max(0f, stretched.Width),
            stretchedHeight,
            stretchedBaseline,
            text,
            stretchedStyle);
    }

    private MathBox LayoutFallback(MathElement element, MathLayoutContext context, int level)
    {
        var style = ScaleStyle(context.BaseStyle, level);
        var height = MathF.Max(1f, style.FontSize);
        return new MathBox(element, 0f, height, height * 0.8f, null, style);
    }

    private MathBox LayoutDelimiterBody(MathDelimiter delimiter, MathLayoutContext context, int level)
    {
        if (string.IsNullOrWhiteSpace(delimiter.SeparatorChar))
        {
            return LayoutElement(delimiter.Body, context, level);
        }

        if (delimiter.Body is not MathRow row || row.Elements.Count < 2)
        {
            return LayoutElement(delimiter.Body, context, level);
        }

        var synthetic = new MathRow();
        for (var i = 0; i < row.Elements.Count; i++)
        {
            synthetic.Elements.Add(row.Elements[i]);
            if (i < row.Elements.Count - 1)
            {
                synthetic.Elements.Add(new MathRun { Text = delimiter.SeparatorChar });
            }
        }

        return LayoutRow(synthetic, context, level);
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
