using ReactiveUI;
using ProEdit.Reporting;

namespace ProEdit.Reporting.Avalonia.Designer;

public sealed partial class ReportDesignerViewModel
{
    private const float DesignerSnapStep = 4f;
    private const float DesignerMinItemWidth = 36f;
    private const float DesignerMinItemHeight = 24f;

    internal void SelectSurfaceItemFromInteraction(ReportDesignerCanvasItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        SelectTarget(item.Item);
    }

    internal bool TryMoveSurfaceItemByDelta(ReportDesignerCanvasItemViewModel canvasItem, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(canvasItem);
        if (canvasItem.IsReadOnly || canvasItem.Item is not { } item)
        {
            return false;
        }

        if (Math.Abs(deltaX) < double.Epsilon && Math.Abs(deltaY) < double.Epsilon)
        {
            return false;
        }

        EnsurePreviewMarkedDirtyForSurfaceEdit($"Moving {canvasItem.Label}.");

        var bounds = item.Bounds;
        var newX = bounds.X + (float)deltaX;
        var newY = bounds.Y + (float)deltaY;
        var (minX, minY, maxX, maxY) = GetMovementConstraints(item);
        var guides = new List<ReportDesignerSnapGuideViewModel>(capacity: 2);
        newX = SnapHorizontalMove(item, newX, bounds.Width, guides);
        newY = SnapVerticalMove(item, newY, bounds.Height, guides);
        newX = SnapAndClamp(newX, minX, maxX);
        newY = SnapAndClamp(newY, minY, maxY);
        ApplySnapGuides(guides);

        var appliedDeltaX = newX - bounds.X;
        var appliedDeltaY = newY - bounds.Y;
        if (Math.Abs(appliedDeltaX) < float.Epsilon && Math.Abs(appliedDeltaY) < float.Epsilon)
        {
            ClearSnapGuides();
            return false;
        }

        item.Bounds = bounds with
        {
            X = newX,
            Y = newY
        };

        if (item is LineItem lineItem)
        {
            lineItem.X2 += appliedDeltaX;
            lineItem.Y2 += appliedDeltaY;
        }

        canvasItem.Left += appliedDeltaX;
        canvasItem.Top += appliedDeltaY;

        if (item is ContainerItem containerItem)
        {
            TranslateCanvasDescendants(containerItem, appliedDeltaX, appliedDeltaY);
        }

        return true;
    }

