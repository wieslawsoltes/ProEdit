using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public partial class StyleInspectorDialog : Window
{
    private readonly IStyleManagerService _styleService;
    private TextBlock? _paragraphStyleName;
    private TextBlock? _paragraphStyleChain;
    private TextBlock? _paragraphDirectFormatting;
    private TextBlock? _characterStyleName;
    private TextBlock? _characterStyleChain;
    private TextBlock? _characterDirectFormatting;
    private TextBlock? _tableStyleName;
    private TextBlock? _tableStyleChain;

    public StyleInspectorDialog(IStyleManagerService styleService)
    {
        _styleService = styleService ?? throw new ArgumentNullException(nameof(styleService));
        InitializeComponent();
        InitializeControls();
        Activated += (_, _) => RefreshSnapshot();
        RefreshSnapshot();
    }

    private void InitializeControls()
    {
        _paragraphStyleName = this.FindControl<TextBlock>("ParagraphStyleName");
        _paragraphStyleChain = this.FindControl<TextBlock>("ParagraphStyleChain");
        _paragraphDirectFormatting = this.FindControl<TextBlock>("ParagraphDirectFormatting");
        _characterStyleName = this.FindControl<TextBlock>("CharacterStyleName");
        _characterStyleChain = this.FindControl<TextBlock>("CharacterStyleChain");
        _characterDirectFormatting = this.FindControl<TextBlock>("CharacterDirectFormatting");
        _tableStyleName = this.FindControl<TextBlock>("TableStyleName");
        _tableStyleChain = this.FindControl<TextBlock>("TableStyleChain");

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }

        if (this.FindControl<Button>("ClearFormattingButton") is { } clearButton)
        {
            clearButton.Click += OnClearFormattingClick;
        }
    }

    private void RefreshSnapshot()
    {
        var snapshot = _styleService.GetStyleInspectorSnapshot();
        SetStyleDetails(
            snapshot.ParagraphStyleId,
            EditorStyleType.Paragraph,
            _paragraphStyleName,
            _paragraphStyleChain,
            snapshot.ParagraphDirectFormatting,
            _paragraphDirectFormatting);

        SetStyleDetails(
            snapshot.CharacterStyleId,
            EditorStyleType.Character,
            _characterStyleName,
            _characterStyleChain,
            snapshot.CharacterDirectFormatting,
            _characterDirectFormatting);

        SetStyleDetails(
            snapshot.TableStyleId,
            EditorStyleType.Table,
            _tableStyleName,
            _tableStyleChain,
            null,
            null);
    }

    private void SetStyleDetails(
        EditorValue<string> styleValue,
        EditorStyleType type,
        TextBlock? nameTarget,
        TextBlock? chainTarget,
        string? directFormatting,
        TextBlock? directTarget)
    {
        if (nameTarget is null || chainTarget is null)
        {
            return;
        }

        if (styleValue.IsMixed)
        {
            nameTarget.Text = "Mixed";
            chainTarget.Text = "Style chain: Mixed";
        }
        else if (!styleValue.HasValue || string.IsNullOrWhiteSpace(styleValue.Value))
        {
            nameTarget.Text = "None";
            chainTarget.Text = "Style chain: None";
        }
        else
        {
            var name = ResolveStyleName(type, styleValue.Value);
            nameTarget.Text = name;
            chainTarget.Text = BuildStyleChain(type, styleValue.Value);
        }

        if (directTarget is not null)
        {
            directTarget.Text = directFormatting ?? string.Empty;
        }
    }

    private string BuildStyleChain(EditorStyleType type, string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return "Style chain: None";
        }

        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = styleId;
        while (!string.IsNullOrWhiteSpace(currentId) && visited.Add(currentId))
        {
            chain.Add(ResolveStyleName(type, currentId));
            currentId = GetBasedOnId(type, currentId);
        }

        if (chain.Count == 0)
        {
            return "Style chain: None";
        }

        return $"Style chain: {string.Join(" > ", chain)}";
    }

    private string? GetBasedOnId(EditorStyleType type, string styleId)
    {
        return type switch
        {
            EditorStyleType.Paragraph => _styleService.GetParagraphStyleDefinition(styleId)?.BasedOnId,
            EditorStyleType.Character => _styleService.GetCharacterStyleDefinition(styleId)?.BasedOnId,
            EditorStyleType.Table => _styleService.GetTableStyleDefinition(styleId)?.BasedOnId,
            _ => null
        };
    }

    private string ResolveStyleName(EditorStyleType type, string styleId)
    {
        return type switch
        {
            EditorStyleType.Paragraph => _styleService.GetParagraphStyleDefinition(styleId)?.Name ?? styleId,
            EditorStyleType.Character => _styleService.GetCharacterStyleDefinition(styleId)?.Name ?? styleId,
            EditorStyleType.Table => _styleService.GetTableStyleDefinition(styleId)?.Name ?? styleId,
            _ => styleId
        };
    }

    private void OnClearFormattingClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService.ClearDirectFormatting())
        {
            RefreshSnapshot();
        }
    }
}
