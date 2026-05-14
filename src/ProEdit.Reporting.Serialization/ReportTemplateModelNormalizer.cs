namespace ProEdit.Reporting.Serialization;

internal static class ReportTemplateModelNormalizer
{
    public static void Normalize(ReportDefinition reportDefinition)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);

        reportDefinition.Sections ??= [];
        reportDefinition.Parameters ??= [];
        reportDefinition.DataSources ??= [];
        reportDefinition.DataSets ??= [];
        reportDefinition.Styles ??= [];
        reportDefinition.SharedTemplates ??= [];
        reportDefinition.Metadata = EnsureCaseInsensitive(reportDefinition.Metadata);

        foreach (var section in reportDefinition.Sections)
        {
            Normalize(section);
        }

        foreach (var parameter in reportDefinition.Parameters)
        {
            Normalize(parameter);
        }

        foreach (var dataSource in reportDefinition.DataSources)
        {
            Normalize(dataSource);
        }

        foreach (var dataSet in reportDefinition.DataSets)
        {
            Normalize(dataSet);
        }
    }

    private static void Normalize(ReportSection section)
    {
        section.PageSettings ??= new ReportPageSettings();
        section.HeaderItems ??= [];
        section.FooterItems ??= [];
        section.BodyItems ??= [];

        foreach (var item in section.HeaderItems)
        {
            Normalize(item);
        }

        foreach (var item in section.FooterItems)
        {
            Normalize(item);
        }

        foreach (var item in section.BodyItems)
        {
            Normalize(item);
        }
    }

    private static void Normalize(ReportParameterDefinition parameter)
    {
        parameter.Dependencies ??= [];
    }

    private static void Normalize(ReportDataSourceDefinition dataSource)
    {
        dataSource.Options = EnsureCaseInsensitive(dataSource.Options);
    }

    private static void Normalize(ReportDataSetDefinition dataSet)
    {
        dataSet.Parameters ??= [];
        dataSet.CalculatedFields ??= [];
        dataSet.Filters ??= [];
        dataSet.Sorts ??= [];
        dataSet.ExpectedFields ??= [];
    }

    private static void Normalize(ReportItem item)
    {
        switch (item)
        {
            case ContainerItem container:
                container.Items ??= [];
                foreach (var child in container.Items)
                {
                    Normalize(child);
                }

                break;
            case ChartItem chart:
                chart.Series ??= [];
                break;
            case GaugeItem:
                break;
            case TablixItem tablix:
                tablix.Filters ??= [];
                tablix.Columns ??= [];
                tablix.Rows ??= [];
                foreach (var row in tablix.Rows)
                {
                    row.Cells ??= [];
                    foreach (var cell in row.Cells)
                    {
                        if (cell.ContentItem is not null)
                        {
                            Normalize(cell.ContentItem);
                        }
                    }
                }

                break;
            case SubreportItem subreport:
                subreport.Parameters ??= [];
                break;
            case DocumentTemplateItem template:
                template.Bindings = EnsureCaseInsensitive(template.Bindings);
                break;
        }

        if (item.DrillthroughAction is { } action)
        {
            action.Parameters ??= [];
        }
    }

    private static Dictionary<string, string> EnsureCaseInsensitive(Dictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (source.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return source;
        }

        var result = new Dictionary<string, string>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }
}