    internal bool TryResizeSurfaceItemByDelta(
        ReportDesignerCanvasItemViewModel canvasItem,
        ReportDesignerSurfaceResizeHandle handle,
        double deltaX,
        double deltaY)
    {
        ArgumentNullException.ThrowIfNull(canvasItem);
        if (canvasItem.IsReadOnly || canvasItem.Item is not { } item || handle is ReportDesignerSurfaceResizeHandle.None or ReportDesignerSurfaceResizeHandle.Move)
        {
            return false;
        }

        EnsurePreviewMarkedDirtyForSurfaceEdit($"Resizing {canvasItem.Label}.");

        if (item is LineItem lineItem)
        {
            return TryResizeLineItemByDelta(canvasItem, lineItem, handle, deltaX, deltaY);
        }

        var bounds = item.Bounds;
        var newX = bounds.X;
        var newY = bounds.Y;
        var newWidth = bounds.Width;
        var newHeight = bounds.Height;

        if (handle is ReportDesignerSurfaceResizeHandle.West or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.SouthWest)
        {
            newX += (float)deltaX;
            newWidth -= (float)deltaX;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.East or ReportDesignerSurfaceResizeHandle.NorthEast or ReportDesignerSurfaceResizeHandle.SouthEast)
        {
            newWidth += (float)deltaX;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.North or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.NorthEast)
        {
            newY += (float)deltaY;
            newHeight -= (float)deltaY;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.South or ReportDesignerSurfaceResizeHandle.SouthWest or ReportDesignerSurfaceResizeHandle.SouthEast)
        {
            newHeight += (float)deltaY;
        }

        var (minX, minY, maxX, maxY) = GetMovementConstraints(item);
        var guides = new List<ReportDesignerSnapGuideViewModel>(capacity: 2);
        if (newWidth < DesignerMinItemWidth)
        {
            if (handle is ReportDesignerSurfaceResizeHandle.West or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.SouthWest)
            {
                newX -= DesignerMinItemWidth - newWidth;
            }

            newWidth = DesignerMinItemWidth;
        }

        if (newHeight < DesignerMinItemHeight)
        {
            if (handle is ReportDesignerSurfaceResizeHandle.North or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.NorthEast)
            {
                newY -= DesignerMinItemHeight - newHeight;
            }

            newHeight = DesignerMinItemHeight;
        }

        var right = newX + newWidth;
        var bottom = newY + newHeight;

        if (handle is ReportDesignerSurfaceResizeHandle.West or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.SouthWest)
        {
            newX = SnapHorizontalEdge(item, newX, isLeftEdge: true, guides);
            newWidth = right - newX;
        }
        else if (handle is ReportDesignerSurfaceResizeHandle.East or ReportDesignerSurfaceResizeHandle.NorthEast or ReportDesignerSurfaceResizeHandle.SouthEast)
        {
            right = SnapHorizontalEdge(item, right, isLeftEdge: false, guides);
            newWidth = right - newX;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.North or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.NorthEast)
        {
            newY = SnapVerticalEdge(item, newY, isTopEdge: true, guides);
            newHeight = bottom - newY;
        }
        else if (handle is ReportDesignerSurfaceResizeHandle.South or ReportDesignerSurfaceResizeHandle.SouthWest or ReportDesignerSurfaceResizeHandle.SouthEast)
        {
            bottom = SnapVerticalEdge(item, bottom, isTopEdge: false, guides);
            newHeight = bottom - newY;
        }

        newX = SnapAndClamp(newX, minX, maxX);
        newY = SnapAndClamp(newY, minY, maxY);
        newWidth = SnapAndClamp(newWidth, DesignerMinItemWidth, GetMaxWidth(item, newX));
        newHeight = SnapAndClamp(newHeight, DesignerMinItemHeight, GetMaxHeight(item, newY));
        ApplySnapGuides(guides);

        if (Math.Abs(newX - bounds.X) < float.Epsilon
            && Math.Abs(newY - bounds.Y) < float.Epsilon
            && Math.Abs(newWidth - bounds.Width) < float.Epsilon
            && Math.Abs(newHeight - bounds.Height) < float.Epsilon)
        {
            ClearSnapGuides();
            return false;
        }

        item.Bounds = bounds with
        {
            X = newX,
            Y = newY,
            Width = newWidth,
            Height = newHeight
        };

        if (item is TablixItem tablixItem)
        {
            ResizeTablixStructure(tablixItem, bounds.Width, bounds.Height, newWidth, newHeight);
        }

        canvasItem.Left += newX - bounds.X;
        canvasItem.Top += newY - bounds.Y;
        canvasItem.Width = newWidth;
        canvasItem.Height = newHeight;

        if (item is ContainerItem containerItem)
        {
            TranslateCanvasDescendants(containerItem, newX - bounds.X, newY - bounds.Y);
        }

        return true;
    }

    private static void ResizeTablixStructure(TablixItem tablixItem, float oldWidth, float oldHeight, float newWidth, float newHeight)
    {
        ResizeTablixColumns(tablixItem, oldWidth, newWidth);
        ResizeTablixRows(tablixItem, oldHeight, newHeight);
    }

    private static void ResizeTablixColumns(TablixItem tablixItem, float oldWidth, float newWidth)
    {
        var columnCount = tablixItem.Columns.Count;
        if (columnCount == 0)
        {
            return;
        }

        var sourceWidths = new float[columnCount];
        var fallbackWidth = columnCount == 0 ? newWidth : Math.Max(1f, oldWidth / columnCount);
        var sourceTotal = 0f;
        for (var index = 0; index < columnCount; index++)
        {
            var width = tablixItem.Columns[index].Width > 0f
                ? tablixItem.Columns[index].Width
                : fallbackWidth;
            sourceWidths[index] = width;
            sourceTotal += width;
        }

        if (sourceTotal <= 0f)
        {
            sourceTotal = columnCount;
        }

        var remainingSource = sourceTotal;
        var remainingTarget = Math.Max(1f, newWidth);
        for (var index = 0; index < columnCount; index++)
        {
            var remainingColumns = columnCount - index - 1;
            var width = index == columnCount - 1
                ? remainingTarget
                : Math.Max(1f, remainingTarget * (sourceWidths[index] / remainingSource));
            var maxWidth = Math.Max(1f, remainingTarget - remainingColumns);
            width = Math.Clamp(width, 1f, maxWidth);
            tablixItem.Columns[index].Width = width;
            remainingTarget -= width;
            remainingSource -= sourceWidths[index];
        }
    }

