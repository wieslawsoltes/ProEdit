using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reactive;
using System.Text;
using ReactiveUI;
using Vibe.Office.Documents;
using Vibe.Office.Reporting;
using Vibe.Office.Reporting.Avalonia.Designer;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Rdl;
using Vibe.Office.Reporting.Serialization;
using Vibe.Reporting.App.Controls;
using Vibe.Reporting.App.Services;
using Vibe.Reporting.App.ViewModels;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Vibe.Reporting.App.Headless.Tests.HeadlessTestAppBuilder))]

namespace Vibe.Reporting.App.Headless.Tests;

public sealed class HeadlessTestApp : Application
{
}

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }
}

public sealed class ReportingStudioAppTests
{
    private static readonly string SampleCorpusPath = ResolveSampleCorpusPath();

    [Fact]
    public async Task DocumentService_OpenNative_LoadsSiblingReferencedReports()
    {
        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var sampleWorkspace = workspaceFactory.CreateSampleWorkspace();

        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sales-overview.vreport.json");
        var detailPath = Path.Combine(Path.GetDirectoryName(rootPath)!, "regional-detail.vreport.json");

        try
        {
            await service.SaveNativeAsync(sampleWorkspace.Source.ReportDefinition, rootPath);
            await service.SaveNativeAsync(sampleWorkspace.Source.ReferencedReports["regional-detail"], detailPath);

            var opened = await service.OpenNativeAsync(rootPath);

            Assert.NotNull(opened.Workspace);
            Assert.Contains("regional-detail", opened.Workspace!.Source.ReferencedReports.Keys);
            Assert.False(opened.HasErrors);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(rootPath)!);
        }
    }

    [AvaloniaFact]
    public async Task ViewModel_Initialize_SaveAndExport_FlowWorks()
    {
        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var outputRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var picker = new StubReportingStudioFilePickerService
        {
            SaveTemplatePath = Path.Combine(outputRoot, "studio-sample"),
            ExportRdlPath = Path.Combine(outputRoot, "studio-sample-export")
        };

        try
        {
            using var viewModel = new ReportingStudioViewModel(picker, service);
            await viewModel.InitializeAsync();

            Assert.NotNull(viewModel.ViewerViewModel.CurrentSnapshot);
            Assert.False(viewModel.DesignerViewModel.IsPreviewDirty);

            await ExecuteAsync(viewModel.SaveTemplateAsCommand);
            await ExecuteAsync(viewModel.ExportRdlCommand);

            Assert.True(File.Exists(Path.Combine(outputRoot, "studio-sample.vreport.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "studio-sample-export.rdl")));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_RendersStudioShell()
    {
        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        using var viewModel = new ReportingStudioViewModel(new StubReportingStudioFilePickerService(), service);
        await viewModel.InitializeAsync();

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.NotNull(window.GetVisualDescendants().OfType<ReportingStudioHeaderBar>().SingleOrDefault());
        Assert.NotNull(window.GetVisualDescendants().OfType<ReportingStudioCommandBar>().SingleOrDefault());
        Assert.Empty(window.GetVisualDescendants().OfType<ReportingStudioSidebar>());
        Assert.NotNull(window.GetVisualDescendants().OfType<ReportingStudioStatusStrip>().SingleOrDefault());
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => string.Equals(text.Text, "VibeOffice Reporting", StringComparison.Ordinal));
        Assert.NotNull(viewModel.ViewerViewModel.CurrentSnapshot);
        Assert.NotEmpty(viewModel.ViewerViewModel.Pages);

        window.Close();
    }

    [Fact]
    public async Task SampleCorpus_ImportsExecutesAndRoundTrips_WhenAvailable()
    {
        if (!Directory.Exists(SampleCorpusPath))
        {
            return;
        }

        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var viewerService = new ReportViewerSessionService();
        var serializer = new ReportRdlSerializer();

        foreach (var path in Directory.EnumerateFiles(SampleCorpusPath, "*.rdl").OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            var opened = await service.ImportRdlAsync(path);
            var workspace = Assert.IsType<ReportingStudioWorkspace>(opened.Workspace);
            Assert.Empty(opened.Diagnostics);

            var suppliedParameters = await ResolveSampleParametersAsync(viewerService, workspace.Source);
            var snapshot = await viewerService.ExecuteAsync(workspace.Source, suppliedParameters);

            Assert.NotNull(snapshot.ExecutionResult.MaterializedReport);
            Assert.NotNull(snapshot.ExecutionResult.Document);
            Assert.NotEmpty(snapshot.PreviewPages);
            Assert.Empty(snapshot.ExecutionResult.Diagnostics);

            var writeResult = serializer.Write(workspace.Source.ReportDefinition);
            Assert.Empty(writeResult.Diagnostics);

            var readResult = serializer.Read(writeResult.Xml);
            Assert.NotNull(readResult.ReportDefinition);
            Assert.Empty(readResult.Diagnostics);
        }
    }

    [Fact]
    public async Task SampleCorpus_InvoicePreservesNestedContainerLayout_WhenAvailable()
    {
        var invoicePath = Path.Combine(SampleCorpusPath, "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var viewerService = new ReportViewerSessionService();

        var opened = await service.ImportRdlAsync(invoicePath);
        var workspace = Assert.IsType<ReportingStudioWorkspace>(opened.Workspace);
        var parameterResolution = await viewerService.ResolveParametersAsync(workspace.Source, new Dictionary<string, ReportParameterValue>());
        var companyParameter = Assert.Single(parameterResolution.Parameters);
        Assert.Equal("Company", companyParameter.Definition.Id);
        Assert.True(companyParameter.ResolvedValue is null || companyParameter.ResolvedValue.IsNull);
        Assert.Equal(2, companyParameter.AvailableValues.Count);
        var suppliedParameters = await ResolveSampleParametersAsync(viewerService, workspace.Source);
        var snapshot = await viewerService.ExecuteAsync(workspace.Source, suppliedParameters);

        var unexpectedDefinitionShapes = EnumerateReportItems(workspace.Source.ReportDefinition.Sections[0].BodyItems)
            .Concat(EnumerateReportItems(workspace.Source.ReportDefinition.Sections[0].HeaderItems))
            .Concat(EnumerateReportItems(workspace.Source.ReportDefinition.Sections[0].FooterItems))
            .OfType<ShapeItem>()
            .Select(static item => item.Id)
            .ToArray();
        Assert.Empty(unexpectedDefinitionShapes);
        var definitionOtherInformation = Assert.IsType<ContainerItem>(
            EnumerateReportItems(workspace.Source.ReportDefinition.Sections[0].BodyItems)
                .Single(item => string.Equals(item.Id, "Rectangle24", StringComparison.Ordinal)));
        Assert.NotEmpty(definitionOtherInformation.Items);
        var invoiceDateText = Assert.IsType<TextItem>(
            EnumerateReportItems(workspace.Source.ReportDefinition.Sections[0].BodyItems)
                .Single(item => string.Equals(item.Id, "Textbox8", StringComparison.Ordinal)));
        Assert.Equal("d MMMM yyyy", invoiceDateText.FormatString);

        var materializedReport = Assert.IsType<MaterializedReport>(snapshot.ExecutionResult.MaterializedReport);
        var unexpectedMaterializedShapes = EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
            .Concat(EnumerateMaterializedReportItems(materializedReport.Sections[0].HeaderItems))
            .Concat(EnumerateMaterializedReportItems(materializedReport.Sections[0].FooterItems))
            .OfType<MaterializedShapeReportItem>()
            .Select(static item => item.SourceItemId)
            .ToArray();
        Assert.Empty(unexpectedMaterializedShapes);
        var materializedOtherInformation = Assert.IsType<MaterializedContainerReportItem>(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Single(item => string.Equals(item.SourceItemId, "Rectangle24", StringComparison.Ordinal)));
        Assert.NotEmpty(materializedOtherInformation.Items);
        var materializedInvoiceBanner = Assert.IsType<MaterializedContainerReportItem>(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Single(item => string.Equals(item.SourceItemId, "Rectangle3", StringComparison.Ordinal)));
        var materializedPaymentPanel = Assert.IsType<MaterializedContainerReportItem>(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Single(item => string.Equals(item.SourceItemId, "Rectangle5", StringComparison.Ordinal)));
        Assert.False(string.IsNullOrWhiteSpace(materializedInvoiceBanner.Style?.Background));
        Assert.False(string.IsNullOrWhiteSpace(materializedPaymentPanel.Style?.Background));
        var materializedInvoiceTitle = Assert.IsType<MaterializedTextReportItem>(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Single(item => string.Equals(item.SourceItemId, "Textbox7", StringComparison.Ordinal)));
        Assert.Equal("White", materializedInvoiceTitle.Style?.Foreground);
        var materializedPaymentInstructions = Assert.IsType<MaterializedTextReportItem>(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Single(item => string.Equals(item.SourceItemId, "Textbox40", StringComparison.Ordinal)));
        Assert.True(materializedPaymentInstructions.Style?.FontSize is > 11f and < 11.5f);
        Assert.Contains("Write reference US-009 on the check.", materializedPaymentInstructions.Text, StringComparison.Ordinal);
        var materializedLineItems = Assert.IsType<MaterializedTablixReportItem>(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Single(item => string.Equals(item.SourceItemId, "Tablix2", StringComparison.Ordinal)));
        Assert.True(materializedLineItems.Rows.Count >= 11, $"Expected invoice line-items tablix to expand to all detail rows, but got {materializedLineItems.Rows.Count} rows.");
        Assert.True(materializedLineItems.Rows[0].IsHeader);
        Assert.False(materializedLineItems.Rows[1].IsHeader);
        Assert.Equal("White", materializedLineItems.Rows[1].Cells[0].Style?.Background);
        Assert.Equal(materializedPaymentPanel.Style?.Background, materializedLineItems.Rows[2].Cells[0].Style?.Background);
        Assert.Equal("White", materializedLineItems.Rows[3].Cells[0].Style?.Background);
        Assert.DoesNotContain(
            EnumerateMaterializedReportItems(materializedReport.Sections[0].BodyItems)
                .Concat(EnumerateMaterializedReportItems(materializedReport.Sections[0].HeaderItems))
                .Concat(EnumerateMaterializedReportItems(materializedReport.Sections[0].FooterItems))
                .OfType<MaterializedTextReportItem>()
                .Select(static item => item.Text),
            static text => text.Contains("12:00:00", StringComparison.Ordinal));

        var document = Assert.IsType<Document>(snapshot.ExecutionResult.Document);
        Assert.Equal("Segoe UI", document.DefaultTextStyle.FontFamily);
        Assert.NotNull(snapshot.Layout);
        var layout = snapshot.Layout!;
        Assert.Single(snapshot.PreviewPages);
        Assert.Single(layout.Pages);
        Assert.Contains(document.Blocks, static block => block is TableBlock);
        var invoiceTable = EnumerateShapeTextBlocks(document)
            .OfType<TableBlock>()
            .OrderByDescending(static table => table.Rows.Count)
            .First();
        Assert.Equal(11, invoiceTable.Rows.Count);
        Assert.NotEqual(invoiceTable.Rows[1].Cells[0].Properties.ShadingColor, invoiceTable.Rows[2].Cells[0].Properties.ShadingColor);
        Assert.Equal(invoiceTable.Rows[1].Cells[0].Properties.ShadingColor, invoiceTable.Rows[3].Cells[0].Properties.ShadingColor);
        Assert.Contains(EnumerateShapeInlines(document), static shape => shape.TextBox is { Blocks.Count: > 0 });
        Assert.Contains(
            EnumerateShapeInlines(document),
            static shape => string.Equals(shape.Name, "Rectangle3", StringComparison.Ordinal)
                            && shape.Properties.Fill is ShapeSolidFill);
        Assert.Contains(
            EnumerateShapeInlines(document),
            static shape => string.Equals(shape.Name, "Rectangle5", StringComparison.Ordinal)
                            && shape.Properties.Fill is ShapeSolidFill);
        Assert.Contains(EnumerateShapeTextBlocks(document), static block => block is TableBlock);
        Assert.Contains(
            EnumerateShapeTextParagraphs(document),
            static paragraph => paragraph.FloatingObjects.Count > 0);
        var documentText = CollectDocumentText(document);
        Assert.Contains("METHODS OF PAYMENT", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lens", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Projector Television", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Surround Sound Receiver", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write reference", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("US-009", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(layout.HeaderFooters);
        Assert.NotEmpty(layout.HeaderFooters[0].HeaderLines);
        Assert.NotEmpty(layout.HeaderFooters[0].FooterLines);
        Assert.True(layout.HeaderFooters[0].HeaderLines.Max(static line => line.Y) < 200f);
        Assert.True(layout.HeaderFooters[0].FooterLines.Min(static line => line.Y) > 900f);
        Assert.Contains(
            layout.HeaderFooters[0].FloatingObjects,
            static floating => floating.Bounds.Y > 900f);
        Assert.True(workspace.Source.ReportDefinition.ConsumeContainerWhitespace);
        Assert.Contains(EnumerateReportItems(workspace.Source.ReportDefinition.Sections[0].BodyItems), static item => item.KeepTogether);
    }

    [AvaloniaFact]
    public async Task SampleCorpus_InvoiceViewer_CapturesPreview_WhenRequested()
    {
        var invoicePath = Path.Combine(SampleCorpusPath, "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var screenshotRoot = Environment.GetEnvironmentVariable("AVALONIA_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotRoot))
        {
            return;
        }

        Directory.CreateDirectory(screenshotRoot);

        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var viewerViewModel = new ReportViewerViewModel(new ReportViewerSessionService());

        var opened = await service.ImportRdlAsync(invoicePath);
        var workspace = Assert.IsType<ReportingStudioWorkspace>(opened.Workspace);
        await viewerViewModel.LoadAsync(workspace.Source);

        var window = new Window
        {
            Width = 1600,
            Height = 1100,
            Content = new ReportViewerControl
            {
                DataContext = viewerViewModel
            }
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(static () => { });

        var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        Assert.NotNull(frame);
        var path = Path.Combine(screenshotRoot, "invoice-viewer-preview.png");
        frame!.Save(path);

        Assert.True(File.Exists(path));
        window.Close();
        viewerViewModel.Dispose();
    }

    [Fact]
    public async Task SampleCorpus_RegionalSalesPreservesGroupPageBreaks_WhenAvailable()
    {
        var regionalSalesPath = Path.Combine(SampleCorpusPath, "RegionalSales.rdl");
        if (!File.Exists(regionalSalesPath))
        {
            return;
        }

        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var serializer = new ReportRdlSerializer();

        var opened = await service.ImportRdlAsync(regionalSalesPath);
        var workspace = Assert.IsType<ReportingStudioWorkspace>(opened.Workspace);

        var tablix = Assert.IsType<TablixItem>(workspace.Source.ReportDefinition.Sections[0].BodyItems.Single(item => item is TablixItem));
        var groupingMember = Assert.Single(tablix.RowMembers);
        Assert.Equal(ReportPageBreakLocation.Between, groupingMember.PageBreak?.Location);

        var writeResult = serializer.Write(workspace.Source.ReportDefinition);
        Assert.Empty(writeResult.Diagnostics);
        Assert.Contains("<BreakLocation>Between</BreakLocation>", writeResult.Xml, StringComparison.Ordinal);
    }

    private static ReportingStudioDocumentService CreateDocumentService(ReportingStudioWorkspaceFactory workspaceFactory)
    {
        return new ReportingStudioDocumentService(
            new ReportTemplateSerializer(),
            new ReportRdlSerializer(),
            workspaceFactory);
    }

    private static async Task<Dictionary<string, ReportParameterValue>> ResolveSampleParametersAsync(
        ReportViewerSessionService viewerService,
        ReportViewerSource source)
    {
        var supplied = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);
        var resolution = await viewerService.ResolveParametersAsync(source, supplied);
        for (var index = 0; index < resolution.Parameters.Count; index++)
        {
            var parameter = resolution.Parameters[index];
            if (parameter.ResolvedValue is not null && !parameter.ResolvedValue.IsNull)
            {
                supplied[parameter.Definition.Id] = parameter.ResolvedValue;
                continue;
            }

            if (parameter.AvailableValues.Count > 0)
            {
                supplied[parameter.Definition.Id] = ReportParameterValue.FromScalar(parameter.AvailableValues[0].Value);
                continue;
            }

            supplied[parameter.Definition.Id] = parameter.Definition.DataType switch
            {
                ReportParameterDataType.Integer => ReportParameterValue.FromScalar(1),
                ReportParameterDataType.Number => ReportParameterValue.FromScalar(1d),
                ReportParameterDataType.Boolean => ReportParameterValue.FromScalar(true),
                ReportParameterDataType.DateTime => ReportParameterValue.FromScalar(new DateTime(2026, 3, 17, 12, 0, 0, DateTimeKind.Utc)),
                _ => ReportParameterValue.FromScalar("Sample")
            };
        }

        return supplied;
    }

    private static Task ExecuteAsync(ReactiveCommand<Unit, Unit> command)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? subscription = null;
        subscription = command.Execute().Subscribe(
            _ => { },
            ex =>
            {
                subscription?.Dispose();
                tcs.TrySetException(ex);
            },
            () =>
            {
                subscription?.Dispose();
                tcs.TrySetResult();
            });

        return tcs.Task;
    }

    private static IEnumerable<ReportItem> EnumerateReportItems(IEnumerable<ReportItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is ContainerItem container)
            {
                foreach (var child in EnumerateReportItems(container.Items))
                {
                    yield return child;
                }
            }

            if (item is TablixItem tablix)
            {
                for (var rowIndex = 0; rowIndex < tablix.Rows.Count; rowIndex++)
                {
                    var row = tablix.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var content = row.Cells[cellIndex].ContentItem;
                        if (content is null)
                        {
                            continue;
                        }

                        foreach (var nested in EnumerateReportItems(new[] { content }))
                        {
                            yield return nested;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<MaterializedReportItem> EnumerateMaterializedReportItems(IEnumerable<MaterializedReportItem> items)
    {
        foreach (var item in items)
        {
            yield return item;

            if (item is MaterializedContainerReportItem container)
            {
                foreach (var child in EnumerateMaterializedReportItems(container.Items))
                {
                    yield return child;
                }
            }

            if (item is MaterializedTablixReportItem tablix)
            {
                for (var rowIndex = 0; rowIndex < tablix.Rows.Count; rowIndex++)
                {
                    var row = tablix.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        if (row.Cells[cellIndex].Content is { } content)
                        {
                            foreach (var nested in EnumerateMaterializedReportItems(new[] { content }))
                            {
                                yield return nested;
                            }
                        }
                    }
                }
            }

            if (item is MaterializedSubreportReportItem subreport)
            {
                if (subreport.Report is null)
                {
                    continue;
                }

                foreach (var section in subreport.Report.Sections)
                {
                    foreach (var nested in EnumerateMaterializedReportItems(section.BodyItems))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }


    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static IEnumerable<ShapeInline> EnumerateShapeInlines(Document document)
    {
        foreach (var block in document.Blocks)
        {
            foreach (var shape in EnumerateShapeInlines(block))
            {
                yield return shape;
            }
        }
    }

    private static IEnumerable<Block> EnumerateShapeTextBlocks(Document document)
    {
        foreach (var shape in EnumerateShapeInlines(document))
        {
            if (shape.TextBox is null)
            {
                continue;
            }

            foreach (var block in shape.TextBox.Blocks)
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<ParagraphBlock> EnumerateShapeTextParagraphs(Document document)
    {
        foreach (var block in EnumerateShapeTextBlocks(document))
        {
            if (block is ParagraphBlock paragraph)
            {
                yield return paragraph;
            }
        }
    }

    private static IEnumerable<ShapeInline> EnumerateShapeInlines(Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    foreach (var shape in EnumerateShapeInlines(inline))
                    {
                        yield return shape;
                    }
                }

                foreach (var floating in paragraph.FloatingObjects)
                {
                    foreach (var shape in EnumerateShapeInlines(floating.Content))
                    {
                        yield return shape;
                    }
                }

                break;

            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var paragraphInCell in cell.Paragraphs)
                        {
                            foreach (var shape in EnumerateShapeInlines(paragraphInCell))
                            {
                                yield return shape;
                            }
                        }
                    }
                }

                break;
        }
    }

    private static IEnumerable<ShapeInline> EnumerateShapeInlines(Inline inline)
    {
        if (inline is ShapeInline shape)
        {
            yield return shape;

            if (shape.TextBox is not null)
            {
                foreach (var block in shape.TextBox.Blocks)
                {
                    foreach (var nested in EnumerateShapeInlines(block))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    private static string CollectDocumentText(Document document)
    {
        var builder = new StringBuilder();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            AppendBlockText(document.Blocks[blockIndex], builder);
        }

        for (var sectionIndex = 0; sectionIndex < document.Sections.Count; sectionIndex++)
        {
            var section = document.Sections[sectionIndex];
            AppendHeaderFooterText(section.Header, builder);
            AppendHeaderFooterText(section.Footer, builder);
        }

        return builder.ToString();
    }

    private static void AppendHeaderFooterText(HeaderFooter headerFooter, StringBuilder builder)
    {
        for (var blockIndex = 0; blockIndex < headerFooter.Blocks.Count; blockIndex++)
        {
            AppendBlockText(headerFooter.Blocks[blockIndex], builder);
        }
    }

    private static void AppendBlockText(Block block, StringBuilder builder)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                if (!string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    builder.AppendLine(paragraph.Text);
                }

                for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
                {
                    AppendInlineText(paragraph.Inlines[inlineIndex], builder);
                }

                for (var floatingIndex = 0; floatingIndex < paragraph.FloatingObjects.Count; floatingIndex++)
                {
                    AppendInlineText(paragraph.FloatingObjects[floatingIndex].Content, builder);
                }

                break;

            case TableBlock table:
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var cell = row.Cells[cellIndex];
                        for (var paragraphIndex = 0; paragraphIndex < cell.Paragraphs.Count; paragraphIndex++)
                        {
                            AppendBlockText(cell.Paragraphs[paragraphIndex], builder);
                        }
                    }
                }

                break;
        }
    }

    private static void AppendInlineText(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case RunInline run:
                var text = run.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }

                break;

            case ShapeInline shape when shape.TextBox is not null:
                for (var blockIndex = 0; blockIndex < shape.TextBox.Blocks.Count; blockIndex++)
                {
                    AppendBlockText(shape.TextBox.Blocks[blockIndex], builder);
                }

                break;
        }
    }

    private static string ResolveSampleCorpusPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VibeOffice.slnx")))
            {
                return Path.Combine(directory.FullName, "external", "Reporting-Services", "PaginatedReportSamples");
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "external", "Reporting-Services", "PaginatedReportSamples");
    }

    private sealed class StubReportingStudioFilePickerService : IReportingStudioFilePickerService
    {
        public string? SaveTemplatePath { get; init; }

        public string? ExportRdlPath { get; init; }

        public ValueTask<string?> PickOpenTemplatePathAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<string?> PickSaveTemplatePathAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(SaveTemplatePath);
        }

        public ValueTask<string?> PickImportRdlPathAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<string?> PickExportRdlPathAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ExportRdlPath);
        }
    }
}
