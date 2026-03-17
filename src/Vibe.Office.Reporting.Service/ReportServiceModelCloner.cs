using Vibe.Office.Reporting.Serialization;

namespace Vibe.Office.Reporting.Service;

internal static class ReportServiceModelCloner
{
    public static ReportParameterValue CloneParameterValue(ReportParameterValue value)
    {
        var clone = new ReportParameterValue
        {
            IsNull = value.IsNull
        };

        for (var index = 0; index < value.Values.Count; index++)
        {
            clone.Values.Add(value.Values[index]);
        }

        for (var index = 0; index < value.Labels.Count; index++)
        {
            clone.Labels.Add(value.Labels[index]);
        }

        return clone;
    }

    public static void CopyParameters(
        IReadOnlyDictionary<string, ReportParameterValue> source,
        Dictionary<string, ReportParameterValue> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = CloneParameterValue(pair.Value);
        }
    }

    public static ReportScheduledOutputDefinition CloneOutput(ReportScheduledOutputDefinition output)
    {
        return new ReportScheduledOutputDefinition
        {
            Format = output.Format,
            FileNamePattern = output.FileNamePattern,
            TablixItemId = output.TablixItemId,
            IncludeHeaderRows = output.IncludeHeaderRows,
            CsvDelimiter = output.CsvDelimiter,
            WorkbookAuthor = output.WorkbookAuthor
        };
    }

    public static ReportDeliveryTargetDefinition CloneDeliveryTarget(ReportDeliveryTargetDefinition target)
    {
        var clone = new ReportDeliveryTargetDefinition
        {
            Id = target.Id,
            Name = target.Name,
            ChannelId = target.ChannelId,
            IsEnabled = target.IsEnabled
        };

        foreach (var pair in target.Properties)
        {
            clone.Properties[pair.Key] = pair.Value;
        }

        return clone;
    }

    public static ReportScheduleDefinition CloneSchedule(ReportScheduleDefinition schedule)
    {
        var clone = new ReportScheduleDefinition
        {
            Id = schedule.Id,
            Name = schedule.Name,
            ReportId = schedule.ReportId,
            RevisionNumber = schedule.RevisionNumber,
            IsEnabled = schedule.IsEnabled,
            StartsAt = schedule.StartsAt,
            EndsAt = schedule.EndsAt,
            Interval = schedule.Interval,
            NextRunAt = schedule.NextRunAt,
            LastRunAt = schedule.LastRunAt,
            Output = CloneOutput(schedule.Output)
        };

        CopyParameters(schedule.ParameterValues, clone.ParameterValues);

        for (var index = 0; index < schedule.DeliveryTargetIds.Count; index++)
        {
            clone.DeliveryTargetIds.Add(schedule.DeliveryTargetIds[index]);
        }

        foreach (var pair in schedule.Metadata)
        {
            clone.Metadata[pair.Key] = pair.Value;
        }

        return clone;
    }

    public static ReportAuditEntry CloneAuditEntry(ReportAuditEntry entry)
    {
        var clone = new ReportAuditEntry
        {
            Id = entry.Id,
            Timestamp = entry.Timestamp,
            EventKind = entry.EventKind,
            Severity = entry.Severity,
            Message = entry.Message,
            ReportId = entry.ReportId,
            RevisionNumber = entry.RevisionNumber,
            ScheduleId = entry.ScheduleId,
            DeliveryTargetId = entry.DeliveryTargetId
        };

        foreach (var pair in entry.Metadata)
        {
            clone.Metadata[pair.Key] = pair.Value;
        }

        return clone;
    }

    public static string ResolveFileNamePattern(
        string pattern,
        string reportId,
        string reportName,
        string? scheduleId,
        DateTimeOffset timestamp)
    {
        var resolved = pattern
            .Replace("{reportId}", SanitizeFileName(reportId), StringComparison.OrdinalIgnoreCase)
            .Replace("{reportName}", SanitizeFileName(reportName), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", timestamp.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(scheduleId))
        {
            resolved = resolved.Replace("{scheduleId}", SanitizeFileName(scheduleId), StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    public static string SanitizeFileName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "report";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var isInvalid = false;
            for (var invalidIndex = 0; invalidIndex < invalidCharacters.Length; invalidIndex++)
            {
                if (invalidCharacters[invalidIndex] == character)
                {
                    isInvalid = true;
                    break;
                }
            }

            builder.Append(isInvalid ? '-' : character);
        }

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "report" : sanitized;
    }

    public static ReportDefinition? CloneReportDefinition(
        IReportTemplateSerializer serializer,
        ReportDefinition reportDefinition,
        List<ReportDiagnostic> diagnostics)
    {
        var writeResult = serializer.Write(reportDefinition);
        AddDiagnostics(writeResult.Diagnostics, diagnostics);
        if (writeResult.HasErrors)
        {
            return null;
        }

        var readResult = serializer.Read(writeResult.Text);
        AddDiagnostics(readResult.Diagnostics, diagnostics);
        return readResult.HasErrors ? null : readResult.ReportDefinition;
    }

    public static void AddDiagnostics(
        IReadOnlyList<ReportDiagnostic> source,
        List<ReportDiagnostic> target)
    {
        for (var index = 0; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }
}
