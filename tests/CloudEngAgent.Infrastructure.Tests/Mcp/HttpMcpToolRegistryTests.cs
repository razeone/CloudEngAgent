using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Mcp;
using CloudEngAgent.Domain.Tools;
using CloudEngAgent.Infrastructure.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Mcp;

public sealed class HttpMcpToolRegistryTests
{
    private static McpClientOptions OptionsFor(params McpServerConfig[] servers) => new()
    {
        Servers = servers.ToList(),
    };

    private static McpServerConfig Server(string name, string auth = "None", string? token = null) => new()
    {
        Name = name,
        Endpoint = $"http://{name}.local/mcp",
        AuthType = auth,
        TokenRef = token,
    };

    private static HttpMcpToolRegistry Build(
        McpClientOptions options,
        IMcpServerSessionFactory factory,
        out ListLogger<HttpMcpToolRegistry> logger)
    {
        logger = new ListLogger<HttpMcpToolRegistry>();
        return new HttpMcpToolRegistry(Options.Create(options), factory, logger);
    }

    private static IMcpServerSession StubSession(
        string name,
        IReadOnlyList<McpToolDescriptor> tools,
        Func<string, string, CancellationToken, Task<McpToolInvocationResult>>? invoker = null)
    {
        var session = Substitute.For<IMcpServerSession>();
        session.Name.Returns(name);
        session.ListToolsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(tools));
        session.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => invoker is null
                ? Task.FromResult(new McpToolInvocationResult(false, "{}"))
                : invoker(ci.ArgAt<string>(0), ci.ArgAt<string>(1), ci.ArgAt<CancellationToken>(2)));
        return session;
    }

    private static IMcpServerSessionFactory StubFactory(params (McpServerConfig Cfg, IMcpServerSession Session)[] mapping)
    {
        var factory = Substitute.For<IMcpServerSessionFactory>();
        foreach (var (cfg, session) in mapping)
        {
            factory.CreateAsync(
                    Arg.Is<McpServerConfig>(c => c.Name == cfg.Name),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(session));
        }
        return factory;
    }

    private static McpToolDescriptor Tool(string server, string name) =>
        new(new ToolRef(server, name), Description: $"{server}/{name}", JsonSchema: "{\"type\":\"object\"}");

    [Fact]
    public async Task ListToolsAsync_aggregates_tools_from_all_servers_with_server_prefix()
    {
        var a = Server("alpha");
        var b = Server("bravo");
        var sessionA = StubSession("alpha", new[] { Tool("alpha", "ping") });
        var sessionB = StubSession("bravo", new[] { Tool("bravo", "status") });
        var factory = StubFactory((a, sessionA), (b, sessionB));

        await using var registry = Build(OptionsFor(a, b), factory, out _);

        var tools = new List<McpToolDescriptor>();
        await foreach (var t in registry.ListToolsAsync(CancellationToken.None))
        {
            tools.Add(t);
        }

        tools.Select(t => t.ToolRef.Qualified).Should().BeEquivalentTo(
            new[] { "mcp:alpha.ping", "mcp:bravo.status" });
    }

    [Fact]
    public async Task ListToolsAsync_skips_unreachable_server_with_warning()
    {
        var a = Server("alpha");
        var b = Server("bravo");
        var sessionA = StubSession("alpha", new[] { Tool("alpha", "ping") });
        var factory = Substitute.For<IMcpServerSessionFactory>();
        factory.CreateAsync(Arg.Is<McpServerConfig>(c => c.Name == "alpha"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sessionA));
        factory.CreateAsync(Arg.Is<McpServerConfig>(c => c.Name == "bravo"), Arg.Any<CancellationToken>())
            .Returns<Task<IMcpServerSession>>(_ => throw new HttpRequestException("unreachable"));

        await using var registry = Build(OptionsFor(a, b), factory, out var logger);

        var tools = new List<McpToolDescriptor>();
        await foreach (var t in registry.ListToolsAsync(CancellationToken.None))
        {
            tools.Add(t);
        }

        tools.Should().HaveCount(1);
        tools[0].ToolRef.ServerName.Should().Be("alpha");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("bravo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListToolsAsync_skips_server_whose_ListToolsAsync_throws()
    {
        var a = Server("alpha");
        var b = Server("bravo");
        var sessionA = StubSession("alpha", new[] { Tool("alpha", "ping") });
        var sessionB = Substitute.For<IMcpServerSession>();
        sessionB.Name.Returns("bravo");
        sessionB.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<McpToolDescriptor>>>(_ => throw new InvalidOperationException("boom"));
        var factory = StubFactory((a, sessionA), (b, sessionB));

        await using var registry = Build(OptionsFor(a, b), factory, out var logger);

        var tools = new List<McpToolDescriptor>();
        await foreach (var t in registry.ListToolsAsync(CancellationToken.None))
        {
            tools.Add(t);
        }

        tools.Should().HaveCount(1);
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("bravo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListToolsAsync_last_wins_on_tool_name_collision_and_logs_warning()
    {
        // Two servers both report a tool whose qualified name collides.
        // We emulate this by having session B return a descriptor whose ToolRef server name is "alpha".
        var a = Server("alpha");
        var b = Server("bravo");
        var sessionA = StubSession("alpha", new[] { Tool("alpha", "ping") with { Description = "from-a" } });
        var sessionB = StubSession("bravo", new[] { Tool("alpha", "ping") with { Description = "from-b" } });
        var factory = StubFactory((a, sessionA), (b, sessionB));

        await using var registry = Build(OptionsFor(a, b), factory, out var logger);

        var tools = new List<McpToolDescriptor>();
        await foreach (var t in registry.ListToolsAsync(CancellationToken.None))
        {
            tools.Add(t);
        }

        tools.Should().ContainSingle();
        tools[0].Description.Should().Be("from-b");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("collision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvokeAsync_routes_to_correct_server_by_prefix()
    {
        var a = Server("alpha");
        var b = Server("bravo");
        var sessionACalls = new List<string>();
        var sessionBCalls = new List<string>();
        var sessionA = StubSession("alpha", Array.Empty<McpToolDescriptor>(),
            (tool, _, _) => { sessionACalls.Add(tool); return Task.FromResult(new McpToolInvocationResult(false, "\"a\"")); });
        var sessionB = StubSession("bravo", Array.Empty<McpToolDescriptor>(),
            (tool, _, _) => { sessionBCalls.Add(tool); return Task.FromResult(new McpToolInvocationResult(false, "\"b\"")); });
        var factory = StubFactory((a, sessionA), (b, sessionB));

        await using var registry = Build(OptionsFor(a, b), factory, out _);

        var result = await registry.InvokeAsync(new ToolRef("bravo", "doit"), "{}", CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.ResultJson.Should().Be("\"b\"");
        sessionACalls.Should().BeEmpty();
        sessionBCalls.Should().ContainSingle().Which.Should().Be("doit");
    }

    [Fact]
    public async Task InvokeAsync_maps_error_response_to_IsError_true()
    {
        var a = Server("alpha");
        var sessionA = StubSession("alpha", Array.Empty<McpToolDescriptor>(),
            (_, _, _) => Task.FromResult(new McpToolInvocationResult(true, "{\"error\":\"tool_failed\"}")));
        var factory = StubFactory((a, sessionA));

        await using var registry = Build(OptionsFor(a), factory, out _);

        var result = await registry.InvokeAsync(new ToolRef("alpha", "ping"), "{}", CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.ResultJson.Should().Contain("tool_failed");
    }

    [Fact]
    public async Task InvokeAsync_returns_error_for_unknown_server()
    {
        var a = Server("alpha");
        var factory = StubFactory((a, StubSession("alpha", Array.Empty<McpToolDescriptor>())));

        await using var registry = Build(OptionsFor(a), factory, out var logger);

        var result = await registry.InvokeAsync(new ToolRef("ghost", "ping"), "{}", CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.ResultJson.Should().Contain("unknown_server");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task InvokeAsync_returns_error_when_session_creation_fails()
    {
        var a = Server("alpha");
        var factory = Substitute.For<IMcpServerSessionFactory>();
        factory.CreateAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>())
            .Returns<Task<IMcpServerSession>>(_ => throw new HttpRequestException("nope"));

        await using var registry = Build(OptionsFor(a), factory, out var logger);

        var result = await registry.InvokeAsync(new ToolRef("alpha", "ping"), "{}", CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.ResultJson.Should().Contain("server_unreachable");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ListToolsAsync_caches_and_InvalidateCache_forces_refresh()
    {
        var a = Server("alpha");
        var callCount = 0;
        var session = Substitute.For<IMcpServerSession>();
        session.Name.Returns("alpha");
        session.ListToolsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            return Task.FromResult<IReadOnlyList<McpToolDescriptor>>(new[] { Tool("alpha", "ping") });
        });
        var factory = StubFactory((a, session));

        await using var registry = Build(OptionsFor(a), factory, out _);

        await Drain(registry.ListToolsAsync(CancellationToken.None));
        await Drain(registry.ListToolsAsync(CancellationToken.None));
        callCount.Should().Be(1);

        registry.InvalidateCache();
        await Drain(registry.ListToolsAsync(CancellationToken.None));
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Bearer_auth_resolves_token_via_secret_resolver_on_session_creation()
    {
        // Exercises the *real* SdkMcpServerSessionFactory path up to the point where
        // the token is requested. We don't care that SseClientTransport will fail to
        // connect — we only assert the resolver was called with the configured TokenRef.
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync("primary-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("secret-value"));

        var factory = new SdkMcpServerSessionFactory(resolver, NullLoggerFactory.Instance);
        var cfg = new McpServerConfig
        {
            Name = "primary",
            Endpoint = "http://does-not-exist.local/mcp",
            AuthType = "Bearer",
            TokenRef = "primary-token",
        };

        try
        {
            await factory.CreateAsync(cfg, CancellationToken.None);
        }
        catch
        {
            // Connection attempt is expected to fail in the test host; what matters
            // is that the bearer token was resolved before the HTTP call.
        }

        await resolver.Received(1).ResolveAsync("primary-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bearer_auth_without_TokenRef_throws()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        var factory = new SdkMcpServerSessionFactory(resolver, NullLoggerFactory.Instance);
        var cfg = new McpServerConfig
        {
            Name = "primary",
            Endpoint = "http://example.local/mcp",
            AuthType = "Bearer",
            TokenRef = null,
        };

        await FluentActions.Awaiting(() => factory.CreateAsync(cfg, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TokenRef*");
    }

    [Fact]
    public void DI_with_no_configured_servers_registers_EmptyMcpToolRegistry()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Personas:Directory"] = Path.GetTempPath(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IMcpToolRegistry>();
        registry.GetType().Name.Should().Be("EmptyMcpToolRegistry");
    }

    [Fact]
    public void DI_with_configured_servers_registers_HttpMcpToolRegistry()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Personas:Directory"] = Path.GetTempPath(),
                ["Mcp:Client:Servers:0:Name"] = "primary",
                ["Mcp:Client:Servers:0:Endpoint"] = "http://localhost:5010/mcp",
                ["Mcp:Client:Servers:0:AuthType"] = "None",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);

        var sp = services.BuildServiceProvider();
        try
        {
            var registry = sp.GetRequiredService<IMcpToolRegistry>();
            registry.Should().BeOfType<HttpMcpToolRegistry>();
        }
        finally
        {
            ((IAsyncDisposable)sp).DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void DI_rejects_invalid_server_name()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Personas:Directory"] = Path.GetTempPath(),
                ["Mcp:Client:Servers:0:Name"] = "bad name!",
                ["Mcp:Client:Servers:0:Endpoint"] = "http://localhost:5010/mcp",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);

        var sp = services.BuildServiceProvider();
        try
        {
            FluentActions.Invoking(() => sp.GetRequiredService<IOptions<McpClientOptions>>().Value)
                .Should().Throw<OptionsValidationException>();
        }
        finally
        {
            ((IAsyncDisposable)sp).DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task Drain(IAsyncEnumerable<McpToolDescriptor> source)
    {
        await foreach (var _ in source) { }
    }
}
