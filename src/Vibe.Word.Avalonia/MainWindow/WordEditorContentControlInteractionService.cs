using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

internal sealed class WordEditorContentControlInteractionService : IContentControlInteractionService
{
    private readonly IEditorDialogService _dialogService;

    public WordEditorContentControlInteractionService(IEditorDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    public bool TryPickListItem(
        ContentControlProperties properties,
        IReadOnlyList<ContentControlListItem> items,
        string? currentValue,
        bool allowCustom,
        out string? selectedValue)
    {
        selectedValue = null;
        var prompt = BuildListPrompt(items, allowCustom);
        var input = _dialogService.PromptAsync("Content Control", prompt, currentValue).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (allowCustom)
        {
            selectedValue = input;
            return true;
        }

        if (TryResolveListSelection(items, input, out selectedValue))
        {
            return true;
        }

        return false;
    }

    public bool TryPickDate(ContentControlProperties properties, DateTimeOffset? currentValue, out DateTimeOffset selectedDate)
    {
        selectedDate = default;
        var initialValue = ResolveInitialDateText(properties, currentValue);
        var prompt = string.IsNullOrWhiteSpace(properties.DateFormat)
            ? "Enter a date value:"
            : $"Enter a date value ({properties.DateFormat}):";
        var input = _dialogService.PromptAsync("Content Control", prompt, initialValue).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return DateTimeOffset.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, out selectedDate);
    }

    private static string BuildListPrompt(IReadOnlyList<ContentControlListItem> items, bool allowCustom)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Select a value:");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var display = item.DisplayText ?? item.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(display) && string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            builder.Append(i + 1).Append(". ").Append(display);
            if (!string.IsNullOrWhiteSpace(item.Value)
                && !string.Equals(item.Value, display, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" (").Append(item.Value).Append(')');
            }

            builder.AppendLine();
        }

        if (allowCustom)
        {
            builder.AppendLine("Enter a custom value.");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool TryResolveListSelection(
        IReadOnlyList<ContentControlListItem> items,
        string input,
        out string? selectedValue)
    {
        selectedValue = null;
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index > 0 && index <= items.Count)
        {
            var item = items[index - 1];
            selectedValue = item.Value ?? item.DisplayText;
            return !string.IsNullOrWhiteSpace(selectedValue);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.Equals(item.Value, input, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.DisplayText, input, StringComparison.OrdinalIgnoreCase))
            {
                selectedValue = item.Value ?? item.DisplayText;
                return !string.IsNullOrWhiteSpace(selectedValue);
            }
        }

        return false;
    }

    private static string? ResolveInitialDateText(ContentControlProperties properties, DateTimeOffset? currentValue)
    {
        if (!currentValue.HasValue)
        {
            return null;
        }

        var culture = CultureInfo.CurrentCulture;
        if (!string.IsNullOrWhiteSpace(properties.DateFormat))
        {
            try
            {
                return currentValue.Value.ToString(properties.DateFormat, culture);
            }
            catch
            {
            }
        }

        return currentValue.Value.ToString("d", culture);
    }
}
