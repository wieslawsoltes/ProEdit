using System;
using ProEdit.Documents;

namespace ProEdit.Editing;

public enum EditorStyleType
{
    Paragraph,
    Character,
    Table
}

public readonly record struct EditorStyleInfo(
    string Id,
    string Name,
    EditorStyleType Type,
    bool IsDefault,
    bool IsInUse,
    bool IsQuickStyle,
    bool IsHidden,
    bool IsSemiHidden,
    bool UnhideWhenUsed,
    bool IsLocked,
    bool IsCustom,
    int? UiPriority);

public readonly record struct EditorStyleInspectorSnapshot(
    EditorValue<string> ParagraphStyleId,
    EditorValue<string> CharacterStyleId,
    EditorValue<string> TableStyleId,
    string ParagraphDirectFormatting,
    string CharacterDirectFormatting);

public readonly record struct EditorStyleCreateOptions(
    EditorStyleType Type,
    string Name,
    string? BasedOnId,
    string? NextStyleId,
    string? LinkedStyleId,
    bool QuickStyle,
    bool AutoRedefine,
    TextStyleProperties? RunProperties,
    ParagraphStyleProperties? ParagraphProperties,
    TableProperties? TableProperties,
    TableCellProperties? TableCellProperties,
    string? StyleId);

public interface IStyleManagerService : IStyleService
{
    event EventHandler? StylesChanged;
    IReadOnlyList<EditorStyleInfo> GetStyles(EditorStyleType? type = null);
    EditorValue<string> GetCurrentCharacterStyleId();
    EditorValue<string> GetCurrentTableStyleId();
    ParagraphStyleDefinition? GetParagraphStyleDefinition(string styleId);
    CharacterStyleDefinition? GetCharacterStyleDefinition(string styleId);
    TableStyleDefinition? GetTableStyleDefinition(string styleId);
    TextStyle? GetStylePreview(EditorStyleType type, string styleId);
    bool ApplyStyle(EditorStyleType type, string styleId);
    bool RenameStyle(EditorStyleType type, string styleId, string name);
    bool SetStyleBasedOn(EditorStyleType type, string styleId, string? basedOnId);
    bool SetStyleNext(EditorStyleType type, string styleId, string? nextStyleId);
    bool SetDefaultStyle(EditorStyleType type, string styleId);
    bool SetStyleQuickStyle(EditorStyleType type, string styleId, bool? quickStyle);
    bool SetStyleHidden(EditorStyleType type, string styleId, bool? hidden);
    bool SetStyleSemiHidden(EditorStyleType type, string styleId, bool? semiHidden);
    bool SetStyleUnhideWhenUsed(EditorStyleType type, string styleId, bool? unhideWhenUsed);
    bool SetStyleAutoRedefine(EditorStyleType type, string styleId, bool? autoRedefine);
    bool SetStyleLocked(EditorStyleType type, string styleId, bool? locked);
    bool SetStyleLinkedStyle(EditorStyleType type, string styleId, string? linkedStyleId);
    bool SetStylePrimaryStyle(EditorStyleType type, string styleId, bool? primaryStyle);
    bool SetStyleCustomStyle(EditorStyleType type, string styleId, bool? customStyle);
    bool SetStylePriority(EditorStyleType type, string styleId, int? priority);
    bool UpdateParagraphStyleProperties(string styleId, TextStyleProperties? runProperties, ParagraphStyleProperties? paragraphProperties);
    bool UpdateCharacterStyleProperties(string styleId, TextStyleProperties? runProperties);
    bool UpdateTableStyleProperties(string styleId, TableProperties? tableProperties, TableCellProperties? cellProperties);
    bool UpdateTableStyleConditions(string styleId, IReadOnlyDictionary<TableStyleCondition, TableStyleConditionProperties>? conditions);
    bool CreateStyle(EditorStyleCreateOptions options);
    EditorStyleInspectorSnapshot GetStyleInspectorSnapshot();
    bool ClearDirectFormatting();
}
