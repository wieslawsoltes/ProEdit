using Avalonia;
using Avalonia.Metadata;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a paragraph block containing inline content.
/// </summary>
public sealed class Paragraph : Block
{
    /// <summary>
    /// Identifies the <see cref="KeepTogether"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> KeepTogetherProperty =
        AvaloniaProperty.Register<Paragraph, bool?>(nameof(KeepTogether));

    /// <summary>
    /// Identifies the <see cref="KeepWithNext"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> KeepWithNextProperty =
        AvaloniaProperty.Register<Paragraph, bool?>(nameof(KeepWithNext));

    /// <summary>
    /// Identifies the <see cref="MinOrphanLines"/> property.
    /// </summary>
    public static readonly StyledProperty<int?> MinOrphanLinesProperty =
        AvaloniaProperty.Register<Paragraph, int?>(nameof(MinOrphanLines));

    /// <summary>
    /// Identifies the <see cref="MinWidowLines"/> property.
    /// </summary>
    public static readonly StyledProperty<int?> MinWidowLinesProperty =
        AvaloniaProperty.Register<Paragraph, int?>(nameof(MinWidowLines));

    /// <summary>
    /// Identifies the <see cref="TextIndent"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> TextIndentProperty =
        AvaloniaProperty.Register<Paragraph, double?>(nameof(TextIndent));

    /// <summary>
    /// Identifies the <see cref="TextDecorations"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextDecorations?> TextDecorationsProperty =
        AvaloniaProperty.Register<Paragraph, FlowTextDecorations?>(nameof(TextDecorations));

    /// <summary>
    /// Gets the inline collection for the paragraph.
    /// </summary>
    [Content]
    public InlineCollection Inlines { get; }

    /// <summary>
    /// Gets or sets whether paragraph lines should be kept together.
    /// </summary>
    public bool? KeepTogether
    {
        get => GetValue(KeepTogetherProperty);
        set => SetValue(KeepTogetherProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this paragraph should stay with the next block.
    /// </summary>
    public bool? KeepWithNext
    {
        get => GetValue(KeepWithNextProperty);
        set => SetValue(KeepWithNextProperty, value);
    }

    /// <summary>
    /// Gets or sets minimum orphan line count.
    /// </summary>
    public int? MinOrphanLines
    {
        get => GetValue(MinOrphanLinesProperty);
        set => SetValue(MinOrphanLinesProperty, value);
    }

    /// <summary>
    /// Gets or sets minimum widow line count.
    /// </summary>
    public int? MinWidowLines
    {
        get => GetValue(MinWidowLinesProperty);
        set => SetValue(MinWidowLinesProperty, value);
    }

    /// <summary>
    /// Gets or sets first-line indent.
    /// </summary>
    public double? TextIndent
    {
        get => GetValue(TextIndentProperty);
        set => SetValue(TextIndentProperty, value);
    }

    /// <summary>
    /// Gets or sets text decoration metadata for paragraph content.
    /// </summary>
    public FlowTextDecorations? TextDecorations
    {
        get => GetValue(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Paragraph"/> class.
    /// </summary>
    public Paragraph()
    {
        Inlines = new InlineCollection(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Paragraph"/> class with a text run.
    /// </summary>
    /// <param name="text">The paragraph text.</param>
    public Paragraph(string text)
        : this()
    {
        Inlines.Add(new Run(text));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TextDecorationsProperty)
        {
            SyncStyleValue(TextDecorationsProperty, value => Style.TextDecorations = value);
        }

        base.OnPropertyChanged(change);
    }
}
