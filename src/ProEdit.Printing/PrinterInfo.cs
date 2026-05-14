namespace ProEdit.Printing;

public sealed record PrinterInfo(string Name, bool IsDefault)
{
    public string DisplayName => IsDefault ? $"{Name} (Default)" : Name;
}
