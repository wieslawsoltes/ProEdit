namespace ProEdit.WinUICompat.Documents;

public class TextRange
{
    public TextRange(TextPointer start, TextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        if (start.CompareTo(end) <= 0)
        {
            Start = start;
            End = end;
        }
        else
        {
            Start = end;
            End = start;
        }
    }

    public TextPointer Start { get; protected set; }

    public TextPointer End { get; protected set; }

    public bool IsEmpty => Start.CompareTo(End) == 0;

    public virtual void Select(TextPointer start, TextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        if (start.CompareTo(end) <= 0)
        {
            Start = start;
            End = end;
        }
        else
        {
            Start = end;
            End = start;
        }
    }
}
