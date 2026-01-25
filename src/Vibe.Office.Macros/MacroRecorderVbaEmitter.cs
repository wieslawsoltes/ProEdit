using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Macros;

public static class MacroRecorderVbaEmitter
{
    public static string EmitVba(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);

        var name = SanitizeIdentifier(macro.Name);
        var builder = new StringBuilder();
        builder.AppendLine($"Sub {name}()");

        if (macro.Commands.Count == 0)
        {
            builder.AppendLine("    ' TODO: Add recorded steps.");
        }
        else
        {
            foreach (var command in macro.Commands)
            {
                builder.Append("    ' ").Append(command.CommandId);
                if (!string.IsNullOrWhiteSpace(command.Payload?.TypeId))
                {
                    builder.Append(" (").Append(command.Payload!.TypeId).Append(')');
                }

                builder.AppendLine();
                if (!string.IsNullOrWhiteSpace(command.Payload?.Json))
                {
                    builder.Append("    ' Payload: ").Append(command.Payload!.Json).AppendLine();
                }
            }
        }

        builder.AppendLine("End Sub");
        return builder.ToString();
    }

    private static string SanitizeIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "RecordedMacro";
        }

        var builder = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
            }
        }

        if (builder.Length == 0)
        {
            return "RecordedMacro";
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
