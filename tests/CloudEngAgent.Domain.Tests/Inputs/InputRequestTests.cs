using CloudEngAgent.Domain.Inputs;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests.Inputs;

public class InputRequestTests
{
    private static InputRequest NewPending(DateTimeOffset? created = null, DateTimeOffset? expires = null)
    {
        var c = created ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var e = expires ?? c.AddMinutes(5);
        return new InputRequest(
            Id: Guid.NewGuid(),
            RunId: Guid.NewGuid(),
            StepId: "step-1",
            AgentId: "analyst",
            SchemaRef: "approval-card@v1",
            Status: InputRequestStatus.Pending,
            CreatedAt: c,
            ExpiresAt: e,
            PayloadJson: null);
    }

    [Fact]
    public void Accept_from_pending_returns_accepted_with_payload()
    {
        var req = NewPending();
        var now = req.CreatedAt.AddMinutes(1);

        var accepted = req.Accept("{\"choice\":\"approve\"}", now);

        accepted.Status.Should().Be(InputRequestStatus.Accepted);
        accepted.PayloadJson.Should().Be("{\"choice\":\"approve\"}");
        accepted.Id.Should().Be(req.Id);
        req.Status.Should().Be(InputRequestStatus.Pending);
    }

    [Fact]
    public void Double_Accept_throws()
    {
        var req = NewPending();
        var now = req.CreatedAt.AddMinutes(1);
        var accepted = req.Accept("{}", now);

        var act = () => accepted.Accept("{}", now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Accept_after_expiry_throws()
    {
        var req = NewPending();
        var afterExpiry = req.ExpiresAt.AddSeconds(1);

        var act = () => req.Accept("{}", afterExpiry);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Accept_exactly_at_expiry_throws()
    {
        var req = NewPending();

        var act = () => req.Accept("{}", req.ExpiresAt);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reject_from_pending_returns_rejected()
    {
        var req = NewPending();

        var rejected = req.Reject(req.CreatedAt.AddSeconds(1));

        rejected.Status.Should().Be(InputRequestStatus.Rejected);
        rejected.PayloadJson.Should().BeNull();
    }

    [Fact]
    public void Reject_from_accepted_throws()
    {
        var req = NewPending();
        var accepted = req.Accept("{}", req.CreatedAt.AddSeconds(1));

        var act = () => accepted.Reject(req.CreatedAt.AddSeconds(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Expire_from_pending_returns_expired()
    {
        var req = NewPending();

        var expired = req.Expire(req.ExpiresAt.AddSeconds(1));

        expired.Status.Should().Be(InputRequestStatus.Expired);
    }

    [Fact]
    public void Expire_from_accepted_throws()
    {
        var req = NewPending();
        var accepted = req.Accept("{}", req.CreatedAt.AddSeconds(1));

        var act = () => accepted.Expire(req.ExpiresAt.AddSeconds(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var c = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var e = c.AddMinutes(5);

        var a = new InputRequest(id, runId, "s", "a", "schema@v1", InputRequestStatus.Pending, c, e, null);
        var b = new InputRequest(id, runId, "s", "a", "schema@v1", InputRequestStatus.Pending, c, e, null);

        a.Should().Be(b);
    }
}
