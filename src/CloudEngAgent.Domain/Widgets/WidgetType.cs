namespace CloudEngAgent.Domain.Widgets;

public readonly record struct WidgetType(string Value)
{
    public static WidgetType ResultTable { get; } = new("result-table");
    public static WidgetType BarChart { get; } = new("bar-chart");
    public static WidgetType KpiCards { get; } = new("kpi-cards");
    public static WidgetType FindingsList { get; } = new("findings-list");
    public static WidgetType DdlDiff { get; } = new("ddl-diff");
    public static WidgetType ApprovalCard { get; } = new("approval-card");
    public static WidgetType FileDownload { get; } = new("file-download");
    public static WidgetType MarkdownReport { get; } = new("markdown-report");

    public static IReadOnlyList<WidgetType> All { get; } = new[]
    {
        ResultTable, BarChart, KpiCards, FindingsList,
        DdlDiff, ApprovalCard, FileDownload, MarkdownReport,
    };

    public static WidgetType Parse(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.Value, value, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        var known = string.Join(", ", All.Select(w => w.Value));
        throw new ArgumentException(
            $"Unknown widget type '{value}'. Known: {known}.",
            nameof(value));
    }

    public override string ToString() => Value;
}
