using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vibe.Office.Documents;

namespace Vibe.Office.OpenXml;

internal static class DocxMacroSerializer
{
    public const string MacroCustomPartContentType = "application/vnd.vibeoffice.macros+json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        },
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(DocumentMacros macros)
    {
        var store = new MacroStore
        {
            Version = 1,
            IsTrusted = macros.IsTrusted
        };

        foreach (var macro in macros.Items)
        {
            store.Macros.Add(ToEntry(macro));
        }

        foreach (var module in macros.VbaModules)
        {
            store.VbaModules.Add(new VbaModuleEntry
            {
                Name = module.Name,
                StreamName = module.StreamName,
                Source = module.Source,
                Procedures = module.Procedures.Count == 0 ? new List<string>() : new List<string>(module.Procedures)
            });
        }

        foreach (var reference in macros.References)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                continue;
            }

            store.References.Add(new VbaReferenceEntry
            {
                Name = reference.Name,
                Identifier = reference.Identifier,
                Description = reference.Description
            });
        }

        return JsonSerializer.Serialize(store, Options);
    }

    public static bool TryDeserialize(string json, DocumentMacros macros)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        MacroStore? store;
        try
        {
            store = JsonSerializer.Deserialize<MacroStore>(json, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (store is null)
        {
            return false;
        }

        macros.IsTrusted = store.IsTrusted;
        macros.Items.Clear();
        macros.VbaModules.Clear();
        macros.References.Clear();

        if (store.Macros is not null)
        {
            foreach (var entry in store.Macros)
            {
                var macro = ToDefinition(entry);
                if (macro is not null)
                {
                    macros.Items.Add(macro);
                }
            }
        }

        if (store.VbaModules is not null)
        {
            foreach (var module in store.VbaModules)
            {
                if (string.IsNullOrWhiteSpace(module.Name))
                {
                    continue;
                }

                macros.VbaModules.Add(new VbaModuleInfo
                {
                    Name = module.Name,
                    StreamName = module.StreamName,
                    Source = module.Source
                });
            }
        }

        if (store.References is not null)
        {
            foreach (var reference in store.References)
            {
                if (string.IsNullOrWhiteSpace(reference.Name))
                {
                    continue;
                }

                macros.References.Add(new VbaProjectReference
                {
                    Name = reference.Name,
                    Identifier = reference.Identifier,
                    Description = reference.Description
                });
            }
        }

        if (store.VbaModules is not null)
        {
            foreach (var module in store.VbaModules)
            {
                if (string.IsNullOrWhiteSpace(module.Name))
                {
                    continue;
                }

                var target = macros.VbaModules.FirstOrDefault(item =>
                    string.Equals(item.Name, module.Name, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    continue;
                }

                if (module.Procedures is not null)
                {
                    target.Procedures.Clear();
                    foreach (var procedure in module.Procedures)
                    {
                        if (!string.IsNullOrWhiteSpace(procedure))
                        {
                            target.Procedures.Add(procedure);
                        }
                    }
                }
            }
        }

        return true;
    }

    private static MacroEntry ToEntry(MacroDefinition macro)
    {
        var entry = new MacroEntry
        {
            Id = macro.Id,
            Name = macro.Name,
            Description = macro.Description,
            Language = macro.Language,
            IsTrusted = macro.IsTrusted,
            Source = macro.Source
        };

        foreach (var command in macro.Commands)
        {
            entry.Commands.Add(new MacroCommandEntry
            {
                CommandId = command.CommandId,
                Payload = command.Payload is null
                    ? null
                    : new MacroPayloadEntry
                    {
                        TypeId = command.Payload.TypeId,
                        Json = command.Payload.Json
                    }
            });
        }

        return entry;
    }

    private static MacroDefinition? ToDefinition(MacroEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            return null;
        }

        var definition = new MacroDefinition
        {
            Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
            Name = entry.Name,
            Description = entry.Description,
            Language = entry.Language,
            IsTrusted = entry.IsTrusted,
            Source = entry.Source
        };

        if (entry.Commands is not null)
        {
            foreach (var command in entry.Commands)
            {
                if (string.IsNullOrWhiteSpace(command.CommandId))
                {
                    continue;
                }

                definition.Commands.Add(new MacroCommand
                {
                    CommandId = command.CommandId,
                    Payload = command.Payload is null
                        ? null
                        : new MacroPayload
                        {
                            TypeId = command.Payload.TypeId,
                            Json = command.Payload.Json
                        }
                });
            }
        }

        return definition;
    }

    private sealed class MacroStore
    {
        public int Version { get; set; }
        public bool IsTrusted { get; set; }
        public List<MacroEntry> Macros { get; set; } = new();
        public List<VbaModuleEntry> VbaModules { get; set; } = new();
        public List<VbaReferenceEntry> References { get; set; } = new();
    }

    private sealed class MacroEntry
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public MacroLanguage Language { get; set; }
        public bool IsTrusted { get; set; }
        public string? Source { get; set; }
        public List<MacroCommandEntry> Commands { get; set; } = new();
    }

    private sealed class MacroCommandEntry
    {
        public string CommandId { get; set; } = string.Empty;
        public MacroPayloadEntry? Payload { get; set; }
    }

    private sealed class MacroPayloadEntry
    {
        public string? TypeId { get; set; }
        public string? Json { get; set; }
    }

    private sealed class VbaModuleEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? StreamName { get; set; }
        public string? Source { get; set; }
        public List<string> Procedures { get; set; } = new();
    }

    private sealed class VbaReferenceEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Identifier { get; set; }
        public string? Description { get; set; }
    }
}
