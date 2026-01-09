using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorHomeCommandMap
{
    private const float IndentStep = 24f;
    private const float FontSizeStep = 1f;
    private const float MinimumFontSize = 1f;
    private static readonly float[] StandardFontSizes =
    {
        8f, 9f, 10f, 11f, 12f, 14f, 16f, 18f, 20f, 22f, 24f, 26f, 28f, 36f, 48f, 72f
    };
    private const int LineSpacingTwipsPerLine = 240;
    private static readonly DocColor DefaultShadingColor = new DocColor(242, 242, 242);
    private static readonly BorderLine DefaultBorderLine = new BorderLine
    {
        Style = DocBorderStyle.Single,
        Thickness = 1f,
        Color = DocColor.Black
    };

    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private readonly EditorTextFormattingApplier _textFormatting;
    private readonly EditorParagraphApplier _paragraphFormatting;
    private readonly EditorTextTransformApplier _textTransform;
    private readonly EditorTextClipboard? _textClipboard;
    private readonly IFormatPainterService? _formatPainter;
    private readonly EditorServices _services;
    private readonly IFormattingState? _formattingState;
    private readonly IStyleService? _styleService;
    private readonly ISelectionState? _selectionState;
    private readonly IClipboardService? _clipboardService;
    private readonly IFindReplaceService? _findReplaceService;

    private enum ChangeCaseMode
    {
        Sentence,
        Lower,
        Upper,
        Capitalize,
        Toggle
    }

    private enum TextEffectKind
    {
        Outline,
        Shadow,
        Emboss,
        Imprint
    }

    public EditorHomeCommandMap(
        EditorCommandRouterAdapter router,
        IEditorMutableSession session,
        EditorServices services,
        ISelectionState? selectionState = null,
        IFormattingState? formattingState = null,
        IStyleService? styleService = null,
        IClipboardService? clipboardService = null,
        IFindReplaceService? findReplaceService = null,
        IFormatPainterService? formatPainter = null,
        ITextContainerNormalizer? textNormalizer = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _selectionState = selectionState;
        _formattingState = formattingState;
        _styleService = styleService;
        _clipboardService = clipboardService;
        _findReplaceService = findReplaceService;
        _textFormatting = new EditorTextFormattingApplier(_session, textNormalizer);
        _paragraphFormatting = new EditorParagraphApplier(_session);
        _textTransform = new EditorTextTransformApplier(_session);
        _textClipboard = clipboardService is null ? null : new EditorTextClipboard(_session, clipboardService);
        _formatPainter = formatPainter;
    }

    public void Register()
    {
        RegisterClipboardCommands();
        RegisterFontCommands();
        RegisterParagraphCommands();
        RegisterStyleCommands();
        RegisterEditingCommands();
    }

    private void RegisterClipboardCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.Copy, (_, __) => CopySelection(), _ => _clipboardService?.CanCopy ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.Cut, (_, __) => CutSelection(), _ => _clipboardService?.CanCut ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.Paste, (_, __) => PasteClipboard(), _ => _clipboardService?.CanPaste ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.PasteKeepSource, (_, __) => PasteClipboard(), _ => _clipboardService?.CanPaste ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.PasteMatchDestination, (_, __) => PasteClipboard(), _ => _clipboardService?.CanPaste ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.PasteTextOnly, (_, __) => PasteClipboard(), _ => _clipboardService?.CanPaste ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.FormatPainterToggle, (_, __) => ToggleFormatPainter(), _ => HasParagraphs());
    }

    private void RegisterFontCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Font.FamilySet, (_, payload) => SetFontFamily(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.SizeSet, (_, payload) => SetFontSize(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.SizeIncrease, (_, payload) => AdjustFontSize(payload, FontSizeStep), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.SizeDecrease, (_, payload) => AdjustFontSize(payload, -FontSizeStep), _ => HasParagraphs());

        _router.RegisterAction(EditorHomeCommandIds.Font.BoldToggle, (_, __) => ToggleBold(), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ItalicToggle, (_, __) => ToggleItalic(), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.UnderlineToggle, (_, __) => ToggleUnderline(), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.UnderlineStyleSet, (_, payload) => SetUnderlineStyle(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.StrikethroughToggle, (_, __) => ToggleStrikethrough(), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.SuperscriptToggle, (_, __) => ToggleVerticalPosition(DocVerticalPosition.Superscript), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.SubscriptToggle, (_, __) => ToggleVerticalPosition(DocVerticalPosition.Subscript), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectOutline, (_, __) => ApplyTextEffect(TextEffectKind.Outline), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectShadow, (_, __) => ApplyTextEffect(TextEffectKind.Shadow), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectEmboss, (_, __) => ApplyTextEffect(TextEffectKind.Emboss), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectImprint, (_, __) => ApplyTextEffect(TextEffectKind.Imprint), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectClear, (_, __) => ClearTextEffects(), _ => HasParagraphs());

        _router.RegisterAction(EditorHomeCommandIds.Font.HighlightSet, (_, payload) => SetHighlightColor(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ColorSet, (_, payload) => SetFontColor(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ClearFormatting, (_, __) => _textFormatting.ClearFormatting(), _ => HasParagraphs());

        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseSentence, (_, __) => ApplyChangeCase(ChangeCaseMode.Sentence), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseLower, (_, __) => ApplyChangeCase(ChangeCaseMode.Lower), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseUpper, (_, __) => ApplyChangeCase(ChangeCaseMode.Upper), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseCapitalize, (_, __) => ApplyChangeCase(ChangeCaseMode.Capitalize), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseToggle, (_, __) => ApplyChangeCase(ChangeCaseMode.Toggle), _ => HasParagraphs());
    }

    private void RegisterParagraphCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignLeft, (_, __) => SetParagraphAlignment(ParagraphAlignment.Left), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignCenter, (_, __) => SetParagraphAlignment(ParagraphAlignment.Center), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignRight, (_, __) => SetParagraphAlignment(ParagraphAlignment.Right), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignJustify, (_, __) => SetParagraphAlignment(ParagraphAlignment.Justify), _ => HasParagraphs());

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.IndentIncrease, (_, __) => AdjustIndent(IndentStep), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.IndentDecrease, (_, __) => AdjustIndent(-IndentStep), _ => HasParagraphs());

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ListBullets, (_, __) => ToggleList(ListKind.Bullet), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ListNumbering, (_, __) => ToggleList(ListKind.Numbered), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ListMultilevel, (_, __) => ToggleList(ListKind.Numbered), _ => HasParagraphs());

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.LineSpacingSet, (_, payload) => SetLineSpacing(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.LineSpacingOptions, (_, __) => { }, _ => false);

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.Sort, (_, __) => SortParagraphs(), _ => CanSortParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ShowInvisiblesToggle, (_, __) => { }, _ => false);
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ShadingSet, (_, payload) => ToggleShading(payload), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.BorderSet, (_, payload) => ToggleBorders(payload), _ => HasParagraphs());
    }

    private void RegisterStyleCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Styles.Apply, (_, payload) => ApplyParagraphStyle(payload), CanApplyStyle);
        _router.RegisterAction(EditorHomeCommandIds.Styles.OpenPane, (_, __) => OpenStylesPane(), _ => CanOpenStylesPane());
        _router.RegisterAction(EditorHomeCommandIds.Styles.Manage, (_, __) => OpenStylesManager(), _ => CanOpenStylesPane());
    }

    private void RegisterEditingCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Editing.Find, (_, payload) => ExecuteFind(payload), _ => _findReplaceService?.IsAvailable ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Editing.Replace, (_, payload) => ExecuteReplace(payload), _ => _findReplaceService?.IsAvailable ?? false);
        _router.RegisterAction(EditorHomeCommandIds.Editing.SelectAll, (_, __) => SelectAll(), _ => HasParagraphs());
        _router.RegisterAction(EditorHomeCommandIds.Editing.SelectObjects, (_, __) => SelectObjects(), _ => CanSelectObjects());
        _router.RegisterAction(EditorHomeCommandIds.Editing.SelectSimilarFormatting, (_, __) => SelectSimilarFormatting(), _ => HasParagraphs());
    }

    private bool HasParagraphs()
    {
        if (_selectionState is not null && _selectionState.GetSnapshot().Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private void CopySelection()
    {
        _textClipboard?.CopySelection();
    }

    private void CutSelection()
    {
        _textClipboard?.CutSelection();
    }

    private void PasteClipboard()
    {
        _textClipboard?.PasteText();
    }

    private void ToggleFormatPainter()
    {
        _formatPainter?.Toggle();
    }

    private void ApplyChangeCase(ChangeCaseMode mode)
    {
        if (!TryResolveChangeCaseRange(out var range))
        {
            return;
        }

        _textTransform.Apply(range, span => TransformCase(span, mode));
    }

    private bool TryResolveChangeCaseRange(out TextRange range)
    {
        var selection = _session.Selection.Normalize();
        if (!selection.IsEmpty)
        {
            range = selection;
            return true;
        }

        return TryGetWordRange(selection.Start, out range);
    }

    private bool TryGetWordRange(TextPosition caret, out TextRange range)
    {
        range = default;
        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var paragraphIndex = Math.Clamp(caret.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var paragraph = _session.Document.GetParagraph(paragraphIndex);
        var text = GetParagraphText(paragraph);
        if (text.Length == 0)
        {
            return false;
        }

        var offset = Math.Clamp(caret.Offset, 0, text.Length);
        if (offset == text.Length && offset > 0)
        {
            offset -= 1;
        }

        if (offset < text.Length && !IsWordChar(text[offset]) && offset > 0 && IsWordChar(text[offset - 1]))
        {
            offset -= 1;
        }

        if (offset >= text.Length || !IsWordChar(text[offset]))
        {
            return false;
        }

        var start = offset;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start -= 1;
        }

        var end = offset;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end += 1;
        }

        if (start >= end)
        {
            return false;
        }

        range = new TextRange(new TextPosition(paragraphIndex, start), new TextPosition(paragraphIndex, end));
        return true;
    }

    private static string GetParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return paragraph.Text ?? string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    builder.Append(run.Text.GetText());
                    break;
                case ImageInline:
                case ShapeInline:
                case ChartInline:
                case EquationInline:
                case PageNumberInline:
                case TotalPagesInline:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
                case MetadataStartInline:
                case MetadataEndInline:
                    break;
                case FootnoteReferenceInline footnote:
                    builder.Append(footnote.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case EndnoteReferenceInline endnote:
                    builder.Append(endnote.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommentReferenceInline comment:
                    builder.Append(comment.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case FieldStartInline:
                case FieldSeparatorInline:
                case FieldEndInline:
                case BookmarkStartInline:
                case BookmarkEndInline:
                case CommentRangeStartInline:
                case CommentRangeEndInline:
                case ContentControlStartInline:
                case ContentControlEndInline:
                    break;
                default:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsWordChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '\'';
    }

    private static string TransformCase(ReadOnlySpan<char> text, ChangeCaseMode mode)
    {
        return mode switch
        {
            ChangeCaseMode.Lower => TransformLower(text),
            ChangeCaseMode.Upper => TransformUpper(text),
            ChangeCaseMode.Toggle => TransformToggle(text),
            ChangeCaseMode.Capitalize => TransformCapitalize(text),
            ChangeCaseMode.Sentence => TransformSentenceCase(text),
            _ => text.ToString()
        };
    }

    private static string TransformLower(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.ToLowerInvariant(source[i]);
            }
        });
    }

    private static string TransformUpper(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.ToUpperInvariant(source[i]);
            }
        });
    }

    private static string TransformToggle(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                if (!char.IsLetter(value))
                {
                    span[i] = value;
                    continue;
                }

                span[i] = char.IsUpper(value)
                    ? char.ToLowerInvariant(value)
                    : char.ToUpperInvariant(value);
            }
        });
    }

    private static string TransformCapitalize(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            var newWord = true;
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                if (IsWordChar(value))
                {
                    span[i] = newWord ? char.ToUpperInvariant(value) : char.ToLowerInvariant(value);
                    newWord = false;
                }
                else
                {
                    span[i] = value;
                    newWord = true;
                }
            }
        });
    }

    private static string TransformSentenceCase(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            var newSentence = true;
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                if (IsWordChar(value))
                {
                    span[i] = newSentence ? char.ToUpperInvariant(value) : char.ToLowerInvariant(value);
                    newSentence = false;
                    continue;
                }

                span[i] = value;
                if (IsSentenceBoundary(value))
                {
                    newSentence = true;
                }
            }
        });
    }

    private static bool IsSentenceBoundary(char value)
    {
        return value is '.' or '!' or '?' or '\r' or '\n';
    }

    private void ToggleBold()
    {
        var target = ResolveToggle(_formattingState?.GetSnapshot().Bold);
        _textFormatting.Apply(style => style.FontWeight = target ? DocFontWeight.Bold : DocFontWeight.Normal);
    }

    private void ToggleItalic()
    {
        var target = ResolveToggle(_formattingState?.GetSnapshot().Italic);
        _textFormatting.Apply(style => style.FontStyle = target ? DocFontStyle.Italic : DocFontStyle.Normal);
    }

    private void ToggleUnderline()
    {
        var snapshot = _formattingState?.GetSnapshot();
        var target = ResolveToggle(snapshot?.Underline);
        var underlineStyle = snapshot?.UnderlineStyle;
        var styleValue = DocUnderlineStyle.Single;
        if (underlineStyle.HasValue && underlineStyle.Value.HasValue && !underlineStyle.Value.IsMixed)
        {
            var current = underlineStyle.Value.Value;
            if (current != DocUnderlineStyle.None)
            {
                styleValue = current;
            }
        }

        if (!target)
        {
            styleValue = DocUnderlineStyle.None;
        }

        _textFormatting.Apply(style =>
        {
            style.Underline = target;
            style.UnderlineStyle = styleValue;
        });
    }

    private void SetUnderlineStyle(object? payload)
    {
        if (!TryGetUnderlineStyle(payload, out var style))
        {
            return;
        }

        _textFormatting.Apply(properties =>
        {
            properties.UnderlineStyle = style;
            properties.Underline = style != DocUnderlineStyle.None;
        });
    }

    private void ToggleStrikethrough()
    {
        var target = ResolveToggle(_formattingState?.GetSnapshot().Strikethrough);
        _textFormatting.Apply(style => style.Strikethrough = target);
    }

    private void ToggleVerticalPosition(DocVerticalPosition target)
    {
        var current = _formattingState?.GetSnapshot().VerticalPosition;
        var next = target;
        if (current.HasValue && current.Value.HasValue && !current.Value.IsMixed)
        {
            var currentValue = current.Value.Value;
            next = currentValue == target ? DocVerticalPosition.Normal : target;
        }

        _textFormatting.Apply(style => style.VerticalPosition = next);
    }

    private void SetFontFamily(object? payload)
    {
        if (!TryGetString(payload, out var family))
        {
            return;
        }

        _textFormatting.Apply(style => style.FontFamily = family);
    }

    private void SetFontSize(object? payload)
    {
        if (!TryGetFloat(payload, out var size))
        {
            return;
        }

        var clamped = Math.Max(MinimumFontSize, size);
        _textFormatting.Apply(style => style.FontSize = clamped);
    }

    private void AdjustFontSize(object? payload, float delta)
    {
        var baseSize = ResolveBaseFontSize();

        if (TryGetFloat(payload, out var customStep))
        {
            var step = MathF.Abs(customStep) * MathF.Sign(delta);
            var next = Math.Max(MinimumFontSize, baseSize + step);
            _textFormatting.Apply(style => style.FontSize = next);
            return;
        }

        var snapped = ResolveNextStandardFontSize(baseSize, delta);
        _textFormatting.Apply(style => style.FontSize = snapped);
    }

    private void SetFontColor(object? payload)
    {
        if (payload is DocColor color)
        {
            _textFormatting.Apply(style => style.Color = color);
            return;
        }

        if (payload is null)
        {
            _textFormatting.Apply(style => style.Color = null);
        }
    }

    private void SetHighlightColor(object? payload)
    {
        if (payload is DocColor color)
        {
            _textFormatting.Apply(style => style.HighlightColor = color);
            return;
        }

        if (payload is null)
        {
            _textFormatting.Apply(style => style.HighlightColor = null);
        }
    }

    private void ApplyTextEffect(TextEffectKind kind)
    {
        var enable = ResolveEffectToggle(kind);
        _textFormatting.Apply(style =>
        {
            var effects = style.Effects;
            if (enable)
            {
                effects ??= new TextEffects();
                switch (kind)
                {
                    case TextEffectKind.Outline:
                        effects.Outline = new TextOutlineEffect
                        {
                            Enabled = true,
                            Color = style.Color ?? DocColor.Black,
                            Thickness = 1f
                        };
                        break;
                    case TextEffectKind.Shadow:
                        effects.Shadow = new TextShadowEffect
                        {
                            Enabled = true,
                            Color = DocColor.Black,
                            BlurRadius = 1.5f,
                            Distance = 1.5f,
                            Direction = 45f
                        };
                        break;
                    case TextEffectKind.Emboss:
                        effects.Emboss = true;
                        break;
                    case TextEffectKind.Imprint:
                        effects.Imprint = true;
                        break;
                    default:
                        break;
                }

                style.Effects = effects;
                return;
            }

            if (effects is null)
            {
                return;
            }

            switch (kind)
            {
                case TextEffectKind.Outline:
                    effects.Outline = null;
                    break;
                case TextEffectKind.Shadow:
                    effects.Shadow = null;
                    break;
                case TextEffectKind.Emboss:
                    effects.Emboss = null;
                    break;
                case TextEffectKind.Imprint:
                    effects.Imprint = null;
                    break;
                default:
                    break;
            }

            if (!effects.HasValues)
            {
                style.Effects = null;
            }
        });
    }

    private void ClearTextEffects()
    {
        _textFormatting.Apply(style => style.Effects = null);
    }

    private bool ResolveEffectToggle(TextEffectKind kind)
    {
        if (_formattingState is null)
        {
            return true;
        }

        var snapshot = _formattingState.GetSnapshot();
        var current = kind switch
        {
            TextEffectKind.Outline => snapshot.TextOutline,
            TextEffectKind.Shadow => snapshot.TextShadow,
            TextEffectKind.Emboss => snapshot.TextEmboss,
            TextEffectKind.Imprint => snapshot.TextImprint,
            _ => EditorValue<bool>.Missing()
        };

        return ResolveToggle(current);
    }

    private void SetParagraphAlignment(ParagraphAlignment alignment)
    {
        _paragraphFormatting.Apply(paragraph => paragraph.Properties.Alignment = alignment);
    }

    private void AdjustIndent(float delta)
    {
        _paragraphFormatting.Apply(paragraph =>
        {
            var current = paragraph.Properties.IndentLeft ?? 0f;
            paragraph.Properties.IndentLeft = Math.Max(0f, current + delta);
        });
    }

    private void ToggleList(ListKind kind)
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var allMatch = true;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            if (paragraph.ListInfo?.Kind != kind)
            {
                allMatch = false;
                break;
            }
        }

        var targetKind = allMatch ? ListKind.None : kind;
        _paragraphFormatting.Apply(paragraph =>
        {
            if (targetKind == ListKind.None)
            {
                paragraph.ListInfo = null;
                return;
            }

            var existing = paragraph.ListInfo;
            var level = existing?.Level ?? 0;
            var listId = existing?.ListId;
            paragraph.ListInfo = new ListInfo(targetKind, level, listId);
        });
    }

    private void SetLineSpacing(object? payload)
    {
        if (!TryResolveLineSpacing(payload, out var lineSpacing, out var rule))
        {
            return;
        }

        _paragraphFormatting.Apply(paragraph =>
        {
            paragraph.Properties.LineSpacing = lineSpacing;
            paragraph.Properties.LineSpacingRule = rule;
        });
    }

    private bool CanSortParagraphs()
    {
        return TryGetSortableRange(out _, out _, out _, out _);
    }

    private void SortParagraphs()
    {
        if (!TryGetSortableRange(out var startIndex, out var endIndex, out var anchor, out var locations))
        {
            return;
        }

        var items = new List<(ParagraphBlock Paragraph, string Key, int Index)>(endIndex - startIndex + 1);
        for (var i = 0; i < locations.Count; i++)
        {
            var paragraph = locations[i].Paragraph;
            var key = GetParagraphSortKey(paragraph);
            items.Add((paragraph, key, i));
        }

        items.Sort((left, right) =>
        {
            var result = string.Compare(left.Key, right.Key, StringComparison.CurrentCultureIgnoreCase);
            return result != 0 ? result : left.Index.CompareTo(right.Index);
        });

        if (anchor.IsInTable)
        {
            var cell = anchor.Cell;
            if (cell is null)
            {
                return;
            }

            var startCellIndex = locations[0].ParagraphIndexInCell;
            for (var i = 0; i < items.Count; i++)
            {
                cell.Paragraphs[startCellIndex + i] = items[i].Paragraph;
            }
        }
        else
        {
            var startBlockIndex = anchor.BlockIndex;
            for (var i = 0; i < items.Count; i++)
            {
                _session.Document.Blocks[startBlockIndex + i] = items[i].Paragraph;
            }
        }

        _session.RefreshLayout();
    }

    private bool TryGetSortableRange(
        out int startIndex,
        out int endIndex,
        out ParagraphLocation anchor,
        out List<ParagraphLocation> locations)
    {
        startIndex = 0;
        endIndex = 0;
        anchor = default;
        locations = new List<ParagraphLocation>();

        if (_session.Document.ParagraphCount < 2)
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        if (startIndex == endIndex)
        {
            return false;
        }

        anchor = _session.Document.GetParagraphLocation(startIndex);
        locations = new List<ParagraphLocation>(endIndex - startIndex + 1);
        for (var i = startIndex; i <= endIndex; i++)
        {
            var location = _session.Document.GetParagraphLocation(i);
            if (!location.IsSameContainer(anchor))
            {
                return false;
            }

            locations.Add(location);
        }

        if (anchor.IsInTable)
        {
            var startCellIndex = locations[0].ParagraphIndexInCell;
            for (var i = 0; i < locations.Count; i++)
            {
                if (locations[i].ParagraphIndexInCell != startCellIndex + i)
                {
                    return false;
                }
            }
        }
        else
        {
            var startBlockIndex = anchor.BlockIndex;
            for (var i = 0; i < locations.Count; i++)
            {
                if (locations[i].BlockIndex != startBlockIndex + i)
                {
                    return false;
                }

                if (_session.Document.Blocks[startBlockIndex + i] is not ParagraphBlock)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string GetParagraphSortKey(ParagraphBlock paragraph)
    {
        var text = GetParagraphText(paragraph);
        return text.Trim();
    }

    private void ToggleShading(object? payload)
    {
        if (payload is DocColor color)
        {
            _paragraphFormatting.Apply(paragraph => paragraph.Properties.ShadingColor = color);
            return;
        }

        _paragraphFormatting.Apply(paragraph =>
        {
            paragraph.Properties.ShadingColor = paragraph.Properties.ShadingColor.HasValue
                ? null
                : DefaultShadingColor;
        });
    }

    private void ToggleBorders(object? payload)
    {
        BorderLine? border = payload switch
        {
            BorderLine line => line,
            DocColor color => new BorderLine { Style = DocBorderStyle.Single, Thickness = 1f, Color = color },
            _ => null
        };

        _paragraphFormatting.Apply(paragraph =>
        {
            if (border is null)
            {
                if (paragraph.Properties.Borders.HasAny)
                {
                    ClearBorders(paragraph.Properties.Borders);
                    return;
                }

                ApplyBorders(paragraph.Properties.Borders, DefaultBorderLine);
                return;
            }

            ApplyBorders(paragraph.Properties.Borders, border);
        });
    }

    private static void ApplyBorders(ParagraphBorders target, BorderLine line)
    {
        var resolved = line.Clone();
        target.Top = resolved.Clone();
        target.Bottom = resolved.Clone();
        target.Left = resolved.Clone();
        target.Right = resolved.Clone();
    }

    private static void ClearBorders(ParagraphBorders target)
    {
        target.Top = null;
        target.Bottom = null;
        target.Left = null;
        target.Right = null;
    }

    private void ApplyParagraphStyle(object? payload)
    {
        if (!TryGetString(payload, out var styleId))
        {
            return;
        }

        _styleService?.ApplyParagraphStyle(styleId);
    }

    private bool CanApplyStyle(object? payload)
    {
        if (_styleService is null)
        {
            return false;
        }

        if (!TryGetString(payload, out var styleId))
        {
            return false;
        }

        return _styleService.GetParagraphStyle(styleId) is not null;
    }

    private void OpenStylesPane()
    {
        if (TryGetStylePaneService(out var service))
        {
            service.OpenStylesPane();
        }
    }

    private void OpenStylesManager()
    {
        if (TryGetStylePaneService(out var service))
        {
            service.OpenStylesManager();
        }
    }

    private bool CanOpenStylesPane()
    {
        return TryGetStylePaneService(out _);
    }

    private bool TryGetStylePaneService(out IStylePaneService service)
    {
        return _services.TryGet(out service);
    }

    private void SelectAll()
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var lastIndex = _session.Document.ParagraphCount - 1;
        var lastParagraph = _session.Document.GetParagraph(lastIndex);
        var endOffset = DocumentEditHelpers.GetParagraphLength(lastParagraph);
        _session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(lastIndex, endOffset)));
    }

    private bool CanSelectObjects()
    {
        return _session.Layout.FloatingObjects.Count > 0;
    }

    private void SelectObjects()
    {
        _session.TrySelectFirstFloatingObject();
    }

    private void SelectSimilarFormatting()
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var selection = _session.Selection.Normalize();
        var anchorIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var anchorLocation = _session.Document.GetParagraphLocation(anchorIndex);
        var targetStyle = GetParagraphStyleId(anchorLocation.Paragraph);

        var startIndex = anchorIndex;
        while (startIndex > 0)
        {
            var candidateIndex = startIndex - 1;
            var location = _session.Document.GetParagraphLocation(candidateIndex);
            if (!location.IsSameContainer(anchorLocation))
            {
                break;
            }

            if (!IsParagraphStyleMatch(location.Paragraph, targetStyle))
            {
                break;
            }

            startIndex = candidateIndex;
        }

        var endIndex = anchorIndex;
        while (endIndex < _session.Document.ParagraphCount - 1)
        {
            var candidateIndex = endIndex + 1;
            var location = _session.Document.GetParagraphLocation(candidateIndex);
            if (!location.IsSameContainer(anchorLocation))
            {
                break;
            }

            if (!IsParagraphStyleMatch(location.Paragraph, targetStyle))
            {
                break;
            }

            endIndex = candidateIndex;
        }

        var endParagraph = _session.Document.GetParagraph(endIndex);
        var endOffset = DocumentEditHelpers.GetParagraphLength(endParagraph);
        _session.SetSelection(new TextRange(new TextPosition(startIndex, 0), new TextPosition(endIndex, endOffset)));
    }

    private string GetParagraphStyleId(ParagraphBlock paragraph)
    {
        var defaultId = _session.Document.Styles.DefaultParagraphStyleId;
        return paragraph.StyleId ?? defaultId ?? string.Empty;
    }

    private bool IsParagraphStyleMatch(ParagraphBlock paragraph, string styleId)
    {
        var current = paragraph.StyleId ?? _session.Document.Styles.DefaultParagraphStyleId ?? string.Empty;
        return string.Equals(current, styleId, StringComparison.OrdinalIgnoreCase);
    }

    private void ExecuteFind(object? payload)
    {
        if (_findReplaceService is null)
        {
            return;
        }

        if (payload is EditorFindQuery query)
        {
            _findReplaceService.TryFindNext(query, out _);
        }
    }

    private void ExecuteReplace(object? payload)
    {
        if (_findReplaceService is null)
        {
            return;
        }

        if (payload is EditorReplaceQuery query)
        {
            _findReplaceService.TryReplaceNext(query, out _);
        }
    }

    private static bool ResolveToggle(EditorValue<bool>? value)
    {
        if (!value.HasValue)
        {
            return true;
        }

        return value.Value.IsMixed || !value.Value.HasValue || !value.Value.Value;
    }

    private static bool TryGetString(object? payload, out string value)
    {
        if (payload is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetFloat(object? payload, out float value)
    {
        switch (payload)
        {
            case float floatValue:
                value = floatValue;
                return true;
            case double doubleValue:
                value = (float)doubleValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case string text when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0f;
                return false;
        }
    }

    private float ResolveBaseFontSize()
    {
        var current = _formattingState?.GetSnapshot().FontSize;
        var baseSize = _session.Document.DefaultTextStyle.FontSize;
        if (current.HasValue && current.Value.HasValue && !current.Value.IsMixed)
        {
            baseSize = current.Value.Value;
        }

        return baseSize;
    }

    private static float ResolveNextStandardFontSize(float baseSize, float delta)
    {
        if (StandardFontSizes.Length == 0)
        {
            return Math.Max(MinimumFontSize, baseSize + delta);
        }

        if (delta > 0f)
        {
            foreach (var size in StandardFontSizes)
            {
                if (size > baseSize)
                {
                    return size;
                }
            }

            return Math.Max(MinimumFontSize, baseSize + FontSizeStep);
        }

        if (delta < 0f)
        {
            for (var i = StandardFontSizes.Length - 1; i >= 0; i--)
            {
                if (StandardFontSizes[i] < baseSize)
                {
                    return StandardFontSizes[i];
                }
            }

            return Math.Max(MinimumFontSize, baseSize - FontSizeStep);
        }

        return Math.Max(MinimumFontSize, baseSize);
    }

    private static bool TryGetUnderlineStyle(object? payload, out DocUnderlineStyle style)
    {
        if (payload is DocUnderlineStyle underlineStyle)
        {
            style = underlineStyle;
            return true;
        }

        if (payload is string text && Enum.TryParse<DocUnderlineStyle>(text, true, out var parsed))
        {
            style = parsed;
            return true;
        }

        style = DocUnderlineStyle.None;
        return false;
    }

    private static bool TryResolveLineSpacing(object? payload, out int lineSpacing, out DocLineSpacingRule rule)
    {
        if (payload is EditorLineSpacingRequest request)
        {
            if (request.Twips.HasValue)
            {
                lineSpacing = request.Twips.Value;
                rule = request.Rule ?? DocLineSpacingRule.AtLeast;
                return true;
            }

            if (request.Multiple.HasValue)
            {
                lineSpacing = (int)MathF.Round(request.Multiple.Value * LineSpacingTwipsPerLine);
                rule = request.Rule ?? DocLineSpacingRule.Auto;
                return true;
            }
        }

        if (TryGetFloat(payload, out var multiple))
        {
            lineSpacing = (int)MathF.Round(multiple * LineSpacingTwipsPerLine);
            rule = DocLineSpacingRule.Auto;
            return true;
        }

        lineSpacing = 0;
        rule = DocLineSpacingRule.Auto;
        return false;
    }
}
