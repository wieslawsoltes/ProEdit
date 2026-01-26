using System;
using System.Collections.Generic;

namespace Vibe.Office.Documents;

public sealed class DocumentProperties
{
    public Dictionary<string, string> CoreProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CustomProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        CoreProperties.Clear();
        CustomProperties.Clear();
    }

    public bool TryGetValue(string name, out string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            value = string.Empty;
            return false;
        }

        if (CoreProperties.TryGetValue(name, out var coreValue) && !string.IsNullOrEmpty(coreValue))
        {
            value = coreValue;
            return true;
        }

        if (CustomProperties.TryGetValue(name, out var customValue) && !string.IsNullOrEmpty(customValue))
        {
            value = customValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public void SetCoreProperty(string name, string? value)
    {
        SetProperty(CoreProperties, name, value);
    }

    public void SetCustomProperty(string name, string? value)
    {
        SetProperty(CustomProperties, name, value);
    }

    private static void SetProperty(Dictionary<string, string> target, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.IsNullOrEmpty(value))
        {
            target.Remove(name);
            return;
        }

        target[name] = value;
    }
}
