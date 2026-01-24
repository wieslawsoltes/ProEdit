namespace Vibe.Office.Documents;

public sealed class MailMergeData
{
    public List<string> FieldNames { get; } = new List<string>();
    public List<MailMergeRecord> Records { get; } = new List<MailMergeRecord>();

    public MailMergeData Clone()
    {
        var clone = new MailMergeData();
        foreach (var name in FieldNames)
        {
            clone.FieldNames.Add(name);
        }

        foreach (var record in Records)
        {
            clone.Records.Add(record.Clone());
        }

        return clone;
    }
}

public sealed class MailMergeRecord
{
    public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public MailMergeRecord Clone()
    {
        var clone = new MailMergeRecord();
        foreach (var pair in Fields)
        {
            clone.Fields[pair.Key] = pair.Value;
        }

        return clone;
    }

    public bool TryGetValue(string key, out string value)
    {
        return Fields.TryGetValue(key, out value!);
    }
}
