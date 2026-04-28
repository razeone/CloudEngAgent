namespace CloudEngAgent.Domain.Runs;

public enum RunEventType
{
    RunStarted = 0,
    TextDelta = 1,
    ToolCall = 2,
    ToolResult = 3,
    AgentHandoff = 4,
    RunFinished = 5,
    Error = 6,
    UiWidgetSnapshot = 7,
    UiWidgetDelta = 8,
    UiInputRequested = 9,
    UiInputReceived = 10,
    ArtifactCreated = 11,
}

public sealed record RunEvent(
    Guid RunId,
    RunEventType Type,
    string PayloadJson,
    long SequenceNo,
    DateTimeOffset OccurredAt);
