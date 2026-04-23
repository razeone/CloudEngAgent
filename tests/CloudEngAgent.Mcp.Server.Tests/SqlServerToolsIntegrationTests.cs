using CloudEngAgent.Mcp.Server.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace CloudEngAgent.Mcp.Server.Tests;

[Collection("MsSql")]
public sealed class SqlServerToolsIntegrationTests(MsSqlContainerFixture fixture)
{
    private bool ShouldSkip => fixture.SkipReason is not null;

    private SqlServerTools CreateTools() =>
        new(fixture.Factory!, NullLogger<SqlServerTools>.Instance);

    [Fact]
    public async Task ListDatabases_IncludesSeededDatabase()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var dbs = await tools.ListDatabasesAsync(database: "root", CancellationToken.None);

        dbs.Should().Contain(MsSqlContainerFixture.SeededDatabaseName);
    }

    [Fact]
    public async Task ListTables_ReturnsSeededTable()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var tables = await tools.ListTablesAsync(database: "seed", CancellationToken.None);

        tables.Should().ContainSingle(t =>
            t.Schema == MsSqlContainerFixture.SeededSchema &&
            t.Name == MsSqlContainerFixture.SeededTable);
    }

    [Fact]
    public async Task DescribeTable_ReturnsExpectedColumns()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var cols = await tools.DescribeTableAsync(
            schema: MsSqlContainerFixture.SeededSchema,
            table: MsSqlContainerFixture.SeededTable,
            database: "seed",
            cancellationToken: CancellationToken.None);

        cols.Select(c => c.Name).Should().BeEquivalentTo(new[] { "Id", "Name", "Email" });
        cols.Single(c => c.Name == "Email").IsNullable.Should().BeTrue();
        cols.Single(c => c.Name == "Id").IsNullable.Should().BeFalse();
    }

    [Fact]
    public async Task SampleRows_RespectsTopArgument()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var rows = await tools.SampleRowsAsync(
            schema: MsSqlContainerFixture.SeededSchema,
            table: MsSqlContainerFixture.SeededTable,
            top: 2,
            database: "seed",
            cancellationToken: CancellationToken.None);

        rows.Should().HaveCount(2);
        rows[0].Should().ContainKey("Id");
    }

    [Fact]
    public async Task SampleRows_HardCapsAt100()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var rows = await tools.SampleRowsAsync(
            schema: MsSqlContainerFixture.SeededSchema,
            table: MsSqlContainerFixture.SeededTable,
            top: 10_000,
            database: "seed",
            cancellationToken: CancellationToken.None);

        rows.Count.Should().BeLessThanOrEqualTo(SqlServerTools.MaxSampleRows);
    }

    [Fact]
    public async Task SampleRows_RejectsInjectionAttemptBeforeHittingDb()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var act = async () => await tools.SampleRowsAsync(
            schema: "dbo",
            table: "Customers; DROP TABLE Customers--",
            top: 1,
            database: "seed",
            cancellationToken: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<McpException>();
        ex.Which.ErrorCode.Should().Be(McpErrorCode.InvalidParams);
    }

    [Fact]
    public async Task DescribeTable_RejectsInjectionAttemptBeforeHittingDb()
    {
        if (ShouldSkip) return;
        var tools = CreateTools();

        var act = async () => await tools.DescribeTableAsync(
            schema: "dbo';--",
            table: "Customers",
            database: "seed",
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<McpException>();
    }
}
