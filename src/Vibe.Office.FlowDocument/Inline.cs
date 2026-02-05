using Avalonia;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Base type for FlowDocument inline elements.
/// </summary>
public abstract class Inline : TextElement
{
    /// <summary>
    /// Identifies the <see cref="BaselineAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowBaselineAlignment?> BaselineAlignmentProperty =
        AvaloniaProperty.Register<Inline, FlowBaselineAlignment?>(nameof(BaselineAlignment));

    /// <summary>
    /// Identifies the <see cref="TextDecorations"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextDecorations?> TextDecorationsProperty =
        AvaloniaProperty.Register<Inline, FlowTextDecorations?>(nameof(TextDecorations));

    /// <summary>
    /// Identifies the <see cref="FlowDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FlowDirectionProperty =
        AvaloniaProperty.Register<Inline, string?>(nameof(FlowDirection));

    /// <summary>
    /// Gets or sets baseline alignment metadata.
    /// </summary>
    public FlowBaselineAlignment? BaselineAlignment
    {
        get => GetValue(BaselineAlignmentProperty);
        set => SetValue(BaselineAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets text decoration metadata.
    /// </summary>
    public FlowTextDecorations? TextDecorations
    {
        get => GetValue(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>
    /// Gets or sets the inline flow direction.
    /// </summary>
    public string? FlowDirection
    {
        get => GetValue(FlowDirectionProperty);
        set => SetValue(FlowDirectionProperty, value);
    }

    /// <summary>
    /// Gets sibling inlines for this inline.
    /// </summary>
    public InlineCollection? SiblingInlines
    {
        get
        {
            if (Parent is Paragraph paragraph)
            {
                return paragraph.Inlines;
            }

            if (Parent is Span span)
            {
                return span.Inlines;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the next sibling inline.
    /// </summary>
    public Inline? NextInline
    {
        get
        {
            var siblings = SiblingInlines;
            if (siblings is null)
            {
                return null;
            }

            var index = siblings.IndexOf(this);
            if (index < 0 || index + 1 >= siblings.Count)
            {
                return null;
            }

            return siblings[index + 1];
        }
    }

    /// <summary>
    /// Gets the previous sibling inline.
    /// </summary>
    public Inline? PreviousInline
    {
        get
        {
            var siblings = SiblingInlines;
            if (siblings is null)
            {
                return null;
            }

            var index = siblings.IndexOf(this);
            if (index <= 0)
            {
                return null;
            }

            return siblings[index - 1];
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TextDecorationsProperty)
        {
            SyncStyleValue(TextDecorationsProperty, value => Style.TextDecorations = value);
        }
        else if (change.Property == BaselineAlignmentProperty)
        {
            SyncStyleValue(BaselineAlignmentProperty, value => Style.BaselineAlignment = value);
        }

        base.OnPropertyChanged(change);
    }
}
