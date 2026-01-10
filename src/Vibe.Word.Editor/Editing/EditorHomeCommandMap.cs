using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorHomeCommandMap
{
    private const float IndentStep = 24f;
    private const float FontSizeStep = 1f;
    private const int MaxListLevel = 8;
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
    private readonly IEditorViewOptionsService? _viewOptions;

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
        _viewOptions = services.TryGet<IEditorViewOptionsService>(out var viewOptions) ? viewOptions : null;
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
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.Copy, (_, __) => CopySelection(), (context, _) => CanCopy(context), isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.Cut, (_, __) => CutSelection(), (context, _) => CanCut(context));
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.Paste, (_, __) => PasteClipboard(), (context, _) => CanPaste(context));
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.PasteKeepSource, (_, __) => PasteClipboard(), (context, _) => CanPaste(context));
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.PasteMatchDestination, (_, __) => PasteClipboard(), (context, _) => CanPaste(context));
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.PasteTextOnly, (_, __) => PasteClipboard(), (context, _) => CanPaste(context));
        _router.RegisterAction(EditorHomeCommandIds.Clipboard.FormatPainterToggle, (_, __) => ToggleFormatPainter(), (context, _) => HasParagraphs(context), isUndoable: false);
    }

    private void RegisterFontCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Font.FamilySet, (_, payload) => SetFontFamily(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.SizeSet, (_, payload) => SetFontSize(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.SizeIncrease, (_, payload) => AdjustFontSize(payload, FontSizeStep), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.SizeDecrease, (_, payload) => AdjustFontSize(payload, -FontSizeStep), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Font.BoldToggle, (_, __) => ToggleBold(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ItalicToggle, (_, __) => ToggleItalic(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.UnderlineToggle, (_, __) => ToggleUnderline(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.UnderlineStyleSet, (_, payload) => SetUnderlineStyle(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.StrikethroughToggle, (_, __) => ToggleStrikethrough(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.SuperscriptToggle, (_, __) => ToggleVerticalPosition(DocVerticalPosition.Superscript), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.SubscriptToggle, (_, __) => ToggleVerticalPosition(DocVerticalPosition.Subscript), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.DialogApply, (_, payload) => ApplyFontDialogOptions(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectOutline, (_, __) => ApplyTextEffect(TextEffectKind.Outline), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectShadow, (_, __) => ApplyTextEffect(TextEffectKind.Shadow), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectEmboss, (_, __) => ApplyTextEffect(TextEffectKind.Emboss), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectImprint, (_, __) => ApplyTextEffect(TextEffectKind.Imprint), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.TextEffectClear, (_, __) => ClearTextEffects(), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Font.HighlightSet, (_, payload) => SetHighlightColor(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ColorSet, (_, payload) => SetFontColor(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ClearFormatting, (_, __) => _textFormatting.ClearFormatting(), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseSentence, (_, __) => ApplyChangeCase(ChangeCaseMode.Sentence), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseLower, (_, __) => ApplyChangeCase(ChangeCaseMode.Lower), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseUpper, (_, __) => ApplyChangeCase(ChangeCaseMode.Upper), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseCapitalize, (_, __) => ApplyChangeCase(ChangeCaseMode.Capitalize), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Font.ChangeCaseToggle, (_, __) => ApplyChangeCase(ChangeCaseMode.Toggle), (context, _) => HasParagraphs(context));
    }

    private void RegisterParagraphCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignLeft, (_, __) => SetParagraphAlignment(ParagraphAlignment.Left), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignCenter, (_, __) => SetParagraphAlignment(ParagraphAlignment.Center), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignRight, (_, __) => SetParagraphAlignment(ParagraphAlignment.Right), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.AlignJustify, (_, __) => SetParagraphAlignment(ParagraphAlignment.Justify), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.IndentIncrease, (_, __) => AdjustIndent(IndentStep), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.IndentDecrease, (_, __) => AdjustIndent(-IndentStep), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ListBullets, (_, __) => ToggleList(ListKind.Bullet, multilevel: false), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ListNumbering, (_, __) => ToggleList(ListKind.Numbered, multilevel: false), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ListMultilevel, (_, __) => ToggleList(ListKind.Numbered, multilevel: true), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.LineSpacingSet, (_, payload) => SetLineSpacing(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.LineSpacingOptions, (_, payload) => SetParagraphSpacingOptions(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.DialogApply, (_, payload) => ApplyParagraphDialogOptions(payload), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorHomeCommandIds.Paragraph.Sort, (_, __) => SortParagraphs(), (context, _) => CanSortParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ShowInvisiblesToggle, (_, payload) => ToggleShowInvisibles(payload), (context, _) => CanToggleShowInvisibles(context), isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.ShadingSet, (_, payload) => ToggleShading(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorHomeCommandIds.Paragraph.BorderSet, (_, payload) => ToggleBorders(payload), (context, _) => HasParagraphs(context));
    }

    private void RegisterStyleCommands()
    {
        _router.RegisterAction(EditorHomeCommandIds.Styles.Apply, (_, payload) => ApplyParagraphStyle(payload), (context, payload) => CanApplyStyle(payload));
        _router.RegisterAction(EditorHomeCommandIds.Styles.OpenPane, (_, __) => OpenStylesPane(), (context, _) => CanOpenStylesPane(context), isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Styles.Manage, (_, __) => OpenStylesManager(), (context, _) => CanOpenStylesPane(context), isUndoable: false);
    }

    private void RegisterEditingCommands()
    {
        var undoRedo = _services.TryGet<IUndoRedoService>(out var undoService) ? undoService : null;
        _router.RegisterAction(
            EditorHomeCommandIds.Editing.Undo,
            (_, __) =>
            {
                if (undoRedo is not null)
                {
                    undoRedo.UndoAsync();
                }
            },
            (context, _) => context?.CanUndo ?? undoRedo?.CanUndo ?? false,
            isUndoable: false);
        _router.RegisterAction(
            EditorHomeCommandIds.Editing.Redo,
            (_, __) =>
            {
                if (undoRedo is not null)
                {
                    undoRedo.RedoAsync();
                }
            },
            (context, _) => context?.CanRedo ?? undoRedo?.CanRedo ?? false,
            isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Editing.Find, (_, payload) => ExecuteFind(payload), (context, _) => CanFind(context), isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Editing.Replace, (_, payload) => ExecuteReplace(payload), (context, _) => CanFind(context));
        _router.RegisterAction(EditorHomeCommandIds.Editing.ReplaceAll, (_, payload) => ExecuteReplaceAll(payload), (context, _) => CanFind(context));
        _router.RegisterAction(EditorHomeCommandIds.Editing.SelectAll, (_, __) => SelectAll(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Editing.SelectObjects, (_, __) => SelectObjects(), (context, _) => CanSelectObjects(context), isUndoable: false);
        _router.RegisterAction(EditorHomeCommandIds.Editing.SelectSimilarFormatting, (_, __) => SelectSimilarFormatting(), (context, _) => HasParagraphs(context), isUndoable: false);
    }

    private bool HasParagraphs()
    {
        if (_selectionState is not null && _selectionState.GetSnapshot().Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private bool HasParagraphs(RibbonContextSnapshot? context)
    {
        if (context.HasValue && context.Value.Selection.Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private bool CanCopy(RibbonContextSnapshot? context)
    {
        return context?.CanCopy ?? _clipboardService?.CanCopy ?? false;
    }

    private bool CanCut(RibbonContextSnapshot? context)
    {
        return context?.CanCut ?? _clipboardService?.CanCut ?? false;
    }

    private bool CanPaste(RibbonContextSnapshot? context)
    {
        return context?.CanPaste ?? _clipboardService?.CanPaste ?? false;
    }

    private bool CanFind(RibbonContextSnapshot? context)
    {
        return context?.IsFindAvailable ?? _findReplaceService?.IsAvailable ?? false;
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
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
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

    private void ApplyFontDialogOptions(object? payload)
    {
        if (payload is not EditorFontDialogOptions options)
        {
            return;
        }

        _textFormatting.Apply(style =>
        {
            if (!string.IsNullOrWhiteSpace(options.FontFamily))
            {
                style.FontFamily = options.FontFamily;
            }

            if (options.FontSize.HasValue)
            {
                style.FontSize = Math.Max(MinimumFontSize, options.FontSize.Value);
            }

            if (options.FontWeight.HasValue)
            {
                style.FontWeight = options.FontWeight.Value;
            }

            if (options.FontStyle.HasValue)
            {
                style.FontStyle = options.FontStyle.Value;
            }

            if (options.UnderlineStyle.HasValue)
            {
                style.UnderlineStyle = options.UnderlineStyle.Value;
                style.Underline = options.UnderlineStyle.Value != DocUnderlineStyle.None;
            }

            if (options.UnderlineColor.HasValue)
            {
                style.UnderlineColor = options.UnderlineColor;
            }

            if (options.FontColor.HasValue)
            {
                style.Color = options.FontColor.Value;
            }

            if (options.Strikethrough.HasValue)
            {
                style.Strikethrough = options.Strikethrough.Value;
            }

            if (options.SmallCaps.HasValue)
            {
                style.SmallCaps = options.SmallCaps.Value;
            }

            if (options.VerticalPosition.HasValue)
            {
                style.VerticalPosition = options.VerticalPosition.Value;
            }

            ApplyTextEffectsFromOptions(style, options);
        });
    }

    private static void ApplyTextEffectsFromOptions(TextStyleProperties style, EditorFontDialogOptions options)
    {
        if (!options.TextOutline.HasValue
            && !options.TextShadow.HasValue
            && !options.TextEmboss.HasValue
            && !options.TextImprint.HasValue)
        {
            return;
        }

        var effects = style.Effects?.Clone() ?? new TextEffects();
        if (options.TextOutline.HasValue)
        {
            effects.Outline = options.TextOutline.Value ? new TextOutlineEffect { Enabled = true } : null;
        }

        if (options.TextShadow.HasValue)
        {
            effects.Shadow = options.TextShadow.Value ? new TextShadowEffect { Enabled = true } : null;
        }

        if (options.TextEmboss.HasValue)
        {
            effects.Emboss = options.TextEmboss.Value;
        }

        if (options.TextImprint.HasValue)
        {
            effects.Imprint = options.TextImprint.Value;
        }

        style.Effects = effects.HasValues ? effects : null;
    }

    private void ApplyParagraphDialogOptions(object? payload)
    {
        if (payload is not EditorParagraphDialogOptions options)
        {
            return;
        }

        _paragraphFormatting.Apply(paragraph =>
        {
            var properties = paragraph.Properties;

            if (options.Alignment.HasValue)
            {
                properties.Alignment = options.Alignment.Value;
            }

            if (options.IndentLeft.HasValue)
            {
                properties.IndentLeft = options.IndentLeft;
            }

            if (options.IndentRight.HasValue)
            {
                properties.IndentRight = options.IndentRight;
            }

            if (options.FirstLineIndent.HasValue)
            {
                properties.FirstLineIndent = options.FirstLineIndent;
            }

            if (options.SpacingBefore.HasValue)
            {
                properties.SpacingBefore = options.SpacingBefore;
            }

            if (options.SpacingAfter.HasValue)
            {
                properties.SpacingAfter = options.SpacingAfter;
            }

            if (options.LineSpacing.HasValue)
            {
                properties.LineSpacing = options.LineSpacing;
            }

            if (options.LineSpacingRule.HasValue)
            {
                properties.LineSpacingRule = options.LineSpacingRule;
            }

            if (options.ContextualSpacing.HasValue)
            {
                properties.ContextualSpacing = options.ContextualSpacing.Value;
            }

            if (options.KeepWithNext.HasValue)
            {
                properties.KeepWithNext = options.KeepWithNext.Value;
            }

            if (options.KeepLinesTogether.HasValue)
            {
                properties.KeepLinesTogether = options.KeepLinesTogether.Value;
            }

            if (options.WidowControl.HasValue)
            {
                properties.WidowControl = options.WidowControl.Value;
            }

            if (options.PageBreakBefore.HasValue)
            {
                properties.PageBreakBefore = options.PageBreakBefore.Value;
            }

            if (options.SuppressLineNumbers.HasValue)
            {
                properties.SuppressLineNumbers = options.SuppressLineNumbers.Value;
            }

            if (options.Bidi.HasValue)
            {
                properties.Bidi = options.Bidi.Value;
            }

            if (options.TextDirection.HasValue)
            {
                properties.TextDirection = options.TextDirection.Value;
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
        if (TryAdjustListLevel(delta))
        {
            return;
        }

        _paragraphFormatting.Apply(paragraph =>
        {
            var current = paragraph.Properties.IndentLeft ?? 0f;
            paragraph.Properties.IndentLeft = Math.Max(0f, current + delta);
        });
    }

    private void ToggleList(ListKind kind, bool multilevel)
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
        var targetListId = targetKind == ListKind.None
            ? (int?)null
            : ResolveListId(kind);
        if (targetKind != ListKind.None)
        {
            targetListId = EnsureListDefinitionId(kind, targetListId, MaxListLevel, multilevel);
        }

        _paragraphFormatting.Apply(paragraph =>
        {
            if (targetKind == ListKind.None)
            {
                paragraph.ListInfo = null;
                return;
            }

            var existing = paragraph.ListInfo;
            var level = existing?.Kind == targetKind ? existing.Level : 0;
            paragraph.ListInfo = new ListInfo(targetKind, level, targetListId);
        });
    }

    private bool TryAdjustListLevel(float delta)
    {
        var step = delta > 0 ? 1 : delta < 0 ? -1 : 0;
        if (step == 0 || _session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var allInList = true;
        var resolvedKind = ListKind.None;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var info = _session.Document.GetParagraph(i).ListInfo;
            if (info is null || info.Kind == ListKind.None)
            {
                allInList = false;
                break;
            }

            if (resolvedKind == ListKind.None)
            {
                resolvedKind = info.Kind;
            }
        }

        if (!allInList || resolvedKind == ListKind.None)
        {
            return false;
        }

        var fallbackListId = ResolveListId(resolvedKind);
        _paragraphFormatting.Apply(paragraph =>
        {
            var info = paragraph.ListInfo;
            if (info is null || info.Kind == ListKind.None)
            {
                return;
            }

            var newLevel = Math.Clamp(info.Level + step, 0, MaxListLevel);
            if (newLevel == info.Level)
            {
                return;
            }

            var listId = info.ListId ?? fallbackListId;
            var multilevel = IsMultiLevelList(info, listId);
            listId = EnsureListDefinitionId(info.Kind, listId, newLevel, multilevel);
            paragraph.ListInfo = CloneListInfo(info, newLevel, listId);
        });

        return true;
    }

    private int? ResolveListId(ListKind kind)
    {
        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            var listInfo = _session.Document.GetParagraph(i).ListInfo;
            if (listInfo is not null && listInfo.Kind == kind && listInfo.ListId.HasValue)
            {
                return listInfo.ListId.Value;
            }
        }

        return null;
    }

    private int EnsureListDefinitionId(ListKind kind, int? listId, int maxLevel, bool multilevel)
    {
        var resolvedId = listId ?? AllocateListId();
        if (!_session.Document.ListDefinitions.TryGetValue(resolvedId, out var definition))
        {
            definition = kind switch
            {
                ListKind.Bullet => ListDefinitionDefaults.CreateBulleted(resolvedId, MaxListLevel + 1),
                ListKind.Numbered => ListDefinitionDefaults.CreateNumbered(resolvedId, multilevel, MaxListLevel + 1),
                _ => ListDefinitionDefaults.CreateNumbered(resolvedId, multilevel, MaxListLevel + 1)
            };

            _session.Document.ListDefinitions[resolvedId] = definition;
            return resolvedId;
        }

        if (kind == ListKind.Numbered)
        {
            multilevel = multilevel || IsMultiLevelDefinition(definition);
        }

        ListDefinitionDefaults.EnsureLevels(definition, kind, maxLevel, multilevel);
        return resolvedId;
    }

    private int AllocateListId()
    {
        var nextId = 1;
        if (_session.Document.ListDefinitions.Count > 0)
        {
            nextId = _session.Document.ListDefinitions.Keys.Max() + 1;
        }

        return nextId;
    }

    private bool IsMultiLevelList(ListInfo info, int? listId)
    {
        if (listId.HasValue
            && _session.Document.ListDefinitions.TryGetValue(listId.Value, out var definition))
        {
            return IsMultiLevelDefinition(definition);
        }

        return IsMultiLevelText(info.LevelText);
    }

    private static bool IsMultiLevelDefinition(ListDefinition definition)
    {
        foreach (var level in definition.Levels.Values)
        {
            if (IsMultiLevelText(level.LevelText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMultiLevelText(string? levelText)
    {
        if (string.IsNullOrWhiteSpace(levelText))
        {
            return false;
        }

        var count = 0;
        foreach (var ch in levelText)
        {
            if (ch == '%' && ++count > 1)
            {
                return true;
            }
        }

        return false;
    }

    private static ListInfo CloneListInfo(ListInfo source, int level, int? listId)
    {
        return new ListInfo(source.Kind, level, listId)
        {
            NumberFormat = source.NumberFormat,
            LevelText = source.LevelText,
            BulletSymbol = source.BulletSymbol,
            StartAt = source.StartAt,
            LeftIndent = source.LeftIndent,
            HangingIndent = source.HangingIndent,
            TabStop = source.TabStop
        };
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

    private void SetParagraphSpacingOptions(object? payload)
    {
        if (payload is not EditorParagraphSpacingOptions options)
        {
            return;
        }

        _paragraphFormatting.Apply(paragraph =>
        {
            paragraph.Properties.SpacingBefore = options.SpacingBefore;
            paragraph.Properties.SpacingAfter = options.SpacingAfter;
            paragraph.Properties.LineSpacing = options.LineSpacing;
            paragraph.Properties.LineSpacingRule = options.LineSpacingRule;
        });
    }

    private bool CanSortParagraphs()
    {
        return TryGetSortableRange(out _, out _, out _, out _);
    }

    private bool CanSortParagraphs(RibbonContextSnapshot? context)
    {
        if (context.HasValue && context.Value.Selection.Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

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
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
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
        if (payload is EditorParagraphBorderRequest request)
        {
            _paragraphFormatting.Apply(paragraph =>
                ApplyBorderRequest(paragraph.Properties.Borders, request));
            return;
        }

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

    private static void ApplyBorderRequest(ParagraphBorders target, EditorParagraphBorderRequest request)
    {
        switch (request.Kind)
        {
            case EditorParagraphBorderKind.None:
                ClearBorders(target);
                return;
            case EditorParagraphBorderKind.All:
            case EditorParagraphBorderKind.Outside:
                ApplyBorders(target, DefaultBorderLine);
                return;
            case EditorParagraphBorderKind.Top:
                ClearBorders(target);
                target.Top = DefaultBorderLine.Clone();
                return;
            case EditorParagraphBorderKind.Bottom:
                ClearBorders(target);
                target.Bottom = DefaultBorderLine.Clone();
                return;
            case EditorParagraphBorderKind.Left:
                ClearBorders(target);
                target.Left = DefaultBorderLine.Clone();
                return;
            case EditorParagraphBorderKind.Right:
                ClearBorders(target);
                target.Right = DefaultBorderLine.Clone();
                return;
            default:
                ApplyBorders(target, DefaultBorderLine);
                return;
        }
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

    private bool CanOpenStylesPane(RibbonContextSnapshot? context)
    {
        return CanOpenStylesPane();
    }

    private bool TryGetStylePaneService(out IStylePaneService service)
    {
        return _services.TryGet(out service);
    }

    private bool CanToggleShowInvisibles()
    {
        return TryGetViewOptionsService(out _);
    }

    private bool CanToggleShowInvisibles(RibbonContextSnapshot? context)
    {
        return CanToggleShowInvisibles();
    }

    private void ToggleShowInvisibles(object? payload)
    {
        if (!TryGetViewOptionsService(out var service))
        {
            return;
        }

        var value = payload is bool isEnabled ? isEnabled : !service.ShowInvisibles;
        service.ShowInvisibles = value;
    }

    private bool TryGetViewOptionsService(out IEditorViewOptionsService service)
    {
        if (_viewOptions is not null)
        {
            service = _viewOptions;
            return true;
        }

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

    private bool CanSelectObjects(RibbonContextSnapshot? context)
    {
        return CanSelectObjects();
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

    private void ExecuteReplaceAll(object? payload)
    {
        if (_findReplaceService is null)
        {
            return;
        }

        if (payload is EditorReplaceQuery query)
        {
            _findReplaceService.ReplaceAll(query);
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
