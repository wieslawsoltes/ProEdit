using ProEdit.Documents;
using System.IO;
using ProEdit.Vba;

namespace ProEdit.Macros;

public static class VbaMacroUtilities
{
    public static IReadOnlyList<string> ExtractProcedures(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Array.Empty<string>();
        }

        try
        {
            var module = VbaParser.ParseModule(source);
            var names = new List<string>();
            foreach (var member in module.Members)
            {
                switch (member)
                {
                    case VbaSubroutineSyntax sub:
                        AddUnique(names, sub.Name);
                        break;
                    case VbaFunctionSyntax function:
                        AddUnique(names, function.Name);
                        break;
                }
            }

            return names;
        }
        catch (VbaParseException)
        {
            return ExtractProceduresFallback(source);
        }
    }

    private static IReadOnlyList<string> ExtractProceduresFallback(string source)
    {
        var names = new List<string>();
        using var reader = new StringReader(source);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("'", StringComparison.Ordinal))
            {
                continue;
            }

            var words = trimmed.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
            {
                continue;
            }

            var index = 0;
            while (index < words.Length)
            {
                var token = words[index];
                if (IsAccessModifier(token))
                {
                    index++;
                    continue;
                }

                if (IsProcedureKeyword(token))
                {
                    if (index + 1 < words.Length)
                    {
                        var name = words[index + 1];
                        var parenIndex = name.IndexOf('(');
                        if (parenIndex > 0)
                        {
                            name = name[..parenIndex];
                        }

                        AddUnique(names, name);
                    }

                    break;
                }

                if (token.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                break;
            }
        }

        return names;
    }

    private static bool IsAccessModifier(string token)
    {
        return token.Equals("Public", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Private", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Friend", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Static", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProcedureKeyword(string token)
    {
        return token.Equals("Sub", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Function", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Property", StringComparison.OrdinalIgnoreCase);
    }

    public static void UpdateModuleProcedures(VbaModuleInfo module, string? source)
    {
        ArgumentNullException.ThrowIfNull(module);
        module.Source = source;
        module.Procedures.Clear();
        foreach (var name in ExtractProcedures(source))
        {
            module.Procedures.Add(name);
        }
    }

    public static void SyncVbaDefinitions(DocumentMacros macros)
    {
        ArgumentNullException.ThrowIfNull(macros);

        var map = new Dictionary<string, VbaModuleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in macros.VbaModules)
        {
            if (!string.IsNullOrWhiteSpace(module.Source))
            {
                UpdateModuleProcedures(module, module.Source);
            }

            foreach (var procedure in module.Procedures)
            {
                if (string.IsNullOrWhiteSpace(procedure))
                {
                    continue;
                }

                if (!map.ContainsKey(procedure))
                {
                    map[procedure] = module;
                }
            }
        }

        for (var i = macros.Items.Count - 1; i >= 0; i--)
        {
            var macro = macros.Items[i];
            if (macro.Language == MacroLanguage.Vba && !map.ContainsKey(macro.Name))
            {
                macros.Items.RemoveAt(i);
            }
        }

        foreach (var (procedure, module) in map)
        {
            MacroDefinition? target = null;
            foreach (var macro in macros.Items)
            {
                if (macro.Language == MacroLanguage.Vba
                    && string.Equals(macro.Name, procedure, StringComparison.OrdinalIgnoreCase))
                {
                    target = macro;
                    break;
                }
            }

            if (target is null)
            {
                target = new MacroDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = procedure,
                    Language = MacroLanguage.Vba,
                    Description = module.Name,
                    IsTrusted = false
                };
                macros.Items.Add(target);
            }

            target.Source = module.Source;
            target.Description = module.Name;
        }
    }

    private static void AddUnique(List<string> list, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        foreach (var existing in list)
        {
            if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        list.Add(name);
    }
}
