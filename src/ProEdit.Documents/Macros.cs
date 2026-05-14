namespace ProEdit.Documents;

public sealed class DocumentMacros
{
    public bool IsTrusted { get; set; }
    public byte[]? VbaProject { get; set; }
    public List<MacroDefinition> Items { get; } = new();
    public List<VbaModuleInfo> VbaModules { get; } = new();
    public List<VbaProjectReference> References { get; } = new();
}

public sealed class MacroDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MacroLanguage Language { get; set; } = MacroLanguage.CommandSequence;
    public bool IsTrusted { get; set; }
    public string? Source { get; set; }
    public List<MacroCommand> Commands { get; } = new();
}

public sealed class MacroCommand
{
    public string CommandId { get; set; } = string.Empty;
    public MacroPayload? Payload { get; set; }
}

public sealed class MacroPayload
{
    public string? TypeId { get; set; }
    public string? Json { get; set; }
}

public enum MacroLanguage
{
    CommandSequence,
    Vba
}

public sealed class VbaModuleInfo
{
    public string Name { get; set; } = string.Empty;
    public string? StreamName { get; set; }
    public string? Source { get; set; }
    public List<string> Procedures { get; } = new();
}

public sealed class VbaProjectReference
{
    public string Name { get; set; } = string.Empty;
    public string? Identifier { get; set; }
    public string? Description { get; set; }
}
