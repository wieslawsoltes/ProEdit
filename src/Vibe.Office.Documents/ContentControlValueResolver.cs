using System.Collections;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Vibe.Office.Documents;

public static class ContentControlValueResolver
{
    public static string ResolvePlaceholderText(ContentControlProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (!string.IsNullOrWhiteSpace(properties.PlaceholderText))
        {
            return properties.PlaceholderText;
        }

        if (!string.IsNullOrWhiteSpace(properties.Placeholder))
        {
            return properties.Placeholder;
        }

        if (!string.IsNullOrWhiteSpace(properties.Alias))
        {
            return properties.Alias;
        }

        if (!string.IsNullOrWhiteSpace(properties.Tag))
        {
            return properties.Tag;
        }

        return "Content Control";
    }

    public static string? ResolveContentControlValue(ContentControlProperties properties, Document document)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(document);

        if (TryResolveContentControlBinding(properties.DataBinding, document, out var bindingValue))
        {
            if (TryResolveStructuredContentControlValueFromBinding(properties, bindingValue, out var structuredValue))
            {
                return structuredValue;
            }

            return bindingValue;
        }

        if (TryResolveStructuredContentControlValue(properties, out var structured))
        {
            return structured;
        }

        return null;
    }

    public static bool TryResolveContentControlBinding(ContentControlDataBinding? binding, Document document, out string value)
    {
        ArgumentNullException.ThrowIfNull(document);

        value = string.Empty;
        if (binding is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.StoreItemId)
            || string.IsNullOrWhiteSpace(binding.XPath))
        {
            return false;
        }

        var key = NormalizeStoreItemId(binding.StoreItemId);
        if (!document.CustomXmlParts.TryGetValue(key, out var xml))
        {
            return false;
        }

        try
        {
            var manager = new XmlNamespaceManager(new NameTable());
            ApplyPrefixMappings(manager, binding.PrefixMappings);
            var result = xml.XPathEvaluate(binding.XPath, manager);
            switch (result)
            {
                case string stringResult:
                    value = stringResult;
                    return !string.IsNullOrWhiteSpace(value);
                case bool boolResult:
                    value = boolResult ? "true" : "false";
                    return true;
                case double doubleResult:
                    value = doubleResult.ToString(CultureInfo.CurrentCulture);
                    return true;
                case IEnumerable<object> enumerableResult:
                    foreach (var item in enumerableResult)
                    {
                        if (TryResolveXPathValue(item, out value))
                        {
                            return true;
                        }
                    }

                    break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                    {
                        if (TryResolveXPathValue(item, out value))
                        {
                            return true;
                        }
                    }

                    break;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static bool TryUpdateContentControlBinding(ContentControlDataBinding? binding, Document document, string value)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (binding is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.StoreItemId)
            || string.IsNullOrWhiteSpace(binding.XPath))
        {
            return false;
        }

        var key = NormalizeStoreItemId(binding.StoreItemId);
        if (!document.CustomXmlParts.TryGetValue(key, out var xml))
        {
            return false;
        }

        try
        {
            var manager = new XmlNamespaceManager(new NameTable());
            ApplyPrefixMappings(manager, binding.PrefixMappings);
            var result = xml.XPathEvaluate(binding.XPath, manager);
            switch (result)
            {
                case IEnumerable<object> enumerableResult:
                {
                    var updated = false;
                    foreach (var item in enumerableResult)
                    {
                        updated |= TrySetXPathValue(item, value);
                    }

                    return updated;
                }
                case IEnumerable enumerable:
                {
                    var updated = false;
                    foreach (var item in enumerable)
                    {
                        updated |= TrySetXPathValue(item, value);
                    }

                    return updated;
                }
                default:
                    return TrySetXPathValue(result, value);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveStructuredContentControlValueFromBinding(
        ContentControlProperties properties,
        string bindingValue,
        out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(bindingValue))
        {
            return false;
        }

        switch (properties.DataType)
        {
            case ContentControlDataType.CheckBox:
                if (TryParseBoolean(bindingValue.AsSpan(), out var isChecked))
                {
                    value = isChecked ? "[x]" : "[ ]";
                    return true;
                }

                break;
            case ContentControlDataType.Date:
            {
                if (!DateTimeOffset.TryParse(bindingValue, out var parsed))
                {
                    break;
                }

                var culture = CultureInfo.CurrentCulture;
                if (!string.IsNullOrWhiteSpace(properties.DateFormat))
                {
                    try
                    {
                        value = parsed.ToString(properties.DateFormat, culture);
                        return true;
                    }
                    catch
                    {
                    }
                }

                value = parsed.ToString("d", culture);
                return true;
            }
            case ContentControlDataType.DropDownList:
            case ContentControlDataType.ComboBox:
            {
                foreach (var item in properties.Items)
                {
                    if (string.Equals(item.Value, bindingValue, StringComparison.OrdinalIgnoreCase))
                    {
                        value = item.DisplayText ?? item.Value ?? bindingValue;
                        return true;
                    }
                }

                value = bindingValue;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveStructuredContentControlValue(ContentControlProperties properties, out string value)
    {
        value = string.Empty;
        switch (properties.DataType)
        {
            case ContentControlDataType.CheckBox:
                if (properties.IsChecked.HasValue)
                {
                    value = properties.IsChecked.Value ? "[x]" : "[ ]";
                    return true;
                }

                break;
            case ContentControlDataType.Date:
            {
                if (string.IsNullOrWhiteSpace(properties.FullDate))
                {
                    break;
                }

                if (!DateTimeOffset.TryParse(properties.FullDate, out var parsed))
                {
                    break;
                }

                var culture = CultureInfo.CurrentCulture;
                if (!string.IsNullOrWhiteSpace(properties.DateFormat))
                {
                    try
                    {
                        value = parsed.ToString(properties.DateFormat, culture);
                        return true;
                    }
                    catch
                    {
                    }
                }

                value = parsed.ToString("d", culture);
                return true;
            }
            case ContentControlDataType.DropDownList:
            case ContentControlDataType.ComboBox:
            {
                if (!string.IsNullOrWhiteSpace(properties.SelectedValue))
                {
                    foreach (var item in properties.Items)
                    {
                        if (string.Equals(item.Value, properties.SelectedValue, StringComparison.OrdinalIgnoreCase))
                        {
                            value = item.DisplayText ?? item.Value ?? properties.SelectedValue;
                            return true;
                        }
                    }

                    value = properties.SelectedValue;
                    return true;
                }

                foreach (var item in properties.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.DisplayText) && string.IsNullOrWhiteSpace(item.Value))
                    {
                        continue;
                    }

                    value = item.DisplayText ?? item.Value ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(value);
                }

                break;
            }
        }

        return false;
    }

    private static bool TryParseBoolean(ReadOnlySpan<char> value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            result = numeric != 0;
            return true;
        }

        if (value.Equals("yes".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || value.Equals("on".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("no".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || value.Equals("off".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryResolveXPathValue(object? item, out string value)
    {
        value = string.Empty;
        switch (item)
        {
            case null:
                return false;
            case string text:
                value = text;
                return !string.IsNullOrWhiteSpace(value);
            case XElement element:
                value = element.Value;
                return !string.IsNullOrWhiteSpace(value);
            case XAttribute attribute:
                value = attribute.Value;
                return !string.IsNullOrWhiteSpace(value);
            case XPathNavigator navigator:
                value = navigator.Value;
                return !string.IsNullOrWhiteSpace(value);
            default:
                value = item.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
        }
    }

    private static bool TrySetXPathValue(object? item, string value)
    {
        switch (item)
        {
            case null:
                return false;
            case XElement element:
                element.Value = value;
                return true;
            case XAttribute attribute:
                attribute.Value = value;
                return true;
            case XText text:
                text.Value = value;
                return true;
            case XPathNavigator navigator when navigator.CanEdit:
                navigator.SetValue(value);
                return true;
            default:
                return false;
        }
    }

    private static void ApplyPrefixMappings(XmlNamespaceManager manager, string? mappings)
    {
        if (string.IsNullOrWhiteSpace(mappings))
        {
            return;
        }

        var segments = mappings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!trimmed.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = trimmed.Split(new[] { '=' }, 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var prefixPart = parts[0];
            var prefixIndex = prefixPart.IndexOf(':');
            if (prefixIndex < 0 || prefixIndex + 1 >= prefixPart.Length)
            {
                continue;
            }

            var prefix = prefixPart.Substring(prefixIndex + 1);
            var uri = parts[1].Trim().Trim('"', '\'');
            if (prefix.Length == 0 || uri.Length == 0)
            {
                continue;
            }

            manager.AddNamespace(prefix, uri);
        }
    }

    private static string NormalizeStoreItemId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '{' && trimmed[^1] == '}')
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }
}
