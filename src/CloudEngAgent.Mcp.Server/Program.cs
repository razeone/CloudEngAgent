using CloudEngAgent.Mcp.Server.Options;
using CloudEngAgent.Mcp.Server.Sql;
using CloudEngAgent.Mcp.Server.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SqlServerOptions>()
    .Bind(builder.Configuration.GetSection(SqlServerOptions.SectionName));

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<SqlServerTools>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapMcp("/mcp");

app.Run();

// Exposed so WebApplicationFactory<Program> can boot the host in tests.
public partial class Program;
