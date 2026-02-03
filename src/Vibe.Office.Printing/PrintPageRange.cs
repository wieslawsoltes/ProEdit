namespace Vibe.Office.Printing;

public readonly record struct PrintPageRange
{
    public PrintPageRange(int start, int end)
    {
        if (start < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start must be >= 1.");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "End must be >= Start.");
        }

        Start = start;
        End = end;
    }

    public int Start { get; }
    public int End { get; }

    public bool Contains(int pageNumber) => pageNumber >= Start && pageNumber <= End;
}