    private static void ResizeTablixRows(TablixItem tablixItem, float oldHeight, float newHeight)
    {
        var rowCount = tablixItem.Rows.Count;
        if (rowCount == 0)
        {
            return;
        }

        var sourceHeights = new float[rowCount];
        var fallbackHeight = rowCount == 0 ? newHeight : Math.Max(1f, oldHeight / rowCount);
        var sourceTotal = 0f;
        for (var index = 0; index < rowCount; index++)
        {
            var height = tablixItem.Rows[index].Height > 0f
                ? tablixItem.Rows[index].Height
                : fallbackHeight;
            sourceHeights[index] = height;
            sourceTotal += height;
        }

        if (sourceTotal <= 0f)
        {
            sourceTotal = rowCount;
        }

        var remainingSource = sourceTotal;
        var remainingTarget = Math.Max(1f, newHeight);
        for (var index = 0; index < rowCount; index++)
        {
            var remainingRows = rowCount - index - 1;
            var height = index == rowCount - 1
                ? remainingTarget
                : Math.Max(1f, remainingTarget * (sourceHeights[index] / remainingSource));
            var maxHeight = Math.Max(1f, remainingTarget - remainingRows);
            height = Math.Clamp(height, 1f, maxHeight);
            tablixItem.Rows[index].Height = height;
            remainingTarget -= height;
            remainingSource -= sourceHeights[index];
        }
    }

    internal void CompleteSurfaceInteraction(ReportDesignerCanvasItemViewModel canvasItem)
    {
        ArgumentNullException.ThrowIfNull(canvasItem);
        ClearSnapGuides();
        RebuildPropertiesAndExpressions();
        RefreshDataWorkspaceEditors();
        RefreshLightweightViews();
        StatusMessage = $"Updated {canvasItem.Label}. Preview is out of date.";
    }

    internal bool TryApplyDesignerDrop(
        ReportDesignerDragPayload payload,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return payload switch
        {
            ReportDesignerDataFieldDragPayload fieldPayload => TryApplyFieldDrop(fieldPayload, surfaceX, surfaceY, targetCanvasItem),
            ReportDesignerDataSetDragPayload dataSetPayload => TryApplyDataSetDrop(dataSetPayload, surfaceX, surfaceY, targetCanvasItem),
            ReportDesignerParameterDragPayload parameterPayload => TryApplyParameterDrop(parameterPayload, surfaceX, surfaceY, targetCanvasItem),
            ReportDesignerBuiltInFieldDragPayload builtInFieldPayload => TryApplyBuiltInFieldDrop(builtInFieldPayload, surfaceX, surfaceY, targetCanvasItem),
            ReportDesignerImageResourceDragPayload imageResourcePayload => TryApplyImageResourceDrop(imageResourcePayload, surfaceX, surfaceY, targetCanvasItem),
            _ => false
        };
    }

