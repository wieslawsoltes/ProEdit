namespace Vibe.Office.Editing;

public sealed class ProofingRuleSet
{
    private readonly HashSet<string> _disabledRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledCategories = new(StringComparer.OrdinalIgnoreCase);

    public void DisableRule(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return;
        }

        _disabledRules.Add(ruleId.Trim());
    }

    public void EnableRule(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return;
        }

        _disabledRules.Remove(ruleId.Trim());
    }

    public void DisableCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        _disabledCategories.Add(category.Trim());
    }

    public void EnableCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        _disabledCategories.Remove(category.Trim());
    }

    public bool IsEnabled(string? ruleId, string? category)
    {
        if (!string.IsNullOrWhiteSpace(ruleId) && _disabledRules.Contains(ruleId.Trim()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(category) && _disabledCategories.Contains(category.Trim()))
        {
            return false;
        }

        return true;
    }
}
