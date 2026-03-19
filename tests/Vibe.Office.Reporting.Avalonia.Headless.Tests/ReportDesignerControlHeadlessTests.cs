using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Vibe.Office.Reporting.Avalonia;
using Vibe.Office.Printing;
using Vibe.Office.Reporting.Avalonia.Designer;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Export;
using Xunit;

namespace Vibe.Office.Reporting.Avalonia.Headless.Tests;

public sealed class ReportDesignerControlHeadlessTests
{
    [AvaloniaFact]
    public async Task DesignerControl_LoadsExplorerGalleryAndDesignSurface()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1600,
            Height = 980
        };
        var window = new Window
        {
            Width = 1640,
            Height = 1024,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.NotEmpty(viewModel.ExplorerNodes);
        Assert.NotEmpty(viewModel.DesignItems);
        Assert.NotEmpty(viewModel.TemplateGalleryItems);
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerDesignSurface>().FirstOrDefault());
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerReportDataPane>().FirstOrDefault());
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerGroupingPane>().FirstOrDefault());
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerPropertiesPane>().FirstOrDefault());

        window.Close();
    }

    [AvaloniaFact]
    public async Task Designer_DefaultLayoutStartsCanvasFirst()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1600,
            Height = 980
        };
        var window = new Window
        {
            Width = 1680,
            Height = 1024,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        var designSurface = Assert.Single(control.GetVisualDescendants().OfType<ReportDesignerDesignSurface>());

        Assert.Equal(PaneVisibilityState.Closed, viewModel.LeftDrawerState);
        Assert.Equal(PaneVisibilityState.Closed, viewModel.RightDrawerState);
        Assert.Equal(PaneVisibilityState.Closed, viewModel.ContextTrayState);
        Assert.Equal(0d, viewModel.LeftDrawerWidth);
        Assert.Equal(0d, viewModel.RightDrawerWidth);
        Assert.True(designSurface.Bounds.Width > 1050d);
        Assert.True(designSurface.Bounds.Height > 640d);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Designer_DrawersAndContextTrayStayCanvasFirstAndContextual()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1600,
            Height = 980
        };
        var window = new Window
        {
            Width = 1680,
            Height = 1024,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        var designSurface = Assert.Single(control.GetVisualDescendants().OfType<ReportDesignerDesignSurface>());
        var closedWidth = designSurface.Bounds.Width;

        Execute(viewModel.OpenReportDataPaneCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        Assert.Equal(PaneVisibilityState.Open, viewModel.LeftDrawerState);
        Assert.True(viewModel.IsLeftDrawerVisible);
        Assert.True(designSurface.Bounds.Width < closedWidth);

        Execute(viewModel.TogglePinLeftDrawerCommand);
        Assert.Equal(PaneVisibilityState.Pinned, viewModel.LeftDrawerState);

        Execute(viewModel.AddTablixItemCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        Assert.True(viewModel.ShowGroupingTray);
        Assert.True(viewModel.IsContextTrayVisible);

        Execute(viewModel.AddChartItemCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        Assert.True(viewModel.ShowChartDataTray);
        Assert.True(viewModel.IsContextTrayVisible);

        Execute(viewModel.OpenParameterLayoutTrayCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        Assert.True(viewModel.ShowParameterLayoutTray);

        Execute(viewModel.CloseLeftDrawerCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        Assert.Equal(PaneVisibilityState.Closed, viewModel.LeftDrawerState);
        Assert.True(designSurface.Bounds.Width >= closedWidth);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Designer_PageSurfaceExposesVisibleScrollBarsForPageViews()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 960,
            Height = 720
        };
        var window = new Window
        {
            Width = 980,
            Height = 760,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        Execute(viewModel.ActualSizeCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        var scrollViewer = control.GetVisualDescendants().OfType<ScrollViewer>()
            .First(viewer => viewer.Content is Border);

        Assert.Equal(ScrollBarVisibility.Visible, scrollViewer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Visible, scrollViewer.VerticalScrollBarVisibility);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Designer_ToolbarTogglesUseBoundStateWithoutCommandDoubleToggle()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 860
        };
        var window = new Window
        {
            Width = 1320,
            Height = 900,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        var toggles = control.GetVisualDescendants().OfType<ToggleButton>().ToArray();
        var rulersToggle = toggles.Single(toggle => toggle.Content is TextBlock { Text: "Rulers" });
        var previewToggle = toggles.Single(toggle => toggle.Content is TextBlock { Text: "Preview" });

        Assert.Null(rulersToggle.Command);
        Assert.Null(previewToggle.Command);

        rulersToggle.IsChecked = false;
        previewToggle.IsChecked = false;
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        Assert.False(viewModel.ShowRulers);
        Assert.False(viewModel.ShowSurfacePreviewBackground);

        rulersToggle.IsChecked = true;
        previewToggle.IsChecked = true;
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        Assert.True(viewModel.ShowRulers);
        Assert.True(viewModel.ShowSurfacePreviewBackground);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Designer_SmallItemsKeepActualSelectionBoundsWithExpandedHitTargets()
    {
        using var viewModel = new ReportDesignerViewModel(CreateSelectionGeometrySource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1200,
            Height = 860
        };
        var window = new Window
        {
            Width = 1240,
            Height = 900,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        var lineCanvasItem = Assert.Single(viewModel.DesignItems, item => item.Item is LineItem);
        var lineItem = Assert.IsType<LineItem>(lineCanvasItem.Item);
        Assert.Equal(lineItem.Bounds.Width, (float)lineCanvasItem.Width);
        Assert.Equal(lineItem.Bounds.Height, (float)lineCanvasItem.Height);
        Assert.True(lineCanvasItem.SurfaceHostHeight > lineCanvasItem.Height);
        Assert.True(lineCanvasItem.InteractionPaddingY > 0d);

        viewModel.SelectedCanvasItem = lineCanvasItem;
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        lineCanvasItem = Assert.Single(viewModel.DesignItems, item => ReferenceEquals(item.Item, lineItem));

        Assert.Equal(lineCanvasItem.Left, lineItem.Bounds.X, 3);
        Assert.Equal(lineCanvasItem.Top, lineItem.Bounds.Y, 3);
        Assert.Equal(lineCanvasItem.Left, lineCanvasItem.SurfaceHostLeft, 3);
        Assert.True(lineCanvasItem.SurfaceHostTop < lineCanvasItem.Top);
        Assert.Equal(lineCanvasItem.Width, lineCanvasItem.SurfaceHostWidth, 3);
        Assert.True(lineCanvasItem.SurfaceHostHeight > lineCanvasItem.Height);

        var tinyTextCanvasItem = Assert.Single(
            viewModel.DesignItems,
            item => item.Item is TextItem textItem && string.Equals(textItem.Id, "tiny-caption", StringComparison.Ordinal));
        var tinyTextItem = Assert.IsType<TextItem>(tinyTextCanvasItem.Item);
        Assert.Equal(tinyTextItem.Bounds.Width, (float)tinyTextCanvasItem.Width);
        Assert.Equal(tinyTextItem.Bounds.Height, (float)tinyTextCanvasItem.Height);
        Assert.True(tinyTextCanvasItem.SurfaceHostWidth > tinyTextCanvasItem.Width);
        Assert.True(tinyTextCanvasItem.SurfaceHostHeight > tinyTextCanvasItem.Height);

        window.Close();
    }

    [AvaloniaFact]
    public void Designer_AddTextItemAndPropertyEditingUpdateDefinition()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());

        Execute(viewModel.AddTextItemCommand);

        var item = viewModel.DesignItems.Last().Item;
        viewModel.SelectedCanvasItem = viewModel.DesignItems.Last();

        var nameProperty = Assert.IsType<ReportDesignerTextPropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "item.name"));
        nameProperty.Value = "Revenue Summary";

        var textProperty = Assert.IsType<ReportDesignerTextPropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "text.staticText"));
        textProperty.Value = "Quarter close";

        var textItem = Assert.IsType<TextItem>(item);
        Assert.Equal("Revenue Summary", textItem.Name);
        Assert.Equal("Quarter close", textItem.StaticText);
        Assert.True(viewModel.IsPreviewDirty);
    }

    [AvaloniaFact]
    public async Task Designer_ExpressionValidationAndPreviewRefreshWork()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        viewModel.SelectedCanvasItem = viewModel.DesignItems.First();

        var textItem = Assert.IsType<TextItem>(viewModel.SelectedCanvasItem!.Item);
        var originalExpression = textItem.ValueExpression;

        viewModel.SelectedExpressionEntry = viewModel.ExpressionEntries.First(entry => entry.Id == "text.valueExpression");
        viewModel.SelectedExpressionText = "Parameters.Title + )";
        Execute(viewModel.ApplySelectedExpressionCommand);

        Assert.NotNull(viewModel.ExpressionStatusMessage);
        Assert.Equal(originalExpression, textItem.ValueExpression);

        viewModel.SelectedExpressionText = "'Changed Title'";
        Execute(viewModel.ApplySelectedExpressionCommand);

        Assert.Equal("'Changed Title'", textItem.ValueExpression);

        await viewModel.RefreshPreviewAsync();

        Assert.False(viewModel.IsPreviewDirty);
        Assert.NotNull(viewModel.PreviewViewModel.Source);
        Assert.Single(viewModel.PreviewViewModel.Pages);
    }

    [AvaloniaFact]
    public async Task Designer_FailedPreviewRefreshKeepsDirtyState()
    {
        using var viewModel = new ReportDesignerViewModel(
            CreateDesignerSource(),
            new PreviewFailureSessionService());

        await viewModel.RefreshPreviewAsync();

        Assert.True(viewModel.IsPreviewDirty);
        Assert.Equal(0, viewModel.SelectedCenterTabIndex);
        Assert.Equal("Preview execution failed.", viewModel.StatusMessage);
        Assert.Null(viewModel.PreviewViewModel.CurrentSnapshot);
    }

    [AvaloniaFact]
    public void Designer_TemplateGalleryAddsNarrativeBlock()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());

        viewModel.SelectedGalleryItem = viewModel.TemplateGalleryItems.First(item => item.Id == "narrative-brief");
        Execute(viewModel.ApplySelectedTemplateCommand);

        Assert.Contains(viewModel.ReportDefinition.SharedTemplates, template => template.Format == ReportDocumentTemplateFormat.Markdown);
        Assert.Contains(viewModel.ReportDefinition.Sections[0].BodyItems, item => item is DocumentTemplateItem);
        Assert.True(viewModel.IsPreviewDirty);
    }

    [AvaloniaFact]
    public void Designer_TablixRowEditingPreservesExistingCellMetadata()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());

        Execute(viewModel.AddTablixItemCommand);

        var tablixItem = Assert.IsType<TablixItem>(viewModel.DesignItems.Last().Item);
        tablixItem.Rows[1].Cells[1].FormatString = "N2";
        tablixItem.Rows[1].Cells[1].StyleName = "currency";
        tablixItem.Rows[1].Cells[1].RowSpan = 2;
        tablixItem.Rows[1].Cells[1].ColumnSpan = 3;
        var detailRowId = tablixItem.Rows[1].Id;

        viewModel.SelectedCanvasItem = viewModel.DesignItems.Last();

        var rowsProperty = Assert.IsType<ReportDesignerTextPropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "tablix.rows"));
        rowsProperty.Value = "H:Category|Value\nD:Fields.Category|Fields.Revenue";

        Assert.StartsWith("tablix", tablixItem.Id, StringComparison.Ordinal);
        Assert.Equal(detailRowId, tablixItem.Rows[1].Id);
        Assert.Equal("N2", tablixItem.Rows[1].Cells[1].FormatString);
        Assert.Equal("currency", tablixItem.Rows[1].Cells[1].StyleName);
        Assert.Equal(2, tablixItem.Rows[1].Cells[1].RowSpan);
        Assert.Equal(3, tablixItem.Rows[1].Cells[1].ColumnSpan);
    }

    [AvaloniaFact]
    public void Designer_DataSourceProviderOptionsExposeConnectorCatalog()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());

        Execute(viewModel.AddDataSourceCommand);
        viewModel.SelectedDataSourceEntry = viewModel.DataSourceEntries.Last();

        var providerProperty = Assert.IsType<ReportDesignerChoicePropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "dataSource.providerId"));

        Assert.Contains(providerProperty.Options, option => option.Value == ReportProviderIds.SqlServer);
        Assert.Contains(providerProperty.Options, option => option.Value == ReportProviderIds.PostgreSql);
        Assert.Contains(providerProperty.Options, option => option.Value == ReportProviderIds.RestJson);
        Assert.Contains(providerProperty.Options, option => option.Value == ReportProviderIds.GraphQl);
    }

    [AvaloniaFact]
    public async Task Designer_DataWorkspacePreviewsSelectedDataSet()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDataWorkspaceSource());

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");

        await ExecuteAsync(viewModel.PreviewSelectedDataSetCommand);

        Assert.Equal("sales", viewModel.SelectedDataSetEntry?.Title);
        Assert.Equal(3, viewModel.DataPreviewColumns.Count);
        Assert.NotEmpty(viewModel.DataPreviewRows);
        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);
        Assert.Contains("Revenue", viewModel.SelectedDataSetFieldsText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Designer_DataWorkspaceFieldInsertionBindsSelectedTextItem()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDataWorkspaceSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems);
        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");

        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Revenue");

        await ExecuteAsync(viewModel.InsertSelectedDataFieldCommand);

        var textItem = Assert.IsType<TextItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Equal("First(Fields.Revenue, 'sales')", textItem.ValueExpression);
        Assert.True(viewModel.IsPreviewDirty);
    }

    [Fact]
    public void Designer_ReportDataWorkspaceIncludesBuiltInFieldsAndImageResources()
    {
        using var viewModel = new ReportDesignerViewModel(CreateResourceWorkspaceSource());

        var builtInGroup = Assert.Single(viewModel.ReportDataNodes, node => string.Equals(node.Title, "Built-in Fields", StringComparison.Ordinal));
        var imagesGroup = Assert.Single(viewModel.ReportDataNodes, node => string.Equals(node.Title, "Images", StringComparison.Ordinal));

        Assert.Contains(builtInGroup.Children, node => node.Kind == ReportDesignerDataNodeKind.BuiltInField && string.Equals(node.Title, "Page Number", StringComparison.Ordinal));
        Assert.Contains(imagesGroup.Children, node => node.Kind == ReportDesignerDataNodeKind.ImageResource && string.Equals(node.Title, "Company Logo", StringComparison.Ordinal));
    }

    [Fact]
    public void Designer_BuiltInFieldAndImageResourceInsertionUpdateSelection()
    {
        using var viewModel = new ReportDesignerViewModel(CreateResourceWorkspaceSource());

        var headlineCanvasItem = Assert.Single(
            viewModel.DesignItems,
            item => item.Item is TextItem textItem && string.Equals(textItem.Id, "headline", StringComparison.Ordinal));
        viewModel.SelectedCanvasItem = headlineCanvasItem;

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.BuiltInField && string.Equals(node.Title, "Page Number", StringComparison.Ordinal));
        Execute(viewModel.InsertSelectedDataFieldCommand);

        var headlineItem = Assert.IsType<TextItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Equal("Globals.PageNumber", headlineItem.ValueExpression);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.ImageResource && string.Equals(node.Title, "Company Logo", StringComparison.Ordinal));
        Execute(viewModel.InsertSelectedDataFieldCommand);

        var imageItem = Assert.IsType<ImageItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Equal("Company Logo", imageItem.Name);
        Assert.Equal(ReportImageSourceKind.Uri, imageItem.SourceKind);
        Assert.Equal("'https://cdn.contoso.test/logo.png'", imageItem.ValueExpression);
        Assert.True(viewModel.IsPreviewDirty);
    }

    [Fact]
    public void Designer_SurfaceInteractionMovesAndResizesSelectedItem()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems);

        var canvasItem = viewModel.SelectedCanvasItem!;
        var textItem = Assert.IsType<TextItem>(canvasItem.Item);

        Assert.True(viewModel.TryMoveSurfaceItemByDelta(canvasItem, 32d, 24d));
        Assert.Equal(72f, textItem.Bounds.X);
        Assert.Equal(64f, textItem.Bounds.Y);

        Assert.True(viewModel.TryResizeSurfaceItemByDelta(
            canvasItem,
            ReportDesignerSurfaceResizeHandle.SouthEast,
            44d,
            20d));

        viewModel.CompleteSurfaceInteraction(canvasItem);

        Assert.Equal(364f, textItem.Bounds.Width);
        Assert.Equal(60f, textItem.Bounds.Height);
        Assert.True(viewModel.IsPreviewDirty);

        var xProperty = Assert.IsType<ReportDesignerTextPropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "item.x"));
        var widthProperty = Assert.IsType<ReportDesignerTextPropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "item.width"));

        Assert.Equal("72", xProperty.Value);
        Assert.Equal("364", widthProperty.Value);
    }

    [AvaloniaFact]
    public async Task Designer_DropPayloadsCreateAndBindDesignItems()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDataWorkspaceSource());

        var salesDataSetNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");
        viewModel.SelectedDataNode = salesDataSetNode;
        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);

        var revenueFieldNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Revenue");
        var titleParameterNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.Parameter && node.Title == "Region");

        var revenuePayload = Assert.IsType<ReportDesignerDataFieldDragPayload>(
            ReportDesignerDragPayloadFactory.Create(revenueFieldNode));
        Assert.True(viewModel.TryApplyDesignerDrop(revenuePayload, 180d, 260d, targetCanvasItem: null));

        var createdFieldTextItem = Assert.IsType<TextItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Equal("First(Fields.Revenue, 'sales')", createdFieldTextItem.ValueExpression);
        Assert.Equal(180f, createdFieldTextItem.Bounds.X);
        Assert.Equal(260f, createdFieldTextItem.Bounds.Y);

        var dataSetPayload = Assert.IsType<ReportDesignerDataSetDragPayload>(
            ReportDesignerDragPayloadFactory.Create(salesDataSetNode));
        Assert.True(viewModel.TryApplyDesignerDrop(dataSetPayload, 240d, 340d, targetCanvasItem: null));
        Assert.IsType<TablixItem>(viewModel.SelectedCanvasItem!.Item);

        viewModel.SelectedCanvasItem = viewModel.DesignItems.OfType<ReportDesignerCanvasItemViewModel>()
            .First(item => item.Item is TextItem);
        var parameterPayload = Assert.IsType<ReportDesignerParameterDragPayload>(
            ReportDesignerDragPayloadFactory.Create(titleParameterNode));
        Assert.True(viewModel.TryApplyDesignerDrop(parameterPayload, 0d, 0d, viewModel.SelectedCanvasItem));

        var targetTextItem = Assert.IsType<TextItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Equal("Parameters.Region", targetTextItem.ValueExpression);
    }

    [Fact]
    public void Designer_InsertToolPlacementCreatesItemAndClearsTool()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());

        Execute(viewModel.BeginInsertRectangleCommand);

        Assert.True(viewModel.HasActiveInsertTool);
        Assert.Equal(ReportDesignerInsertTool.Rectangle, viewModel.ActiveInsertTool);
        Assert.True(viewModel.TryCommitInsertToolPlacement(120d, 140d, 360d, 300d));

        var rectangle = Assert.IsType<ContainerItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Equal(120f, rectangle.Bounds.X);
        Assert.Equal(140f, rectangle.Bounds.Y);
        Assert.Equal(240f, rectangle.Bounds.Width);
        Assert.Equal(160f, rectangle.Bounds.Height);
        Assert.False(viewModel.HasActiveInsertTool);
    }

    [Fact]
    public void Designer_WorkbenchDuplicateAndArrangeCommandsUpdateOrder()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems);

        Execute(viewModel.DuplicateSelectedCommand);

        var items = viewModel.ReportDefinition.Sections[0].BodyItems;
        Assert.Equal(2, items.Count);
        Assert.NotEqual(items[0].Id, items[1].Id);
        Assert.True(items[1].Bounds.X > items[0].Bounds.X);
        Assert.True(items[1].Bounds.Y > items[0].Bounds.Y);

        viewModel.SelectedCanvasItem = viewModel.DesignItems.First(item => ReferenceEquals(item.Item, items[0]));
        Execute(viewModel.BringSelectedToFrontCommand);
        Assert.Same(items[^1], viewModel.SelectedCanvasItem!.Item);

        Execute(viewModel.SendSelectedToBackCommand);
        Assert.Same(items[0], viewModel.SelectedCanvasItem!.Item);
    }

    [Fact]
    public void Designer_SurfaceMoveProducesAndClearsSnapGuides()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems);

        var canvasItem = viewModel.SelectedCanvasItem!;
        Assert.True(viewModel.TryMoveSurfaceItemByDelta(canvasItem, 32d, 0d));
        Assert.Contains(viewModel.SnapGuides, guide => guide.IsVertical && Math.Abs(guide.Offset - 72d) < 0.1d);

        viewModel.CompleteSurfaceInteraction(canvasItem);

        Assert.Empty(viewModel.SnapGuides);
    }

    [AvaloniaFact]
    public async Task Designer_GroupingPaneAuthorsRowGroupsFromSelectedField()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDataWorkspaceSource());
        Execute(viewModel.AddTablixItemCommand);
        viewModel.SelectedCanvasItem = viewModel.DesignItems.Last();

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");
        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);
        var regionFieldNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Region");
        viewModel.SelectedDataNode = regionFieldNode;
        var payload = Assert.IsType<ReportDesignerDataFieldDragPayload>(
            ReportDesignerDragPayloadFactory.Create(regionFieldNode));

        Execute(viewModel.AddRowGroupCommand);

        var tablix = Assert.IsType<TablixItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.NotEmpty(tablix.RowMembers);
        Assert.Contains(tablix.RowMembers, member => member.Kind == ReportTablixMemberKind.Group);

        Assert.True(viewModel.TryApplyGroupingDrop(payload, ReportDesignerGroupingDropTarget.RowGroups));
        Assert.True(tablix.RowMembers.Count >= 2);
    }

    [Fact]
    public void Designer_ParameterPaneReordersParametersAcrossLayoutGrid()
    {
        using var viewModel = new ReportDesignerViewModel(CreateParameterLayoutSource());

        Assert.True(viewModel.ShowParameterLayoutPane);
        Assert.Equal(2, viewModel.ParameterLayoutRows.Count);
        Assert.Equal(2, viewModel.ParameterLayoutRows[0].Cells.Count);

        var regionCell = viewModel.ParameterLayoutRows.SelectMany(static row => row.Cells)
            .Single(cell => string.Equals(cell.ParameterId, "Region", StringComparison.Ordinal));
        viewModel.SelectedParameterLayoutCell = regionCell;

        Execute(viewModel.MoveSelectedParameterRightCommand);

        Assert.Equal("Region", viewModel.ParameterLayoutRows[1].Cells[1].ParameterId);
        Assert.Equal("Region", viewModel.ReportDefinition.Parameters[1].Id);
    }

    [Fact]
    public void Designer_ChartDataPaneBindsBucketsFromSelectedReportDataField()
    {
        using var viewModel = new ReportDesignerViewModel(CreateChartDesignerSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems);

        Assert.True(viewModel.ShowChartDataPane);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Revenue");
        Execute(viewModel.ApplySelectedFieldToChartValueCommand);

        var chart = Assert.IsType<ChartItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Contains(chart.Series, series => string.Equals(series.ValueExpression, "Fields.Revenue", StringComparison.Ordinal));

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Region");
        Execute(viewModel.ApplySelectedFieldToChartCategoryCommand);
        Execute(viewModel.ApplySelectedFieldToChartSeriesGroupCommand);

        Assert.Equal("Fields.Region", chart.CategoryExpression);
        Assert.Contains(chart.Series, series => string.Equals(series.NameExpression, "Fields.Region", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Designer_GroupingPaneAuthorsColumnGroupsAndAdvancedModeMemberProperties()
    {
        using var viewModel = new ReportDesignerViewModel(CreateGroupingDesignerSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems, item => item.Item is TablixItem);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");
        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Region");
        Execute(viewModel.AddRowGroupCommand);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Quarter");
        Execute(viewModel.AddColumnGroupCommand);

        var tablix = Assert.IsType<TablixItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Contains(tablix.RowMembers, member => member.Kind == ReportTablixMemberKind.Group);
        Assert.Contains(tablix.ColumnMembers, member => member.Kind == ReportTablixMemberKind.Group);
        Assert.Contains(viewModel.ColumnGroupEntries, entry => entry.Member?.Kind == ReportTablixMemberKind.Group);

        viewModel.ShowAdvancedGroupingMode = true;

        var staticColumnEntry = viewModel.ColumnGroupEntries.First(
            entry => entry.Member?.Kind == ReportTablixMemberKind.Static);
        viewModel.SelectedColumnGroupEntry = staticColumnEntry;

        var repeatProperty = Assert.IsType<ReportDesignerBooleanPropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "tablixMember.repeatOnNewPage"));
        repeatProperty.Value = true;

        var keepWithGroupProperty = Assert.IsType<ReportDesignerChoicePropertyViewModel>(
            viewModel.PropertyEntries.First(property => property.Id == "tablixMember.keepWithGroup"));
        keepWithGroupProperty.SelectedOption = Assert.Single(
            keepWithGroupProperty.Options,
            option => string.Equals(option.Value, "After", StringComparison.Ordinal));

        Assert.True(staticColumnEntry.Member!.RepeatOnNewPage);
        Assert.Equal("After", staticColumnEntry.Member.KeepWithGroup);
        Assert.Contains("Advanced", viewModel.GroupingStatusText, StringComparison.Ordinal);
        Assert.True(viewModel.IsPreviewDirty);
    }

    [AvaloniaFact]
    public async Task Designer_GroupingPaneSupportsParentChildAdjacentGroupsAndTotals()
    {
        using var viewModel = new ReportDesignerViewModel(CreateGroupingDesignerSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems, item => item.Item is TablixItem);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");
        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Region");
        Execute(viewModel.AddParentRowGroupCommand);

        var tablix = Assert.IsType<TablixItem>(viewModel.SelectedCanvasItem!.Item);
        var rowGroup = Assert.Single(tablix.RowMembers, member => member.Kind == ReportTablixMemberKind.Group);
        Assert.Equal("Fields.Region", rowGroup.GroupExpression);

        viewModel.SelectedRowGroupEntry = Assert.Single(
            viewModel.RowGroupEntries,
            entry => ReferenceEquals(entry.Member, rowGroup));

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Quarter");
        Execute(viewModel.AddChildRowGroupCommand);

        var childGroup = Assert.Single(rowGroup.Members, member => member.Kind == ReportTablixMemberKind.Group);
        Assert.Equal("Fields.Quarter", childGroup.GroupExpression);

        viewModel.SelectedRowGroupEntry = Assert.Single(
            viewModel.RowGroupEntries,
            entry => ReferenceEquals(entry.Member, childGroup));
        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Region");
        Execute(viewModel.AddAdjacentRowGroupCommand);

        Assert.True(rowGroup.Members.Count(member => member.Kind == ReportTablixMemberKind.Group) >= 2);
        Assert.Equal(
            rowGroup.Members.Count,
            rowGroup.Members.Select(static member => member.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Quarter");
        Execute(viewModel.AddParentColumnGroupCommand);
        var columnGroup = Assert.Single(tablix.ColumnMembers, member => member.Kind == ReportTablixMemberKind.Group);
        Assert.Equal("Fields.Quarter", columnGroup.GroupExpression);

        viewModel.SelectedColumnGroupEntry = Assert.Single(
            viewModel.ColumnGroupEntries,
            entry => ReferenceEquals(entry.Member, columnGroup));
        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Region");
        Execute(viewModel.AddAdjacentColumnGroupCommand);

        Assert.True(tablix.ColumnMembers.Count(member => member.Kind == ReportTablixMemberKind.Group) >= 2);
        Assert.Equal(
            EnumerateTablixMemberIds(tablix).Count,
            EnumerateTablixMemberIds(tablix).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var initialRowCount = tablix.Rows.Count;
        var initialColumnCount = tablix.Columns.Count;
        Execute(viewModel.AddRowTotalCommand);
        Execute(viewModel.AddColumnTotalCommand);

        Assert.Equal(initialRowCount + 1, tablix.Rows.Count);
        Assert.Equal("Total", tablix.Rows[^1].Cells[0].Text);
        Assert.Contains(tablix.Rows[^1].Cells, cell => string.Equals(cell.ValueExpression, "Sum(Fields.Revenue)", StringComparison.Ordinal));

        Assert.Equal(initialColumnCount + 1, tablix.Columns.Count);
        Assert.Equal("Total", tablix.Rows[0].Cells[^1].Text);
        Assert.Equal(string.Empty, tablix.Rows[1].Cells[^1].Text);
        Assert.Equal("Sum(Fields.Revenue)", tablix.Rows[^1].Cells[^1].ValueExpression);
        Assert.True(viewModel.IsPreviewDirty);
    }

    [AvaloniaFact]
    public async Task Designer_TemplateWorkspaceUsesSelectedDataNodeAndResolvesPreview()
    {
        using var viewModel = new ReportDesignerViewModel(CreateTemplateWorkspaceSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems, item => item.Item is DocumentTemplateItem);
        viewModel.SelectedInspectorTabIndex = 4;

        Assert.True(viewModel.HasSelectedTemplateItem);
        Assert.True(viewModel.ShowTemplateBindingEditor);
        Assert.True(viewModel.ShowTemplateReferenceEditor);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.DataSet && node.Title == "sales");
        await ExecuteAsync(viewModel.RefreshSelectedDataSetFieldsCommand);

        viewModel.SelectedDataNode = Assert.Single(
            EnumerateDataNodes(viewModel.ReportDataNodes),
            node => node.Kind == ReportDesignerDataNodeKind.QueryField && node.Title == "Quarter");

        Execute(viewModel.UseSelectedDataNodeInTemplateCommand);

        var templateItem = Assert.IsType<DocumentTemplateItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.Contains("Quarter", templateItem.Bindings.Keys);

        var sharedTemplate = Assert.Single(viewModel.ReportDefinition.SharedTemplates, template => template.Id == "narrative");
        Assert.Contains("{{Quarter}}", sharedTemplate.Content, StringComparison.Ordinal);

        await ExecuteAsync(viewModel.RefreshTemplatePreviewCommand);

        Assert.Contains("FY26 Revenue Review", viewModel.TemplatePreviewText, StringComparison.Ordinal);
        Assert.Contains("Q1", viewModel.TemplatePreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void Designer_TemplateWorkspaceCanDetachAndPromoteTemplateItems()
    {
        using var viewModel = new ReportDesignerViewModel(CreateTemplateWorkspaceSource());
        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems, item => item.Item is DocumentTemplateItem);

        var templateItem = Assert.IsType<DocumentTemplateItem>(viewModel.SelectedCanvasItem!.Item);
        Assert.True(viewModel.CanEditReferencedSharedTemplate);
        Assert.True(viewModel.CanDetachSelectedTemplateItem);

        Execute(viewModel.EditReferencedSharedTemplateCommand);
        Assert.NotNull(viewModel.SelectedSharedTemplateEntry);
        Assert.Equal("narrative", viewModel.SelectedSharedTemplateEntry!.Title);

        viewModel.SelectedCanvasItem = Assert.Single(viewModel.DesignItems, item => item.Item is DocumentTemplateItem);
        Execute(viewModel.DetachSelectedTemplateItemCommand);

        Assert.Null(templateItem.TemplateId);
        Assert.Contains("{{Title}}", templateItem.EmbeddedContent ?? string.Empty, StringComparison.Ordinal);
        Assert.True(viewModel.CanPromoteSelectedTemplateItemToShared);

        var templateCountBeforePromotion = viewModel.ReportDefinition.SharedTemplates.Count;
        Execute(viewModel.PromoteSelectedTemplateItemToSharedCommand);

        Assert.NotNull(templateItem.TemplateId);
        Assert.Null(templateItem.EmbeddedContent);
        Assert.True(viewModel.ReportDefinition.SharedTemplates.Count > templateCountBeforePromotion);
    }

    [AvaloniaFact]
    public async Task DesignerControl_UsesWysiwygSurfaceInsteadOfPlaceholderButtons()
    {
        using var viewModel = new ReportDesignerViewModel(CreateNestedDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1600,
            Height = 980
        };
        var window = new Window
        {
            Width = 1640,
            Height = 1024,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Contains(viewModel.DesignItems, item => string.Equals(item.ContentText, "Sales overview", StringComparison.Ordinal));
        Assert.DoesNotContain(control.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("designer-canvas-item"));
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerSurfaceCanvasControl>().FirstOrDefault());

        window.Close();
    }

    [AvaloniaFact]
    public async Task Designer_ViewControlsAdjustZoomWithoutDirtyingPreview()
    {
        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1600,
            Height = 980
        };
        var window = new Window
        {
            Width = 1640,
            Height = 1024,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });
        await viewModel.RefreshPreviewAsync();

        Assert.False(viewModel.IsPreviewDirty);
        var initialScaledWidth = viewModel.SurfaceScaledWidth;

        viewModel.ShowRulers = false;
        Assert.False(viewModel.IsPreviewDirty);
        Assert.Equal("Hid design rulers.", viewModel.StatusMessage);

        Execute(viewModel.ZoomInCommand);
        Assert.False(viewModel.IsPreviewDirty);
        Assert.True(viewModel.SurfaceZoomFactor > 1d);
        Assert.True(viewModel.SurfaceScaledWidth > initialScaledWidth);

        Execute(viewModel.ActualSizeCommand);
        Assert.False(viewModel.IsPreviewDirty);
        Assert.Equal(1d, viewModel.SurfaceZoomFactor, 3);

        Execute(viewModel.FitPageCommand);
        Assert.False(viewModel.IsPreviewDirty);
        Assert.True(viewModel.SurfaceZoomFactor < 1d);

        window.Close();
    }

    [Fact]
    public void Designer_SurfaceModelIncludesNestedAndReferencedSubreportItems()
    {
        using var viewModel = new ReportDesignerViewModel(CreateNestedDesignerSource());

        var nestedContainerItem = Assert.Single(viewModel.DesignItems, item => item.Item.Name == "Panel Title");
        var nestedSubreportItem = Assert.Single(viewModel.DesignItems, item => item.Item.Name == "Regional Caption");

        Assert.False(nestedContainerItem.IsReadOnly);
        Assert.Equal(58d, nestedContainerItem.Left, 3);
        Assert.Equal(56d, nestedContainerItem.Top, 3);

        Assert.True(nestedSubreportItem.IsReadOnly);
        Assert.Equal(412d, nestedSubreportItem.Left, 3);
        Assert.Equal(74d, nestedSubreportItem.Top, 3);
    }

    [AvaloniaFact]
    public async Task Designer_CapturesCanvasFirstBaselines_WhenRequested()
    {
        var screenshotRoot = Environment.GetEnvironmentVariable("AVALONIA_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotRoot))
        {
            return;
        }

        Directory.CreateDirectory(screenshotRoot);

        using var viewModel = new ReportDesignerViewModel(CreateDesignerSource());
        var control = new ReportDesignerControl
        {
            DataContext = viewModel,
            Width = 1600,
            Height = 980
        };
        var window = new Window
        {
            Width = 1680,
            Height = 1024,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        SaveFrame(window, screenshotRoot, "designer-default-design.png");

        Execute(viewModel.OpenReportDataPaneCommand);
        Execute(viewModel.TogglePinLeftDrawerCommand);
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        SaveFrame(window, screenshotRoot, "designer-left-drawer-pinned.png");

        Assert.True(File.Exists(Path.Combine(screenshotRoot, "designer-default-design.png")));
        Assert.True(File.Exists(Path.Combine(screenshotRoot, "designer-left-drawer-pinned.png")));

        window.Close();
    }

    private static void Execute(ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> command)
    {
        using var subscription = command.Execute().Subscribe();
    }

    private static async Task ExecuteAsync(ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> command)
    {
        using var subscription = command.Execute().Subscribe();
        await Dispatcher.UIThread.InvokeAsync(() => { });
        await Task.Delay(50);
    }

    private static IEnumerable<ReportDesignerDataNodeViewModel> EnumerateDataNodes(IEnumerable<ReportDesignerDataNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateDataNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static void SaveFrame(Window window, string screenshotRoot, string fileName)
    {
        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        Assert.NotNull(frame);
        var path = Path.Combine(screenshotRoot, fileName);
        frame!.Save(path);
    }

    private static ReportViewerSource CreateDesignerSource()
    {
        var report = new ReportDefinition
        {
            Id = "designer-report",
            Name = "Designer Sample",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Title",
                    DisplayName = "Title",
                    Prompt = "Title",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'Designer Preview'",
                    Visibility = ReportParameterVisibility.Visible
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "title",
                            Name = "Title",
                            ValueExpression = "Parameters.Title",
                            Bounds = new ReportItemBounds(40f, 40f, 320f, 40f)
                        }
                    }
                }
            }
        };

        return new ReportViewerSource
        {
            ReportDefinition = report
        };
    }

    private static ReportViewerSource CreateNestedDesignerSource()
    {
        var detailReport = new ReportDefinition
        {
            Id = "regional-detail",
            Name = "Regional Detail",
            Sections =
            {
                new ReportSection
                {
                    Id = "detail",
                    Name = "Detail",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "caption",
                            Name = "Regional Caption",
                            StaticText = "Regional detail",
                            Bounds = new ReportItemBounds(12f, 14f, 160f, 24f)
                        }
                    }
                }
            }
        };

        var report = new ReportDefinition
        {
            Id = "wysiwyg-sample",
            Name = "WYSIWYG Sample",
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "summary",
                            Name = "Summary",
                            StaticText = "Sales overview",
                            Bounds = new ReportItemBounds(40f, 24f, 220f, 32f)
                        },
                        new ContainerItem
                        {
                            Id = "panel",
                            Name = "Panel",
                            Bounds = new ReportItemBounds(40f, 40f, 280f, 120f),
                            Items =
                            {
                                new TextItem
                                {
                                    Id = "panel-title",
                                    Name = "Panel Title",
                                    StaticText = "North division",
                                    Bounds = new ReportItemBounds(18f, 16f, 160f, 24f)
                                }
                            }
                        },
                        new SubreportItem
                        {
                            Id = "detail-host",
                            Name = "Detail Host",
                            ReportReferenceId = "regional-detail",
                            Bounds = new ReportItemBounds(400f, 60f, 220f, 120f)
                        }
                    }
                }
            }
        };

        var source = new ReportViewerSource
        {
            ReportDefinition = report
        };
        source.ReferencedReports[detailReport.Id] = detailReport;
        return source;
    }

    private static ReportViewerSource CreateSelectionGeometrySource()
    {
        var report = new ReportDefinition
        {
            Id = "selection-geometry",
            Name = "Selection Geometry",
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new LineItem
                        {
                            Id = "divider",
                            Name = "Divider",
                            Bounds = new ReportItemBounds(40f, 96f, 240f, 1f),
                            X2 = 280f,
                            Y2 = 97f
                        },
                        new TextItem
                        {
                            Id = "tiny-caption",
                            Name = "Tiny Caption",
                            StaticText = "FY26",
                            Bounds = new ReportItemBounds(48f, 132f, 12f, 12f)
                        }
                    }
                }
            }
        };

        return new ReportViewerSource
        {
            ReportDefinition = report
        };
    }

    private static ReportViewerSource CreateDataWorkspaceSource()
    {
        var report = new ReportDefinition
        {
            Id = "data-workspace",
            Name = "Data Workspace",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Region",
                    DisplayName = "Region",
                    Prompt = "Region",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'West'",
                    Visibility = ReportParameterVisibility.Visible
                }
            },
            DataSources =
            {
                new ReportDataSourceDefinition
                {
                    Id = "sales-source",
                    ProviderId = ReportProviderIds.InMemory,
                    Options =
                    {
                        ["sourceKey"] = "sales"
                    }
                }
            },
            DataSets =
            {
                new ReportDataSetDefinition
                {
                    Id = "sales",
                    DataSourceId = "sales-source"
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "headline",
                            Name = "Headline",
                            StaticText = "Summary",
                            Bounds = new ReportItemBounds(40f, 40f, 220f, 36f)
                        }
                    }
                }
            }
        };

        var hostDataRegistry = new ReportHostDataRegistry();
        hostDataRegistry.RegisterInMemorySource(
            "sales",
            new ReportDictionaryDataSource(
                new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "West",
                        ["Revenue"] = 1500000m,
                        ["Quarter"] = "Q1"
                    },
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Region"] = "East",
                        ["Revenue"] = 1200000m,
                        ["Quarter"] = "Q2"
                    }
                }));

        return new ReportViewerSource
        {
            ReportDefinition = report,
            ProviderRegistry = ReportDataProviders.CreateDefaultRegistry(),
            HostDataRegistry = hostDataRegistry
        };
    }

    private static ReportViewerSource CreateTemplateWorkspaceSource()
    {
        var source = CreateDataWorkspaceSource();
        source.ReportDefinition.Parameters.Insert(
            0,
            new ReportParameterDefinition
            {
                Id = "Title",
                DisplayName = "Report Title",
                Prompt = "Report Title",
                DataType = ReportParameterDataType.String,
                DefaultValueExpression = "'FY26 Revenue Review'",
                Visibility = ReportParameterVisibility.Visible
            });

        source.ReportDefinition.SharedTemplates.Add(new ReportSharedTemplateDefinition
        {
            Id = "narrative",
            Format = ReportDocumentTemplateFormat.Markdown,
            IsEmbedded = true,
            Content = "# {{Title}}\n\nQuarter {{Quarter}}"
        });

        source.ReportDefinition.Sections[0].BodyItems.Add(new DocumentTemplateItem
        {
            Id = "template1",
            Name = "Narrative Block",
            TemplateId = "narrative",
            TemplateFormat = ReportDocumentTemplateFormat.Markdown,
            Bounds = new ReportItemBounds(40f, 100f, 320f, 160f),
            Bindings =
            {
                ["Title"] = "Parameters.Title"
            }
        });

        return source;
    }

    private static ReportViewerSource CreateResourceWorkspaceSource()
    {
        var source = CreateDataWorkspaceSource();
        source.ReportDefinition.Sections[0].BodyItems.Add(new ImageItem
        {
            Id = "logo",
            Name = "Company Logo",
            SourceKind = ReportImageSourceKind.Uri,
            ValueExpression = "'https://cdn.contoso.test/logo.png'",
            Bounds = new ReportItemBounds(40f, 96f, 180f, 96f)
        });

        return source;
    }

    private static ReportViewerSource CreateParameterLayoutSource()
    {
        var report = new ReportDefinition
        {
            Id = "parameter-layout",
            Name = "Parameter Layout",
            ParameterLayout =
            {
                ColumnCount = 2,
                RowCount = 2,
                Cells =
                {
                    new ReportParameterLayoutCellDefinition
                    {
                        ParameterId = "Title",
                        RowIndex = 0,
                        ColumnIndex = 0
                    },
                    new ReportParameterLayoutCellDefinition
                    {
                        ParameterId = "Region",
                        RowIndex = 1,
                        ColumnIndex = 0
                    }
                }
            },
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Title",
                    DisplayName = "Report Title",
                    Prompt = "Report Title",
                    DataType = ReportParameterDataType.String,
                    Visibility = ReportParameterVisibility.Visible
                },
                new ReportParameterDefinition
                {
                    Id = "Region",
                    DisplayName = "Region",
                    Prompt = "Focus region",
                    DataType = ReportParameterDataType.String,
                    Visibility = ReportParameterVisibility.Visible
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main"
                }
            }
        };

        return new ReportViewerSource
        {
            ReportDefinition = report
        };
    }

    private static ReportViewerSource CreateChartDesignerSource()
    {
        var source = CreateDataWorkspaceSource();
        source.ReportDefinition.Sections[0].BodyItems.Clear();
        source.ReportDefinition.Sections[0].BodyItems.Add(new ChartItem
        {
            Id = "chart1",
            Name = "Revenue Chart",
            DataSetId = "sales",
            Bounds = new ReportItemBounds(80f, 72f, 360f, 220f)
        });
        return source;
    }

    private static ReportViewerSource CreateGroupingDesignerSource()
    {
        var source = CreateDataWorkspaceSource();
        source.ReportDefinition.Sections[0].BodyItems.Clear();
        source.ReportDefinition.Sections[0].BodyItems.Add(new TablixItem
        {
            Id = "sales-tablix",
            Name = "Sales Matrix",
            DataSetId = "sales",
            Bounds = new ReportItemBounds(60f, 72f, 520f, 240f),
            Columns =
            {
                new ReportTablixColumnDefinition { Id = "category", Width = 180f },
                new ReportTablixColumnDefinition { Id = "quarter", Width = 140f },
                new ReportTablixColumnDefinition { Id = "revenue", Width = 160f }
            },
            Rows =
            {
                new ReportTablixRowDefinition
                {
                    Id = "header",
                    IsHeader = true,
                    Cells =
                    {
                        new ReportTablixCellDefinition { Text = "Region" },
                        new ReportTablixCellDefinition { Text = "Quarter" },
                        new ReportTablixCellDefinition { Text = "Revenue" }
                    }
                },
                new ReportTablixRowDefinition
                {
                    Id = "detail",
                    Cells =
                    {
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Region" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Quarter" },
                        new ReportTablixCellDefinition { ValueExpression = "Fields.Revenue", FormatString = "N0" }
                    }
                }
            }
        });

        return source;
    }

    private static List<string> EnumerateTablixMemberIds(TablixItem tablix)
    {
        var ids = new List<string>();
        CollectMemberIds(tablix.RowMembers, ids);
        CollectMemberIds(tablix.ColumnMembers, ids);
        return ids;
    }

    private static void CollectMemberIds(
        IReadOnlyList<ReportTablixMemberDefinition> members,
        ICollection<string> ids)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            ids.Add(member.Id);
            CollectMemberIds(member.Members, ids);
        }
    }

    private sealed class PreviewFailureSessionService : IReportViewerSessionService
    {
        public ValueTask<ReportViewerParameterResolutionResult> ResolveParametersAsync(
            ReportViewerSource source,
            IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = new ReportViewerParameterResolutionResult();
            foreach (var definition in source.ReportDefinition.Parameters)
            {
                result.Parameters.Add(new ReportViewerParameterState
                {
                    Definition = definition
                });
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<ReportViewerExecutionSnapshot> ExecuteAsync(
            ReportViewerSource source,
            IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Preview execution failed.");
        }

        public ValueTask<ReportExportResult> ExportAsync(
            ReportViewerExecutionSnapshot snapshot,
            ReportExportRequest request,
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ReportExportResult());
        }

        public ValueTask<PrintJobResult> PrintAsync(
            ReportViewerExecutionSnapshot snapshot,
            PrintSettings settings,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(PrintJobResult.Success(settings.OutputPath));
        }
    }
}
