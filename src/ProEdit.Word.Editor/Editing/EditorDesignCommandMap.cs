using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Primitives;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorDesignCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private readonly EditorServices _services;

    public EditorDesignCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Register()
    {
        _router.RegisterAction(EditorDesignCommandIds.Themes.Theme, (_, payload) => ApplyTheme(payload));
        _router.RegisterAction(EditorDesignCommandIds.Themes.Colors, (_, payload) => ApplyThemeColors(payload));
        _router.RegisterAction(EditorDesignCommandIds.Themes.Fonts, (_, payload) => ApplyThemeFonts(payload));
        _router.RegisterAction(EditorDesignCommandIds.Themes.Effects, (_, __) => ShowNotImplemented("Theme Effects", "Theme effects are not available yet."), isUndoable: false);
        _router.RegisterAction(EditorDesignCommandIds.Themes.SetAsDefault, (_, __) => ShowNotImplemented("Set As Default", "Setting default themes is not available yet."), isUndoable: false);

        _router.RegisterAction(EditorDesignCommandIds.DocumentFormatting.StyleSet, (_, payload) => ApplyStyleSet(payload));
        _router.RegisterAction(EditorDesignCommandIds.DocumentFormatting.Colors, (_, payload) => ApplyThemeColors(payload));
        _router.RegisterAction(EditorDesignCommandIds.DocumentFormatting.Fonts, (_, payload) => ApplyThemeFonts(payload));
        _router.RegisterAction(EditorDesignCommandIds.DocumentFormatting.ParagraphSpacing, (_, payload) => ApplyParagraphSpacing(payload));
        _router.RegisterAction(EditorDesignCommandIds.DocumentFormatting.Effects, (_, __) => ShowNotImplemented("Document Effects", "Document effects are not available yet."), isUndoable: false);
        _router.RegisterAction(EditorDesignCommandIds.DocumentFormatting.SetAsDefault, (_, __) => ShowNotImplemented("Set As Default", "Setting default formatting is not available yet."), isUndoable: false);

        _router.RegisterAction(EditorDesignCommandIds.PageBackground.Watermark, (_, payload) => ToggleWatermark(payload));
        _router.RegisterAction(EditorDesignCommandIds.PageBackground.PageColor, (_, payload) => TogglePageColor(payload));
        _router.RegisterAction(EditorDesignCommandIds.PageBackground.PageBorders, (_, payload) => TogglePageBorders(payload));
    }

    private void ShowNotImplemented(string title, string message)
    {
        if (_services.TryGet<IEditorDialogService>(out var dialog))
        {
            _ = dialog.ShowMessageAsync(title, message);
        }
    }

    private void ApplyTheme(object? payload)
    {
        var name = payload as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (TryGetThemePalette(name, out var palette))
        {
            ApplyPalette(palette);
        }
    }

    private void ApplyThemeColors(object? payload)
    {
        var name = payload as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (TryGetColorPalette(name, out var palette))
        {
            ApplyPalette(palette);
        }
    }

    private void ApplyThemeFonts(object? payload)
    {
        var name = payload as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmed = name.Trim();
        ApplyThemeFonts(trimmed, trimmed);
    }

    private void ApplyStyleSet(object? payload)
    {
        var name = payload as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        switch (name.Trim())
        {
            case "Modern":
                ApplyThemeFonts("Aptos", "Aptos");
                ApplyParagraphSpacing("Open");
                break;
            case "Elegant":
                ApplyThemeFonts("Cambria", "Cambria");
                ApplyParagraphSpacing("Compact");
                break;
            default:
                ApplyParagraphSpacing("Default");
                break;
        }
    }

    private void ApplyParagraphSpacing(object? payload)
    {
        var name = payload as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var before = 0f;
        var after = 0f;
        switch (name.Trim())
        {
            case "No Spacing":
                before = 0f;
                after = 0f;
                break;
            case "Compact":
                before = 0f;
                after = 4f;
                break;
            case "Open":
                before = 6f;
                after = 10f;
                break;
            default:
                before = 0f;
                after = 8f;
                break;
        }

        _session.Document.DefaultParagraphStyleProperties.SpacingBefore = before;
        _session.Document.DefaultParagraphStyleProperties.SpacingAfter = after;

        foreach (var paragraph in EnumerateParagraphs())
        {
            paragraph.Properties.SpacingBefore = before;
            paragraph.Properties.SpacingAfter = after;
        }

        _session.RefreshLayout();
    }

    private void TogglePageColor(object? payload)
    {
        var color = payload is DocColor provided
            ? provided
            : new DocColor(242, 242, 242);

        ApplyToSections(properties =>
        {
            if (properties.PageBackgroundColor.HasValue)
            {
                properties.PageBackgroundColor = null;
            }
            else
            {
                properties.PageBackgroundColor = color;
            }
        });

        _session.RefreshLayout();
    }

    private void TogglePageBorders(object? payload)
    {
        var borderColor = payload is DocColor provided
            ? provided
            : DocColor.Black;

        ApplyToSections(properties =>
        {
            if (properties.PageBorders is { HasAny: true })
            {
                properties.PageBorders = null;
                return;
            }

            properties.PageBorders = new PageBorders
            {
                Top = new BorderLine { Color = borderColor },
                Bottom = new BorderLine { Color = borderColor },
                Left = new BorderLine { Color = borderColor },
                Right = new BorderLine { Color = borderColor },
                OffsetFrom = PageBorderOffset.Page,
                Display = PageBorderDisplay.AllPages,
                ZOrder = PageBorderZOrder.Back
            };
        });

        _session.RefreshLayout();
    }

    private void ToggleWatermark(object? payload)
    {
        if (RemoveExistingWatermark())
        {
            _session.RefreshLayout();
            return;
        }

        var text = payload as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "CONFIDENTIAL";
        }

        InsertWatermark(text.Trim());
        _session.RefreshLayout();
    }

    private bool RemoveExistingWatermark()
    {
        var removed = false;
        foreach (var paragraph in EnumerateParagraphs())
        {
            for (var i = paragraph.FloatingObjects.Count - 1; i >= 0; i--)
            {
                var floating = paragraph.FloatingObjects[i];
                if (floating.Content is ShapeInline shape && string.Equals(shape.Name, "Watermark", StringComparison.OrdinalIgnoreCase))
                {
                    paragraph.FloatingObjects.RemoveAt(i);
                    removed = true;
                }
            }
        }

        return removed;
    }

    private void InsertWatermark(string text)
    {
        var paragraph = _session.Document.ParagraphCount > 0
            ? _session.Document.GetParagraph(0)
            : new ParagraphBlock();

        var width = MathF.Max(200f, _session.LayoutSettings.PageWidth * 0.7f);
        var height = MathF.Max(120f, _session.LayoutSettings.PageHeight * 0.25f);
        var textStyle = new TextStyleProperties
        {
            FontSize = 72f,
            FontWeight = DocFontWeight.Bold,
            Color = new DocColor(200, 200, 200)
        };

        var textParagraph = new ParagraphBlock(string.Empty)
        {
            Properties =
            {
                Alignment = ParagraphAlignment.Center
            }
        };
        textParagraph.Inlines.Add(new RunInline(text, textStyle));

        var textBox = new ShapeTextBox();
        textBox.Properties.VerticalAlignment = ShapeTextVerticalAlignment.Center;
        textBox.Blocks.Add(textParagraph);

        var shape = new ShapeInline(width, height)
        {
            Name = "Watermark",
            TextBox = textBox
        };
        shape.Properties.PresetGeometry = "rect";
        shape.Properties.Rotation = -45f;

        var floating = new FloatingObject(shape);
        floating.Anchor.HorizontalReference = FloatingHorizontalReference.Page;
        floating.Anchor.VerticalReference = FloatingVerticalReference.Page;
        floating.Anchor.HorizontalAlignment = FloatingHorizontalAlignment.Center;
        floating.Anchor.VerticalAlignment = FloatingVerticalAlignment.Center;
        floating.Anchor.WrapStyle = FloatingWrapStyle.None;
        floating.Anchor.BehindText = true;

        paragraph.FloatingObjects.Add(floating);
        if (_session.Document.ParagraphCount == 0)
        {
            _session.Document.Blocks.Add(paragraph);
        }
    }

    private void ApplyPalette(ThemePalette palette)
    {
        var colors = _session.Document.ThemeColors;
        colors.Clear();
        if (!palette.UseDefaults)
        {
            ApplyThemeColor(colors, DocThemeColor.Dark1, palette.Dark1);
            ApplyThemeColor(colors, DocThemeColor.Light1, palette.Light1);
            ApplyThemeColor(colors, DocThemeColor.Dark2, palette.Dark2);
            ApplyThemeColor(colors, DocThemeColor.Light2, palette.Light2);
            ApplyThemeColor(colors, DocThemeColor.Accent1, palette.Accent1);
            ApplyThemeColor(colors, DocThemeColor.Accent2, palette.Accent2);
            ApplyThemeColor(colors, DocThemeColor.Accent3, palette.Accent3);
            ApplyThemeColor(colors, DocThemeColor.Accent4, palette.Accent4);
            ApplyThemeColor(colors, DocThemeColor.Accent5, palette.Accent5);
            ApplyThemeColor(colors, DocThemeColor.Accent6, palette.Accent6);
            ApplyThemeColor(colors, DocThemeColor.Hyperlink, palette.Hyperlink);
            ApplyThemeColor(colors, DocThemeColor.FollowedHyperlink, palette.FollowedHyperlink);
        }

        if (!string.IsNullOrWhiteSpace(palette.MajorFont) || !string.IsNullOrWhiteSpace(palette.MinorFont))
        {
            ApplyThemeFonts(palette.MajorFont, palette.MinorFont);
        }

        _session.RefreshLayout();
    }

    private static void ApplyThemeColor(DocumentThemeColorMap colors, DocThemeColor themeColor, DocColor? value)
    {
        if (value.HasValue)
        {
            colors.Set(themeColor, value);
        }
    }

    private void ApplyThemeFonts(string? major, string? minor)
    {
        var theme = _session.Document.Fonts.Theme;
        theme.Clear();

        if (!string.IsNullOrWhiteSpace(major))
        {
            theme.Set(DocThemeFont.MajorAscii, major);
            theme.Set(DocThemeFont.MajorHighAnsi, major);
            theme.Set(DocThemeFont.MajorEastAsia, major);
            theme.Set(DocThemeFont.MajorBidi, major);
        }

        if (!string.IsNullOrWhiteSpace(minor))
        {
            theme.Set(DocThemeFont.MinorAscii, minor);
            theme.Set(DocThemeFont.MinorHighAnsi, minor);
            theme.Set(DocThemeFont.MinorEastAsia, minor);
            theme.Set(DocThemeFont.MinorBidi, minor);
            _session.Document.DefaultTextStyle.FontFamily = minor;
        }

        _session.RefreshLayout();
    }

    private void ApplyToSections(Action<SectionProperties> action)
    {
        action(_session.Document.SectionProperties);
        foreach (var section in _session.Document.Sections)
        {
            action(section.Properties);
        }
    }

    private IEnumerable<ParagraphBlock> EnumerateParagraphs()
    {
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    yield return paragraph;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                yield return paragraph;
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static bool TryGetThemePalette(string name, out ThemePalette palette)
    {
        return ThemePalettes.TryGetValue(name.Trim(), out palette);
    }

    private static bool TryGetColorPalette(string name, out ThemePalette palette)
    {
        return ColorPalettes.TryGetValue(name.Trim(), out palette);
    }

    private static readonly Dictionary<string, ThemePalette> ThemePalettes = new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "Office",
            new ThemePalette("Office", UseDefaults: true)
        },
        {
            "Facet",
            new ThemePalette(
                "Facet",
                Dark1: new DocColor(0, 0, 0),
                Light1: new DocColor(255, 255, 255),
                Dark2: new DocColor(34, 34, 34),
                Light2: new DocColor(242, 242, 242),
                Accent1: new DocColor(31, 73, 125),
                Accent2: new DocColor(79, 129, 189),
                Accent3: new DocColor(155, 187, 89),
                Accent4: new DocColor(192, 80, 77),
                Accent5: new DocColor(128, 100, 162),
                Accent6: new DocColor(75, 172, 198),
                Hyperlink: new DocColor(0, 112, 192),
                FollowedHyperlink: new DocColor(128, 0, 128),
                MajorFont: "Aptos Display",
                MinorFont: "Aptos")
        },
        {
            "Ion",
            new ThemePalette(
                "Ion",
                Dark1: new DocColor(0, 0, 0),
                Light1: new DocColor(255, 255, 255),
                Dark2: new DocColor(47, 47, 47),
                Light2: new DocColor(244, 244, 244),
                Accent1: new DocColor(0, 112, 192),
                Accent2: new DocColor(0, 176, 240),
                Accent3: new DocColor(146, 208, 80),
                Accent4: new DocColor(255, 192, 0),
                Accent5: new DocColor(255, 0, 0),
                Accent6: new DocColor(112, 48, 160),
                Hyperlink: new DocColor(0, 112, 192),
                FollowedHyperlink: new DocColor(112, 48, 160),
                MajorFont: "Calibri",
                MinorFont: "Calibri")
        }
    };

    private static readonly Dictionary<string, ThemePalette> ColorPalettes = new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "Office",
            new ThemePalette("Office", UseDefaults: true)
        },
        {
            "Blue",
            new ThemePalette(
                "Blue",
                Accent1: new DocColor(31, 73, 125),
                Accent2: new DocColor(79, 129, 189),
                Accent3: new DocColor(143, 170, 220),
                Accent4: new DocColor(184, 204, 228),
                Accent5: new DocColor(54, 95, 145),
                Accent6: new DocColor(95, 136, 186),
                Hyperlink: new DocColor(0, 112, 192),
                FollowedHyperlink: new DocColor(128, 0, 128))
        },
        {
            "Green",
            new ThemePalette(
                "Green",
                Accent1: new DocColor(0, 102, 51),
                Accent2: new DocColor(0, 153, 102),
                Accent3: new DocColor(102, 204, 153),
                Accent4: new DocColor(153, 204, 153),
                Accent5: new DocColor(51, 153, 102),
                Accent6: new DocColor(0, 128, 96),
                Hyperlink: new DocColor(0, 112, 192),
                FollowedHyperlink: new DocColor(128, 0, 128))
        },
        {
            "Colorful",
            new ThemePalette(
                "Colorful",
                Accent1: new DocColor(192, 80, 77),
                Accent2: new DocColor(155, 187, 89),
                Accent3: new DocColor(128, 100, 162),
                Accent4: new DocColor(75, 172, 198),
                Accent5: new DocColor(247, 150, 70),
                Accent6: new DocColor(79, 129, 189),
                Hyperlink: new DocColor(0, 112, 192),
                FollowedHyperlink: new DocColor(128, 0, 128))
        },
        {
            "Monochrome",
            new ThemePalette(
                "Monochrome",
                Accent1: new DocColor(64, 64, 64),
                Accent2: new DocColor(96, 96, 96),
                Accent3: new DocColor(128, 128, 128),
                Accent4: new DocColor(160, 160, 160),
                Accent5: new DocColor(192, 192, 192),
                Accent6: new DocColor(224, 224, 224),
                Hyperlink: new DocColor(0, 112, 192),
                FollowedHyperlink: new DocColor(128, 0, 128))
        }
    };

    private readonly record struct ThemePalette(
        string Name,
        bool UseDefaults = false,
        DocColor? Dark1 = null,
        DocColor? Light1 = null,
        DocColor? Dark2 = null,
        DocColor? Light2 = null,
        DocColor? Accent1 = null,
        DocColor? Accent2 = null,
        DocColor? Accent3 = null,
        DocColor? Accent4 = null,
        DocColor? Accent5 = null,
        DocColor? Accent6 = null,
        DocColor? Hyperlink = null,
        DocColor? FollowedHyperlink = null,
        string? MajorFont = null,
        string? MinorFont = null);
}
