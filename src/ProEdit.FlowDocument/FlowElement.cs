using Avalonia;

namespace ProEdit.FlowDocument;

/// <summary>
/// Base type for all FlowDocument elements.
/// </summary>
public abstract class FlowElement : AvaloniaObject
{
    private FlowElement? _parent;

    /// <summary>
    /// Identifies the <see cref="Parent"/> property.
    /// </summary>
    public static readonly DirectProperty<FlowElement, FlowElement?> ParentProperty =
        AvaloniaProperty.RegisterDirect<FlowElement, FlowElement?>(nameof(Parent), element => element.Parent);

    /// <summary>
    /// Gets the parent element in the FlowDocument tree.
    /// </summary>
    internal FlowElement? Parent
    {
        get => _parent;
        set => SetAndRaise(ParentProperty, ref _parent, value);
    }

    internal void NotifyChanged()
    {
        FlowElement? current = this;
        while (current is not null)
        {
            if (current is FlowDocument document)
            {
                document.RaiseChanged();
                return;
            }

            current = current.Parent;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != ParentProperty)
        {
            NotifyChanged();
        }
    }
}
