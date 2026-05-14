using System.Text;

namespace ProEdit.Documents;

public sealed record ShapePresetDescriptor(string Id, string DisplayName);

public static class ShapePresetCatalog
{
    private static readonly Lazy<IReadOnlyList<ShapePresetDescriptor>> Presets = new(CreatePresets);

    public static IReadOnlyList<ShapePresetDescriptor> GetPresets()
    {
        return Presets.Value;
    }

    private static IReadOnlyList<ShapePresetDescriptor> CreatePresets()
    {
        var names = ShapePresetGeometryLibrary.GetPresetNames();
        var result = new List<ShapePresetDescriptor>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var id = names[i];
            result.Add(new ShapePresetDescriptor(id, CreateDisplayName(id)));
        }

        return result;
    }

    private static string CreateDisplayName(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Shape";
        }

        var span = id.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return "Shape";
        }

        var builder = new StringBuilder(span.Length + 8);
        for (var i = 0; i < span.Length; i++)
        {
            var current = span[i];
            if (i > 0)
            {
                var previous = span[i - 1];
                var next = i + 1 < span.Length ? span[i + 1] : '\0';
                var lowerToUpper = char.IsLower(previous) && char.IsUpper(current);
                var acronymBoundary = char.IsUpper(previous) && char.IsUpper(current) && i + 1 < span.Length && char.IsLower(next);
                var digitBoundary = char.IsDigit(previous) != char.IsDigit(current);
                if (lowerToUpper || acronymBoundary || digitBoundary)
                {
                    builder.Append(' ');
                }
            }

            if (builder.Length == 0)
            {
                builder.Append(char.ToUpperInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
