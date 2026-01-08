namespace Vibe.Office.Documents;

public abstract class FieldInline : Inline
{
}

public sealed class FieldStartInline : FieldInline
{
    public string Instruction { get; set; }
    public FieldDefinition? Definition { get; set; }

    public FieldStartInline(string instruction)
    {
        Instruction = instruction ?? string.Empty;
    }
}

public sealed class FieldSeparatorInline : FieldInline
{
}

public sealed class FieldEndInline : FieldInline
{
}
