namespace Vibe.Office.Vba;

public static class VbaCompiler
{
    public static VbaCompilation Compile(string source)
    {
        var module = VbaParser.ParseModule(source);
        return new VbaCompilation(module);
    }
}

public sealed class VbaCompilation
{
    public VbaModuleSyntax Module { get; }

    public VbaCompilation(VbaModuleSyntax module)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
    }
}
