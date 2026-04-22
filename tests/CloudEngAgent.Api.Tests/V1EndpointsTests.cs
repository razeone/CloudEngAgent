using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CloudEngAgent.Api.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CloudEngAgent.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string Environment { get; init; } = "Development";

    public IDictionary<string, string?> ExtraConfig { get; init; } = new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(ExtraConfig);
        });
    }
}

public class V1EndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public V1EndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetWorkflows_returns_seeded_dba_default()
    {
        using var client = _factory.CreateClient();

        var workflows = await client.GetFromJsonAsync<WorkflowDto[]>("/v1/workflows");

        workflows.Should().NotBeNull();
        workflows!.Should().ContainSingle(w => w.Id == "dba-default" && w.EntryPersonaId == "orchestrator");
    }

    [Fact]
    public async Task GetPersonas_returns_six_seeded_personas()
    {
        using var client = _factory.CreateClient();

        var personas = await client.GetFromJsonAsync<PersonaDto[]>("/v1/personas");

        personas.Should().NotBeNull();
        personas!.Select(p => p.Id).Should().BeEquivalentTo(new[]
        {
            "orchestrator", "explorer", "analyst", "performance", "assessor", "administrator",
        });
    }

    [Fact]
    public async Task PostRun_returns_accepted_with_runId_and_run_becomes_visible()
    {
        using var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/v1/runs", new StartRunRequest("dba-default", "Hello"));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await resp.Content.ReadFromJsonAsync<StartRunResponse>();
        body.Should().NotBeNull();
        body!.RunId.Should().NotBe(Guid.Empty);
        body.EventsUrl.Should().Be($"/v1/runs/{body.RunId}/events");

        // The run may not be persisted before the dispatcher returns; poll briefly.
        RunDto? run = null;
        for (var i = 0; i < 20 && run is null; i++)
        {
            var get = await client.GetAsync($"/v1/runs/{body.RunId}");
            if (get.StatusCode == HttpStatusCode.OK)
            {
                run = await get.Content.ReadFromJsonAsync<RunDto>();
            }
            else
            {
                await Task.Delay(50);
            }
        }

        run.Should().NotBeNull();
        run!.WorkflowId.Should().Be("dba-default");
    }

    [Fact]
    public async Task PostRun_with_unknown_workflow_returns_404()
    {
        using var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/v1/runs", new StartRunRequest("nope", "Hello"));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SseStream_emits_RunStarted_and_terminates_with_RunFinished()
    {
        using var client = _factory.CreateClient();

        var start = await client.PostAsJsonAsync("/v1/runs", new StartRunRequest("dba-default", "Hello"));
        var startResp = await start.Content.ReadFromJsonAsync<StartRunResponse>();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/runs/{startResp!.RunId}/events");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();

        var (events, lastId) = await ReadSseAsync(resp, cts.Token);

        events.Select(e => e.evt).Should().Contain("RunStarted");
        events.Select(e => e.evt).Should().Contain(new[] { "TextMessageChunk", "ToolCallStart", "ToolCallResult" });
        events.Last().evt.Should().Be("RunFinished");

        // Monotonic ids
        var ids = events.Select(e => e.id).ToArray();
        ids.Should().BeInAscendingOrder();
        lastId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SseStream_resumes_after_LastEventId()
    {
        using var client = _factory.CreateClient();

        var start = await client.PostAsJsonAsync("/v1/runs", new StartRunRequest("dba-default", "Hello"));
        var startResp = await start.Content.ReadFromJsonAsync<StartRunResponse>();

        // Wait for the run to finish so all events are persisted.
        for (var i = 0; i < 50; i++)
        {
            var run = await client.GetFromJsonAsync<RunDto>($"/v1/runs/{startResp!.RunId}");
            if (run!.EndedAt is not null) break;
            await Task.Delay(50);
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/runs/{startResp!.RunId}/events");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("Last-Event-ID", "0");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();

        var (events, _) = await ReadSseAsync(resp, cts.Token);

        // SequenceNo 0 corresponds to RunStarted; with Last-Event-ID=0 we should
        // resume *after* it — the first event we see must not be RunStarted.
        events.Should().NotBeEmpty();
        events.First().evt.Should().NotBe("RunStarted");
    }

    [Fact]
    public async Task NonDevelopment_without_entra_refuses_to_start()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var factory = new ApiFactory { Environment = "Production" };
            using var _ = factory.CreateClient();
            await Task.CompletedTask;
        });
    }

    private static async Task<(List<(long id, string evt, string data)> events, long lastId)> ReadSseAsync(
        HttpResponseMessage resp,
        CancellationToken cancellationToken)
    {
        var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var events = new List<(long id, string evt, string data)>();
        long lastId = -1;
        string? curEvent = null;
        string? curData = null;
        long curId = -1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            if (line.StartsWith(": ", StringComparison.Ordinal)) continue;

            if (line.Length == 0)
            {
                if (curEvent is not null && curData is not null)
                {
                    events.Add((curId, curEvent, curData));
                    if (curId > lastId) lastId = curId;
                    if (curEvent is "RunFinished" or "RunError") return (events, lastId);
                }

                curEvent = curData = null;
                curId = -1;
                continue;
            }

            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                long.TryParse(line[4..], out curId);
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                curEvent = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                curData = line[6..];
            }
        }

        return (events, lastId);
    }
}
