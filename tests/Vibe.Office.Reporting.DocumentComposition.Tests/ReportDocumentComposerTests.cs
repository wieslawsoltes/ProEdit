using Vibe.Office.Documents;
using Vibe.Office.OpenXml;
using Vibe.Office.Reporting.DocumentComposition;
using Xunit;

namespace Vibe.Office.Reporting.DocumentComposition.Tests;

public sealed class ReportDocumentComposerTests
{
    [Fact]
    public async Task ComposeAsync_CreatesHeadersPageFieldsTablesAndCharts()
    {
        var report = new MaterializedReport
        {
            Id = "sales",
            Name = "Sales"
        };

        var section = new MaterializedReportSection
        {
            Id = "main",
            Name = "Main",
            Bookmark = "main-section",
            PageSettings = new ReportPageSettings
            {
                Width = 800f,
                Height = 1100f,
                MarginLeft = 50f,
                MarginTop = 60f,
                MarginRight = 50f,
                MarginBottom = 60f
            }
        };

        section.HeaderItems.Add(new MaterializedTextReportItem { Text = "Page", ValueKind = MaterializedTextValueKind.Static });
        section.HeaderItems.Add(new MaterializedTextReportItem { ValueKind = MaterializedTextValueKind.PageNumber });
        section.HeaderItems.Add(new MaterializedTextReportItem { Text = "of", ValueKind = MaterializedTextValueKind.Static });
        section.HeaderItems.Add(new MaterializedTextReportItem { ValueKind = MaterializedTextValueKind.TotalPages });

        section.BodyItems.Add(new MaterializedTextReportItem
        {
            SourceItemId = "overview",
            Text = "Overview",
            Bookmark = "overview",
            Style = new MaterializedReportStyle
            {
                Bold = true,
                FontSize = 18f
            }
        });

        var tablix = new MaterializedTablixReportItem
        {
            SourceItemId = "table",
            RepeatHeaderRows = true
        };
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "col1", Width = 100f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "col2", Width = 100f });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            IsHeader = true,
            Cells =
            {
                new MaterializedTablixCell { Text = "Region" },
                new MaterializedTablixCell { Text = "Amount" }
            }
        });
        tablix.Rows.Add(new MaterializedTablixRow
        {
            Cells =
            {
                new MaterializedTablixCell { Text = "West" },
                new MaterializedTablixCell { Text = "10.00" }
            }
        });
        section.BodyItems.Add(tablix);

        var chart = new MaterializedChartReportItem
        {
            SourceItemId = "chart",
            Bounds = new ReportItemBounds(0f, 0f, 200f, 120f),
            Model = new ChartModel
            {
                Title = "Sales"
            }
        };
        chart.Model.Series.Add(new ChartSeries
        {
            Name = "Amount",
            Points =
            {
                new ChartPoint { Category = "West", Value = 10d }
            }
        });
        section.BodyItems.Add(chart);

        report.Sections.Add(section);

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Sections);
        var headerAnchor = Assert.Single(result.Document.Header.Blocks.OfType<ParagraphBlock>());
        Assert.Equal(4, headerAnchor.FloatingObjects.Count);
        Assert.Contains(EnumerateFloatingInlines(headerAnchor), inline => inline is PageNumberInline);
        Assert.Contains(EnumerateFloatingInlines(headerAnchor), inline => inline is TotalPagesInline);
        Assert.Contains(result.Document.Blocks, block => block is ParagraphBlock paragraph && ParagraphContainsBookmark(paragraph, "main-section"));
        Assert.Contains(result.Document.Blocks, block => block is TableBlock);
        Assert.Contains(result.Document.Blocks, block => block is ParagraphBlock paragraph && ParagraphContainsAnyBookmark(paragraph));
        Assert.Contains(result.Document.Blocks, block => block is ParagraphBlock paragraph && paragraph.FloatingObjects.Any(static floating => floating.Content is ChartInline));
    }

    [Fact]
    public async Task ComposeAsync_RendersStateIndicatorGaugeAsImageInline()
    {
        var report = new MaterializedReport
        {
            Id = "gauges",
            Name = "Gauges",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedGaugeReportItem
                        {
                            SourceItemId = "variance-indicator",
                            Name = "Variance Indicator",
                            GaugeKind = ReportGaugeKind.StateIndicator,
                            Value = -1d,
                            Bounds = new ReportItemBounds(24f, 32f, 28f, 28f)
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        var anchor = Assert.Single(document.Blocks.OfType<ParagraphBlock>(), static paragraph => paragraph.FloatingObjects.Count > 0);
        var image = Assert.IsType<ImageInline>(Assert.Single(anchor.FloatingObjects).Content);
        Assert.Equal("image/svg+xml", image.ContentType);
    }

    [Fact]
    public async Task ComposeAsync_BindsTemplateContentAndAddsSectionBreaks()
    {
        var nestedReport = new MaterializedReport
        {
            Id = "nested",
            Name = "Nested",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "nested-section",
                    Name = "Nested Section",
                    BodyItems =
                    {
                        new MaterializedTextReportItem
                        {
                            Text = "Nested"
                        }
                    }
                }
            }
        };

        var report = new MaterializedReport
        {
            Id = "main",
            Name = "Main",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "first",
                    Name = "First",
                    BodyItems =
                    {
                        new MaterializedDocumentTemplateReportItem
                        {
                            SourceItemId = "template",
                            TemplateFormat = ReportDocumentTemplateFormat.Vibe,
                            Content = "Hello {{Name}}",
                            Bindings =
                            {
                                ["Name"] = "Contoso"
                            }
                        }
                    }
                },
                new MaterializedReportSection
                {
                    Id = "second",
                    Name = "Second",
                    BodyItems =
                    {
                        new MaterializedSubreportReportItem
                        {
                            SourceItemId = "subreport",
                            Report = nestedReport
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Document);
        Assert.Equal(2, result.Document!.Sections.Count);
        Assert.Contains(result.Document.Blocks, block => block is SectionBreakBlock);

        var text = GetDocumentText(result.Document);
        Assert.Contains("Hello Contoso", text);
        Assert.Contains("Nested", text);
    }

    [Fact]
    public async Task ComposeAsync_ImportsTemplateCustomXmlBindingsIntoTargetDocument()
    {
        var sourceTemplate = new Document();
        sourceTemplate.Blocks.Clear();
        sourceTemplate.CustomXmlParts["template-store"] = System.Xml.Linq.XDocument.Parse("<root><Name>Original</Name></root>");

        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new ContentControlStartInline(new ContentControlProperties
        {
            Alias = "Name",
            DataBinding = new ContentControlDataBinding
            {
                StoreItemId = "template-store",
                XPath = "/root/Name"
            }
        }));
        paragraph.Inlines.Add(new RunInline("Placeholder"));
        paragraph.Inlines.Add(new ContentControlEndInline(id: null));
        sourceTemplate.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        new DocxExporter().Save(sourceTemplate, stream);

        var report = new MaterializedReport
        {
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedDocumentTemplateReportItem
                        {
                            SourceItemId = "template",
                            TemplateFormat = ReportDocumentTemplateFormat.Docx,
                            Content = Convert.ToBase64String(stream.ToArray()),
                            Bindings =
                            {
                                ["Name"] = "Alice"
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Document);
        Assert.Contains("template-store", result.Document!.CustomXmlParts.Keys);

        var importedParagraph = Assert.IsType<ParagraphBlock>(result.Document.Blocks[0]);
        var controlStart = Assert.IsType<ContentControlStartInline>(importedParagraph.Inlines[0]);
        Assert.Equal("Alice", ContentControlValueResolver.ResolveContentControlValue(controlStart.Properties, result.Document));
    }

    [Fact]
    public async Task ComposeAsync_DoesNotOverGrowExactFitTwoWordLabel()
    {
        var report = new MaterializedReport
        {
            Id = "invoice-labels",
            Name = "Invoice Labels",
            DefaultFontFamily = "Segoe UI",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "payment-reference",
                            Name = "PaymentReference",
                            Text = "Payment reference",
                            Bounds = new ReportItemBounds(0f, 0f, 94.5f, 15.1f),
                            CanGrow = true,
                            Style = new MaterializedReportStyle
                            {
                                FontSize = 11.338582f
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var anchor = Assert.Single(result.Document!.Blocks.OfType<ParagraphBlock>());
        var shape = Assert.IsType<ShapeInline>(Assert.Single(anchor.FloatingObjects).Content);
        Assert.Equal(94.5f, shape.Width, 3);
        Assert.Equal(15.1f, shape.Height, 3);
    }

    [Fact]
    public async Task ComposeAsync_AppliesRichStylesZOrderAndTablixPageBreaks()
    {
        var report = new MaterializedReport
        {
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "styled",
                            Name = "Styled",
                            Bounds = new ReportItemBounds(0f, 0f, 180f, 30f),
                            ZIndex = 7,
                            Text = "Styled",
                            Style = new MaterializedReportStyle
                            {
                                PaddingLeft = 8f,
                                PaddingTop = 6f,
                                PaddingRight = 4f,
                                PaddingBottom = 2f,
                                VerticalAlign = ReportVerticalAlignment.Middle,
                                TextDecoration = ReportTextDecoration.Underline,
                                Border = new MaterializedReportBorder
                                {
                                    Style = ReportBorderLineStyle.None
                                },
                                TopBorder = new MaterializedReportBorder
                                {
                                    Style = ReportBorderLineStyle.Solid,
                                    Width = 1f,
                                    Color = "Black"
                                }
                            }
                        },
                        new MaterializedTablixReportItem
                        {
                            SourceItemId = "tablix",
                            Name = "Tablix",
                            Bounds = new ReportItemBounds(0f, 60f, 240f, 160f),
                            RepeatHeaderRows = true,
                            Columns =
                            {
                                new MaterializedTablixColumn
                                {
                                    Id = "col1",
                                    Width = 120f
                                }
                            },
                            Rows =
                            {
                                new MaterializedTablixRow
                                {
                                    IsHeader = true,
                                    Cells =
                                    {
                                        new MaterializedTablixCell { Text = "Region" }
                                    }
                                },
                                new MaterializedTablixRow
                                {
                                    Cells =
                                    {
                                        new MaterializedTablixCell
                                        {
                                            Text = "West",
                                            Style = new MaterializedReportStyle
                                            {
                                                PaddingLeft = 10f,
                                                PaddingTop = 6f,
                                                PaddingRight = 4f,
                                                PaddingBottom = 2f,
                                                VerticalAlign = ReportVerticalAlignment.Bottom,
                                                Border = new MaterializedReportBorder
                                                {
                                                    Style = ReportBorderLineStyle.Solid,
                                                    Width = 1f,
                                                    Color = "Black"
                                                }
                                            }
                                        }
                                    }
                                },
                                new MaterializedTablixRow
                                {
                                    PageBreakBefore = true,
                                    Cells =
                                    {
                                        new MaterializedTablixCell
                                        {
                                            Text = "East"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);

        var anchor = Assert.Single(document.Blocks.OfType<ParagraphBlock>(), static paragraph => paragraph.FloatingObjects.Count > 0);
        var floating = Assert.Single(anchor.FloatingObjects);
        var textShape = Assert.IsType<ShapeInline>(floating.Content);
        Assert.Equal(7u, floating.Anchor.ZOrder);
        Assert.Equal(new Vibe.Office.Primitives.DocThickness(8f, 6f, 4f, 2f), textShape.TextBox!.Properties.Padding);
        Assert.Equal(ShapeTextVerticalAlignment.Center, textShape.TextBox.Properties.VerticalAlignment);

        var tableBlocks = document.Blocks.OfType<TableBlock>().ToList();
        Assert.Equal(2, tableBlocks.Count);
        Assert.Contains(document.Blocks, static block => block is PageBreakBlock);
        Assert.Equal(new Vibe.Office.Primitives.DocThickness(10f, 6f, 4f, 2f), tableBlocks[0].Rows[1].Cells[0].Properties.Padding!.Value);
        Assert.Equal(TableCellVerticalAlignment.Bottom, tableBlocks[0].Rows[1].Cells[0].Properties.VerticalAlignment);
    }

    [Fact]
    public async Task ComposeAsync_ReflowsCanGrowTextAndConsumesContainerWhitespace()
    {
        var report = new MaterializedReport
        {
            ConsumeContainerWhitespace = true,
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedContainerReportItem
                        {
                            SourceItemId = "header",
                            Name = "Header",
                            Bounds = new ReportItemBounds(0f, 0f, 220f, 48f),
                            Items =
                            {
                                new MaterializedTextReportItem
                                {
                                    SourceItemId = "title",
                                    Name = "Title",
                                    Bounds = new ReportItemBounds(0f, 0f, 90f, 12f),
                                    Text = "Contoso invoice heading with a long wrapped title",
                                    CanGrow = true,
                                    Style = new MaterializedReportStyle
                                    {
                                        FontSize = 12f,
                                        PaddingLeft = 2f,
                                        PaddingRight = 2f,
                                        PaddingTop = 2f,
                                        PaddingBottom = 2f
                                    }
                                },
                                new MaterializedTextReportItem
                                {
                                    SourceItemId = "address",
                                    Name = "Address",
                                    Bounds = new ReportItemBounds(0f, 18f, 90f, 12f),
                                    Text = "123 Violet Road",
                                    CanGrow = true,
                                    Style = new MaterializedReportStyle
                                    {
                                        FontSize = 12f
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        var outerAnchor = Assert.Single(document.Blocks.OfType<ParagraphBlock>(), static paragraph => paragraph.FloatingObjects.Count > 0);
        var outerShape = Assert.IsType<ShapeInline>(Assert.Single(outerAnchor.FloatingObjects).Content);
        Assert.True(outerShape.Height > 48f);

        var innerAnchor = Assert.Single(outerShape.TextBox!.Blocks.OfType<ParagraphBlock>(), static paragraph => paragraph.FloatingObjects.Count > 0);
        Assert.Equal(2, innerAnchor.FloatingObjects.Count);

        var firstTextShape = Assert.IsType<ShapeInline>(innerAnchor.FloatingObjects[0].Content);
        var secondTextShape = Assert.IsType<ShapeInline>(innerAnchor.FloatingObjects[1].Content);
        Assert.True(firstTextShape.Height > 12f);
        Assert.True(innerAnchor.FloatingObjects[1].Anchor.OffsetY >= 18f);
        Assert.Equal(ShapeTextOverflow.Clip, secondTextShape.TextBox!.Properties.VerticalOverflow);
    }

    [Fact]
    public async Task ComposeAsync_ReservesRdlHeaderFooterBandsAndGrowsNestedCellContainers()
    {
        var report = new MaterializedReport
        {
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "invoice",
                    Name = "Invoice",
                    PageSettings = new ReportPageSettings
                    {
                        Width = 816f,
                        Height = 1056f,
                        MarginLeft = 0f,
                        MarginTop = 0f,
                        MarginRight = 0f,
                        MarginBottom = 0f,
                        HeaderHeight = 96f,
                        FooterHeight = 48f
                    },
                    HeaderItems =
                    {
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "header-title",
                            Name = "Header Title",
                            Bounds = new ReportItemBounds(24f, 12f, 180f, 18f),
                            Text = "Header"
                        }
                    },
                    FooterItems =
                    {
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "footer-title",
                            Name = "Footer Title",
                            Bounds = new ReportItemBounds(24f, 12f, 180f, 18f),
                            Text = "Footer"
                        }
                    },
                    BodyItems =
                    {
                        new MaterializedTablixReportItem
                        {
                            SourceItemId = "outer-tablix",
                            Name = "Outer Tablix",
                            Columns =
                            {
                                new MaterializedTablixColumn
                                {
                                    Id = "col1",
                                    Width = 320f
                                }
                            },
                            Rows =
                            {
                                new MaterializedTablixRow
                                {
                                    Height = 24f,
                                    Cells =
                                    {
                                        new MaterializedTablixCell
                                        {
                                            Content = new MaterializedContainerReportItem
                                            {
                                                SourceItemId = "detail-container",
                                                Name = "Detail Container",
                                                Bounds = new ReportItemBounds(0f, 0f, 320f, 24f),
                                                Items =
                                                {
                                                    new MaterializedTablixReportItem
                                                    {
                                                        SourceItemId = "inner-tablix",
                                                        Name = "Inner Tablix",
                                                        Bounds = new ReportItemBounds(0f, 0f, 320f, 24f),
                                                        Columns =
                                                        {
                                                            new MaterializedTablixColumn
                                                            {
                                                                Id = "inner-col1",
                                                                Width = 320f
                                                            }
                                                        },
                                                        Rows =
                                                        {
                                                            new MaterializedTablixRow
                                                            {
                                                                Height = 20f,
                                                                Cells =
                                                                {
                                                                    new MaterializedTablixCell { Text = "Line 1" }
                                                                }
                                                            },
                                                            new MaterializedTablixRow
                                                            {
                                                                Height = 20f,
                                                                Cells =
                                                                {
                                                                    new MaterializedTablixCell { Text = "Line 2" }
                                                                }
                                                            },
                                                            new MaterializedTablixRow
                                                            {
                                                                Height = 20f,
                                                                Cells =
                                                                {
                                                                    new MaterializedTablixCell { Text = "Line 3" }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        Assert.Equal(96f, document.SectionProperties.MarginTop);
        Assert.Equal(48f, document.SectionProperties.MarginBottom);
        Assert.Equal(0f, document.SectionProperties.HeaderOffset);
        Assert.Equal(0f, document.SectionProperties.FooterOffset);

        var table = Assert.Single(document.Blocks.OfType<TableBlock>());
        var cellParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(table.Rows[0].Cells[0].Blocks));
        var containerShape = Assert.IsType<ShapeInline>(Assert.Single(cellParagraph.Inlines));
        Assert.True(containerShape.Height >= 60f, $"Expected nested container to grow beyond its original placeholder height, but was {containerShape.Height}.");
    }

    [Fact]
    public async Task ComposeAsync_PositionsEmbeddedSubreportAtPlaceholderAndReflowsFollowingItems()
    {
        var nestedReport = new MaterializedReport
        {
            Id = "detail",
            Name = "Detail",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "detail-section",
                    Name = "Detail Section",
                    BodyItems =
                    {
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "nested-title",
                            Name = "Nested Title",
                            Text = "Regional Detail: Central",
                            Bounds = new ReportItemBounds(24f, 24f, 180f, 20f)
                        },
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "nested-body",
                            Name = "Nested Body",
                            Text = "Q1 Revenue",
                            Bounds = new ReportItemBounds(24f, 108f, 180f, 24f)
                        }
                    }
                }
            }
        };

        var report = new MaterializedReport
        {
            Id = "overview",
            Name = "Overview",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new MaterializedSubreportReportItem
                        {
                            SourceItemId = "detail-host",
                            Name = "Detail Host",
                            Bounds = new ReportItemBounds(528f, 274f, 220f, 96f),
                            Report = nestedReport
                        },
                        new MaterializedTextReportItem
                        {
                            SourceItemId = "after-subreport",
                            Name = "After Subreport",
                            Text = "Revenue Table",
                            Bounds = new ReportItemBounds(528f, 390f, 220f, 24f)
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        var floatingObjects = document.Blocks
            .OfType<ParagraphBlock>()
            .SelectMany(static paragraph => paragraph.FloatingObjects)
            .ToList();

        var nestedTitle = Assert.Single(floatingObjects, static floating => GetFloatingText(floating).Contains("Regional Detail: Central", StringComparison.Ordinal));
        var nestedBody = Assert.Single(floatingObjects, static floating => GetFloatingText(floating).Contains("Q1 Revenue", StringComparison.Ordinal));
        var afterSubreport = Assert.Single(floatingObjects, static floating => GetFloatingText(floating).Contains("Revenue Table", StringComparison.Ordinal));

        Assert.Equal(552f, nestedTitle.Anchor.OffsetX, 3);
        Assert.Equal(298f, nestedTitle.Anchor.OffsetY, 3);
        Assert.Equal(552f, nestedBody.Anchor.OffsetX, 3);
        Assert.Equal(382f, nestedBody.Anchor.OffsetY, 3);
        Assert.True(afterSubreport.Anchor.OffsetY >= 426f, $"Expected following content to be pushed below the embedded subreport, but was {afterSubreport.Anchor.OffsetY}.");
    }

    [Fact]
    public async Task ComposeAsync_AnchorsNestedContainerContentToContainerParagraphAndAllowsOverflow()
    {
        var report = new MaterializedReport
        {
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    HeaderItems =
                    {
                        new MaterializedContainerReportItem
                        {
                            SourceItemId = "header-container",
                            Name = "Header Container",
                            Bounds = new ReportItemBounds(0f, 0f, 240f, 40f),
                            Items =
                            {
                                new MaterializedTextReportItem
                                {
                                    SourceItemId = "header-text",
                                    Name = "Header Text",
                                    Bounds = new ReportItemBounds(12f, 8f, 160f, 18f),
                                    Text = "Invoice Header"
                                }
                            }
                        }
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        var headerParagraph = Assert.Single(document.Header.Blocks.OfType<ParagraphBlock>());
        var headerShape = Assert.IsType<ShapeInline>(Assert.Single(headerParagraph.FloatingObjects).Content);
        Assert.Equal(ShapeTextOverflow.Overflow, headerShape.TextBox!.Properties.HorizontalOverflow);
        Assert.Equal(ShapeTextOverflow.Overflow, headerShape.TextBox.Properties.VerticalOverflow);

        var nestedAnchor = Assert.Single(headerShape.TextBox.Blocks.OfType<ParagraphBlock>(), static paragraph => paragraph.FloatingObjects.Count > 0);
        var nestedFloating = Assert.Single(nestedAnchor.FloatingObjects);
        Assert.Equal(FloatingHorizontalReference.Paragraph, nestedFloating.Anchor.HorizontalReference);
        Assert.Equal(FloatingVerticalReference.Paragraph, nestedFloating.Anchor.VerticalReference);
        Assert.Equal(12f, nestedFloating.Anchor.OffsetX);
        Assert.Equal(8f, nestedFloating.Anchor.OffsetY);
    }

    [Fact]
    public async Task ComposeAsync_FlowsSingleColumnWidthTablixAcrossMultiColumnPageSettings()
    {
        var report = new MaterializedReport
        {
            Id = "labels",
            Name = "Labels",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    PageSettings = new ReportPageSettings
                    {
                        Width = 612f,
                        Height = 792f,
                        MarginLeft = 15.8f,
                        MarginRight = 15.8f,
                        MarginTop = 36f,
                        MarginBottom = 36f,
                        ColumnCount = 3,
                        ColumnGap = 10.08f
                    },
                    BodyItems =
                    {
                        CreateLabelsTablix()
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        var section = Assert.Single(document.Sections);
        Assert.Equal(3, section.Properties.ColumnCount);

        var table = Assert.Single(document.Blocks.OfType<TableBlock>());
        Assert.Null(table.Properties.FloatingAnchor);
    }

    [Fact]
    public async Task ComposeAsync_FlowsSingleRootOriginAlignedTablixOnSingleColumnPages()
    {
        var tablix = new MaterializedTablixReportItem
        {
            SourceItemId = "country-sales",
            Name = "Country Sales",
            Bounds = new ReportItemBounds(1.3334401f, 0f, 945.60004f, 115.200005f),
            RepeatHeaderRows = true
        };

        tablix.Columns.Add(new MaterializedTablixColumn { Id = "salesperson", Width = 158.4f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "sales", Width = 96f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "trend", Width = 134.4f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "country-total", Width = 134.4f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "overall", Width = 110.4f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "quota", Width = 96f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "variance", Width = 96f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "indicator", Width = 81.6f });
        tablix.Columns.Add(new MaterializedTablixColumn { Id = "icon", Width = 38.4f });

        tablix.Rows.Add(new MaterializedTablixRow
        {
            Height = 24f,
            IsHeader = true,
            Cells =
            {
                new MaterializedTablixCell { Text = "Salesperson" },
                new MaterializedTablixCell { Text = "Sales" },
                new MaterializedTablixCell { Text = "Trend" },
                new MaterializedTablixCell { Text = "% of Country Total" },
                new MaterializedTablixCell { Text = "Overall %" },
                new MaterializedTablixCell { Text = "Quota" },
                new MaterializedTablixCell { Text = "Variance" },
                new MaterializedTablixCell { Text = string.Empty }
            }
        });

        for (var index = 0; index < 12; index++)
        {
            tablix.Rows.Add(new MaterializedTablixRow
            {
                Height = 24f,
                Cells =
                {
                    new MaterializedTablixCell { Text = $"Country {index}" },
                    new MaterializedTablixCell { Text = "$1,000" },
                    new MaterializedTablixCell { Text = "Trend" },
                    new MaterializedTablixCell { Text = "100%" },
                    new MaterializedTablixCell { Text = "5%" },
                    new MaterializedTablixCell { Text = "$900" },
                    new MaterializedTablixCell { Text = "10%" },
                    new MaterializedTablixCell { Text = "Up" }
                }
            });
        }

        var report = new MaterializedReport
        {
            Id = "country-sales",
            Name = "Country Sales",
            Sections =
            {
                new MaterializedReportSection
                {
                    Id = "main",
                    Name = "Main",
                    PageSettings = new ReportPageSettings
                    {
                        Width = 1056f,
                        Height = 816f,
                        MarginLeft = 24f,
                        MarginTop = 24f,
                        MarginRight = 24f,
                        MarginBottom = 24f
                    },
                    BodyItems =
                    {
                        tablix
                    }
                }
            }
        };

        var composer = new ReportDocumentComposer();
        var result = await composer.ComposeAsync(new ReportDocumentCompositionRequest
        {
            MaterializedReport = report
        });

        Assert.False(result.HasErrors);
        var document = Assert.IsType<Document>(result.Document);
        var table = Assert.Single(document.Blocks.OfType<TableBlock>());
        Assert.Null(table.Properties.FloatingAnchor);
    }

    private static string GetDocumentText(Document document)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var block in document.Blocks)
        {
            AppendBlockText(block, builder);
        }

        return builder.ToString();
    }

    private static void AppendBlockText(Block block, System.Text.StringBuilder builder)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                if (!string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    builder.AppendLine(paragraph.Text);
                }

                foreach (var run in paragraph.Inlines.OfType<RunInline>())
                {
                    builder.AppendLine(run.GetText());
                }

                foreach (var floating in paragraph.FloatingObjects)
                {
                    AppendFloatingText(floating, builder);
                }

                break;
            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var cellBlock in cell.Blocks)
                        {
                            AppendBlockText(cellBlock, builder);
                        }
                    }
                }

                break;
        }
    }

    private static IEnumerable<Inline> EnumerateFloatingInlines(ParagraphBlock paragraph)
    {
        foreach (var floating in paragraph.FloatingObjects)
        {
            if (floating.Content is ShapeInline { TextBox: { } textBox })
            {
                foreach (var block in textBox.Blocks)
                {
                    if (block is ParagraphBlock textParagraph)
                    {
                        foreach (var inline in textParagraph.Inlines)
                        {
                            yield return inline;
                        }
                    }
                }
            }
        }
    }

    private static bool ParagraphContainsBookmark(ParagraphBlock paragraph, string bookmarkName)
    {
        return paragraph.Inlines.OfType<BookmarkStartInline>().Any(bookmark => bookmark.Name == bookmarkName)
               || EnumerateFloatingInlines(paragraph).OfType<BookmarkStartInline>().Any(bookmark => bookmark.Name == bookmarkName);
    }

    private static bool ParagraphContainsAnyBookmark(ParagraphBlock paragraph)
    {
        return paragraph.Inlines.Any(static inline => inline is BookmarkStartInline)
               || EnumerateFloatingInlines(paragraph).Any(static inline => inline is BookmarkStartInline);
    }

    private static string GetFloatingText(FloatingObject floating)
    {
        var builder = new System.Text.StringBuilder();
        AppendFloatingText(floating, builder);
        return builder.ToString();
    }

    private static void AppendFloatingText(FloatingObject floating, System.Text.StringBuilder builder)
    {
        switch (floating.Content)
        {
            case ShapeInline { TextBox: { } textBox }:
                foreach (var block in textBox.Blocks)
                {
                    AppendBlockText(block, builder);
                }

                break;
            case ChartInline:
                builder.AppendLine("[Chart]");
                break;
            case ImageInline:
                builder.AppendLine("[Image]");
                break;
        }
    }

    private static MaterializedTablixReportItem CreateLabelsTablix()
    {
        var tablix = new MaterializedTablixReportItem
        {
            SourceItemId = "labels",
            Bounds = new ReportItemBounds(0f, 0f, 186.7f, 72f)
        };

        tablix.Columns.Add(new MaterializedTablixColumn
        {
            Id = "label",
            Width = 186.7f
        });

        tablix.Rows.Add(new MaterializedTablixRow
        {
            Height = 72f,
            Cells =
            {
                new MaterializedTablixCell
                {
                    Text = "Ebony E. Gill\n3235 Mi Casa Court\nBirmingham AL 35203\nUnited States"
                }
            }
        });

        tablix.Rows.Add(new MaterializedTablixRow
        {
            Height = 72f,
            Cells =
            {
                new MaterializedTablixCell
                {
                    Text = "Baha Nueimat\n12308 Neupor Lane\nCharlotte CT 41050\nUnited States"
                }
            }
        });

        return tablix;
    }
}
