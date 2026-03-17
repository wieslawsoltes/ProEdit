using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
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
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerReportDataPane>().FirstOrDefault());
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerGroupingPane>().FirstOrDefault());
        Assert.NotNull(control.GetVisualDescendants().OfType<ReportDesignerPropertiesPane>().FirstOrDefault());

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

    private static void Execute(ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> command)
    {
        using var subscription = command.Execute().Subscribe();
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