    private bool TryApplyFieldDrop(
        ReportDesignerDataFieldDragPayload payload,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        var targetItem = targetCanvasItem?.IsReadOnly == true ? null : targetCanvasItem?.Item;
        switch (targetItem)
        {
            case TextItem textItem:
                textItem.StaticText = null;
                textItem.ValueExpression = CreateReportFieldExpression(payload.DataSet.Id, payload.FieldName);
                RebuildDesignerState(textItem);
                MarkDirty($"Bound field '{payload.FieldName}' to text item.");
                return true;
            case TablixItem tablixItem:
                tablixItem.DataSetId = payload.DataSet.Id;
                AppendFieldColumnToTablix(tablixItem, payload.FieldName);
                RebuildDesignerState(tablixItem);
                MarkDirty($"Added field '{payload.FieldName}' to tablix.");
                return true;
            case ChartItem chartItem:
                chartItem.DataSetId = payload.DataSet.Id;
                InsertFieldIntoChart(chartItem, payload.DataSet, payload.FieldName, payload.DataType);
                RebuildDesignerState(chartItem);
                MarkDirty($"Added field '{payload.FieldName}' to chart.");
                return true;
            case DocumentTemplateItem templateItem:
                ApplyTemplateBindingAndPlaceholder(templateItem, payload.FieldName, CreateReportFieldExpression(payload.DataSet.Id, payload.FieldName));
                RebuildDesignerState(templateItem);
                MarkDirty($"Bound field '{payload.FieldName}' to template.");
                return true;
        }

        var item = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = payload.FieldName,
            ValueExpression = CreateReportFieldExpression(payload.DataSet.Id, payload.FieldName),
            Bounds = new ReportItemBounds(0f, 0f, 220f, 40f)
        };
        PlaceDroppedItem(item, surfaceX, surfaceY, targetCanvasItem);
        MarkDirty($"Added text item for field '{payload.FieldName}'.");
        return true;
    }

    private bool TryApplyDataSetDrop(
        ReportDesignerDataSetDragPayload payload,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        var targetItem = targetCanvasItem?.IsReadOnly == true ? null : targetCanvasItem?.Item;
        switch (targetItem)
        {
            case TablixItem tablixItem:
                tablixItem.DataSetId = payload.DataSet.Id;
                ConfigureTablixFromDataSet(tablixItem, payload.DataSet);
                RebuildDesignerState(tablixItem);
                MarkDirty($"Bound dataset '{payload.DataSet.Id}' to tablix.");
                return true;
            case ChartItem chartItem:
                chartItem.DataSetId = payload.DataSet.Id;
                ConfigureChartFromDataSet(chartItem, payload.DataSet);
                RebuildDesignerState(chartItem);
                MarkDirty($"Bound dataset '{payload.DataSet.Id}' to chart.");
                return true;
        }

        var ownerSection = ResolveDropSection(targetCanvasItem);
        var tablix = CreateDefaultTablix(
            CreateUniqueId("tablix", EnumerateItemIds()),
            payload.DataSet.Id,
            ownerSection);
        ConfigureTablixFromDataSet(tablix, payload.DataSet);
        PlaceDroppedItem(tablix, surfaceX, surfaceY, targetCanvasItem);
        MarkDirty($"Added tablix bound to dataset '{payload.DataSet.Id}'.");
        return true;
    }

    private bool TryApplyParameterDrop(
        ReportDesignerParameterDragPayload payload,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        var targetItem = targetCanvasItem?.IsReadOnly == true ? null : targetCanvasItem?.Item;
        switch (targetItem)
        {
            case TextItem textItem:
                textItem.StaticText = null;
                textItem.ValueExpression = $"Parameters.{payload.Parameter.Id}";
                RebuildDesignerState(textItem);
                MarkDirty($"Bound parameter '{payload.Parameter.Id}' to text item.");
                return true;
            case DocumentTemplateItem templateItem:
                ApplyTemplateBindingAndPlaceholder(templateItem, payload.Parameter.Id, $"Parameters.{payload.Parameter.Id}");
                RebuildDesignerState(templateItem);
                MarkDirty($"Bound parameter '{payload.Parameter.Id}' to template.");
                return true;
        }

        var item = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = payload.Parameter.DisplayName,
            ValueExpression = $"Parameters.{payload.Parameter.Id}",
            Bounds = new ReportItemBounds(0f, 0f, 220f, 40f)
        };
        PlaceDroppedItem(item, surfaceX, surfaceY, targetCanvasItem);
        MarkDirty($"Added text item for parameter '{payload.Parameter.Id}'.");
        return true;
    }

    private bool TryApplyBuiltInFieldDrop(
        ReportDesignerBuiltInFieldDragPayload payload,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        var targetItem = targetCanvasItem?.IsReadOnly == true ? null : targetCanvasItem?.Item;
        switch (targetItem)
        {
            case TextItem textItem:
                textItem.StaticText = null;
                textItem.ValueExpression = payload.Definition.Expression;
                RebuildDesignerState(textItem);
                MarkDirty($"Bound built-in field '{payload.Definition.Label}' to text item.");
                return true;
            case DocumentTemplateItem templateItem:
                ApplyTemplateBindingAndPlaceholder(templateItem, payload.Definition.Id, payload.Definition.Expression);
                RebuildDesignerState(templateItem);
                MarkDirty($"Bound built-in field '{payload.Definition.Label}' to template.");
                return true;
        }

        var item = new TextItem
        {
            Id = CreateUniqueId("text", EnumerateItemIds()),
            Name = payload.Definition.Label,
            ValueExpression = payload.Definition.Expression,
            Bounds = new ReportItemBounds(0f, 0f, 220f, 40f)
        };
        PlaceDroppedItem(item, surfaceX, surfaceY, targetCanvasItem);
        MarkDirty($"Added built-in field '{payload.Definition.Label}' to the surface.");
        return true;
    }

    private bool TryApplyImageResourceDrop(
        ReportDesignerImageResourceDragPayload payload,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        var targetItem = targetCanvasItem?.IsReadOnly == true ? null : targetCanvasItem?.Item;
        switch (targetItem)
        {
            case ImageItem imageItem:
                ApplyImageResourceDefinition(imageItem, payload.Definition);
                RebuildDesignerState(imageItem);
                MarkDirty($"Updated image item from '{payload.Definition.Label}'.");
                return true;
        }

        var item = new ImageItem
        {
            Id = CreateUniqueId("image", EnumerateItemIds()),
            Name = payload.Definition.Label,
            Bounds = new ReportItemBounds(0f, 0f, 220f, 140f)
        };
        ApplyImageResourceDefinition(item, payload.Definition);
        PlaceDroppedItem(item, surfaceX, surfaceY, targetCanvasItem);
        MarkDirty($"Added image resource '{payload.Definition.Label}' to the surface.");
        return true;
    }

    private void PlaceDroppedItem(
        ReportItem item,
        double surfaceX,
        double surfaceY,
        ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        ArgumentNullException.ThrowIfNull(item);

        var parentContainer = ResolveDropContainer(targetCanvasItem);
        if (parentContainer is not null)
        {
            var parentCanvasItem = _itemCanvasMap[parentContainer];
            item.Bounds = item.Bounds with
            {
                X = SnapAndClamp((float)(surfaceX - parentCanvasItem.Left), 0f, Math.Max(0f, parentContainer.Bounds.Width - item.Bounds.Width)),
                Y = SnapAndClamp((float)(surfaceY - parentCanvasItem.Top), 0f, Math.Max(0f, parentContainer.Bounds.Height - item.Bounds.Height))
            };
            parentContainer.Items.Add(item);
            RebuildDesignerState(item);
            return;
        }

        var section = ResolveDropSection(targetCanvasItem);
        item.Bounds = item.Bounds with
        {
            X = SnapAndClamp((float)surfaceX, 0f, Math.Max(0f, section.PageSettings.Width - item.Bounds.Width)),
            Y = SnapAndClamp((float)surfaceY, 0f, Math.Max(0f, section.PageSettings.Height - item.Bounds.Height))
        };
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
    }

    private ContainerItem? ResolveDropContainer(ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        if (targetCanvasItem is null || targetCanvasItem.IsReadOnly)
        {
            return null;
        }

        if (targetCanvasItem.Item is ContainerItem directContainer)
        {
            return directContainer;
        }

        return _itemContainerMap.TryGetValue(targetCanvasItem.Item, out var parentContainer)
            ? parentContainer
            : null;
    }

    private ReportSection ResolveDropSection(ReportDesignerCanvasItemViewModel? targetCanvasItem)
    {
        if (targetCanvasItem is not null
            && _itemSectionMap.TryGetValue(targetCanvasItem.Item, out var ownerSection))
        {
            return ownerSection;
        }

        return EnsureSelectedSection();
    }

    private void EnsurePreviewMarkedDirtyForSurfaceEdit(string message)
    {
        if (!IsPreviewDirty)
        {
            IsPreviewDirty = true;
            UpdateSurfacePreviewMode();
            this.RaisePropertyChanged(nameof(HasCurrentSurfacePreview));
        }

        StatusMessage = message;
    }

    private void TranslateCanvasDescendants(ContainerItem containerItem, float deltaX, float deltaY)
    {
        foreach (var child in containerItem.Items)
        {
            if (_itemCanvasMap.TryGetValue(child, out var childCanvasItem))
            {
                childCanvasItem.Left += deltaX;
                childCanvasItem.Top += deltaY;
            }

            if (child is ContainerItem nestedContainer)
            {
                TranslateCanvasDescendants(nestedContainer, deltaX, deltaY);
            }
        }
    }

    private bool TryResizeLineItemByDelta(
        ReportDesignerCanvasItemViewModel canvasItem,
        LineItem lineItem,
        ReportDesignerSurfaceResizeHandle handle,
        double deltaX,
        double deltaY)
    {
        var startX = lineItem.Bounds.X;
        var startY = lineItem.Bounds.Y;
        var endX = lineItem.X2;
        var endY = lineItem.Y2;

        if (handle is ReportDesignerSurfaceResizeHandle.West or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.SouthWest)
        {
            startX += (float)deltaX;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.East or ReportDesignerSurfaceResizeHandle.NorthEast or ReportDesignerSurfaceResizeHandle.SouthEast)
        {
            endX += (float)deltaX;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.North or ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.NorthEast)
        {
            startY += (float)deltaY;
        }

        if (handle is ReportDesignerSurfaceResizeHandle.South or ReportDesignerSurfaceResizeHandle.SouthWest or ReportDesignerSurfaceResizeHandle.SouthEast)
        {
            endY += (float)deltaY;
        }

        var left = MathF.Min(startX, endX);
        var top = MathF.Min(startY, endY);
        var width = MathF.Max(DesignerMinItemWidth, MathF.Abs(endX - startX));
        var height = MathF.Max(DesignerMinItemHeight, MathF.Abs(endY - startY));
        var (minX, minY, maxX, maxY) = GetMovementConstraints(lineItem);
        left = SnapAndClamp(left, minX, maxX);
        top = SnapAndClamp(top, minY, maxY);
        width = SnapAndClamp(width, DesignerMinItemWidth, GetMaxWidth(lineItem, left));
        height = SnapAndClamp(height, DesignerMinItemHeight, GetMaxHeight(lineItem, top));

        var oldBounds = lineItem.Bounds;
        lineItem.Bounds = new ReportItemBounds(left, top, width, height);
        lineItem.X2 = left + width;
        lineItem.Y2 = top + height;

        if (Math.Abs(lineItem.Bounds.X - oldBounds.X) < float.Epsilon
            && Math.Abs(lineItem.Bounds.Y - oldBounds.Y) < float.Epsilon
            && Math.Abs(lineItem.Bounds.Width - oldBounds.Width) < float.Epsilon
            && Math.Abs(lineItem.Bounds.Height - oldBounds.Height) < float.Epsilon)
        {
            return false;
        }

        canvasItem.Left += lineItem.Bounds.X - oldBounds.X;
        canvasItem.Top += lineItem.Bounds.Y - oldBounds.Y;
        canvasItem.Width = lineItem.Bounds.Width;
        canvasItem.Height = lineItem.Bounds.Height;
        return true;
    }

    private (float minX, float minY, float maxX, float maxY) GetMovementConstraints(ReportItem item)
    {
        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is not null)
        {
            return (
                0f,
                0f,
                Math.Max(0f, parentContainer.Bounds.Width - item.Bounds.Width),
                Math.Max(0f, parentContainer.Bounds.Height - item.Bounds.Height));
        }

        var section = _itemSectionMap.TryGetValue(item, out var ownerSection)
            ? ownerSection
            : EnsureSelectedSection();
        return (
            0f,
            0f,
            Math.Max(0f, section.PageSettings.Width - item.Bounds.Width),
            Math.Max(0f, section.PageSettings.Height - item.Bounds.Height));
    }

    private float GetMaxWidth(ReportItem item, float x)
    {
        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is not null)
        {
            return Math.Max(DesignerMinItemWidth, parentContainer.Bounds.Width - x);
        }

        var section = _itemSectionMap.TryGetValue(item, out var ownerSection)
            ? ownerSection
            : EnsureSelectedSection();
        return Math.Max(DesignerMinItemWidth, section.PageSettings.Width - x);
    }

    private float GetMaxHeight(ReportItem item, float y)
    {
        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is not null)
        {
            return Math.Max(DesignerMinItemHeight, parentContainer.Bounds.Height - y);
        }

        var section = _itemSectionMap.TryGetValue(item, out var ownerSection)
            ? ownerSection
            : EnsureSelectedSection();
        return Math.Max(DesignerMinItemHeight, section.PageSettings.Height - y);
    }

    private static float SnapAndClamp(float value, float minimum, float maximum)
    {
        if (maximum < minimum)
        {
            maximum = minimum;
        }

        var snapped = MathF.Round(value / DesignerSnapStep) * DesignerSnapStep;
        return Math.Clamp(snapped, minimum, maximum);
    }
}
