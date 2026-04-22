using CloudEngAgent.Domain.Runs;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests;

public class RunTests
{
    private static Run NewRun(RunStatus status = RunStatus.Pending) => new(
        Guid.NewGuid(),
        "dba-triage",
        status,
        DateTimeOffset.UtcNow,
        EndedAt: null,
        InputSummary: null);

    [Theory]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    public void WithStatus_terminal_sets_endedAt(RunStatus terminal)
    {
        var run = NewRun();

        var next = run.WithStatus(terminal);

        next.Status.Should().Be(terminal);
        next.EndedAt.Should().NotBeNull();
        next.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void WithStatus_non_terminal_keeps_endedAt_null()
    {
        var run = NewRun();

        var next = run.WithStatus(RunStatus.Running);

        next.Status.Should().Be(RunStatus.Running);
        next.EndedAt.Should().BeNull();
        next.IsTerminal.Should().BeFalse();
    }
}
