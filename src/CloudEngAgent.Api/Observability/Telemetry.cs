using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CloudEngAgent.Api.Observability;

/// <summary>
/// Central names for ActivitySource and Meter so OpenTelemetry instrumentation
/// can reliably subscribe to them. The static fields are also the runtime entry
/// points used throughout the run pipeline.
/// </summary>
public static class Telemetry
{
    public const string ActivitySourceName = "CloudEngAgent.Runs";
    public const string MeterName = "CloudEngAgent.Runs";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> RunsStarted = Meter.CreateCounter<long>(
        "cloudeng.runs.started",
        unit: "{run}",
        description: "Number of workflow runs started.");

    public static readonly Counter<long> RunsFinished = Meter.CreateCounter<long>(
        "cloudeng.runs.finished",
        unit: "{run}",
        description: "Number of workflow runs that reached a terminal status.");

    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "cloudeng.events.published",
        unit: "{event}",
        description: "Number of run events published to the event bus.");

    public static readonly UpDownCounter<long> SseClientsActive = Meter.CreateUpDownCounter<long>(
        "cloudeng.sse.clients.active",
        unit: "{client}",
        description: "Number of currently connected SSE clients.");
}
