using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Widgets;
using CloudEngAgent.Infrastructure.Widgets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Widgets;

public sealed class WidgetRegistryTests
{
    private static WidgetRegistry CreateRegistry(IPersonaWidgetPolicy? personaPolicy = null) =>
        new(personaPolicy ?? NullPersonaWidgetPolicy.Instance, NullLogger<WidgetRegistry>.Instance);

    public static IEnumerable<object[]> AllWidgetTypes() =>
        WidgetType.All.Select(t => new object[] { t });

    [Fact]
    public void Constructor_loads_all_eight_schemas()
    {
        var registry = CreateRegistry();

        registry.Types.Should().BeEquivalentTo(WidgetType.All);
        registry.Types.Should().HaveCount(8);

        foreach (var type in WidgetType.All)
        {
            registry.GetSchemaJson(type).Should().NotBeNullOrWhiteSpace(
                "type {0} must have a non-empty embedded schema", type.Value);
        }
    }

    [Theory]
    [MemberData(nameof(AllWidgetTypes))]
    public void GetSchemaJson_returns_valid_json(WidgetType type)
    {
        var registry = CreateRegistry();
        var json = registry.GetSchemaJson(type);
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(ValidPropsByType))]
    public void Validate_returns_valid_for_well_formed_props(WidgetType type, string propsJson)
    {
        var registry = CreateRegistry();

        var result = registry.Validate(type, propsJson);

        result.IsValid.Should().BeTrue(
            "schema for {0} should accept canonical props but reported: {1}",
            type.Value, string.Join("; ", result.Errors));
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(MissingRequiredFieldByType))]
    public void Validate_returns_invalid_when_required_field_missing(WidgetType type, string propsJson)
    {
        var registry = CreateRegistry();

        var result = registry.Validate(type, propsJson);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ExtraRootFieldByType))]
    public void Validate_returns_invalid_when_unknown_root_field_present(WidgetType type, string propsJson)
    {
        var registry = CreateRegistry();

        var result = registry.Validate(type, propsJson);

        result.IsValid.Should().BeFalse(
            "additionalProperties:false at root must reject extras for {0}", type.Value);
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_returns_invalid_for_malformed_json()
    {
        var registry = CreateRegistry();

        var result = registry.Validate(WidgetType.KpiCards, "{ not json");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.StartsWith("Invalid JSON", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("result-table", 1_048_576)]
    [InlineData("bar-chart", 262_144)]
    [InlineData("kpi-cards", 65_536)]
    [InlineData("findings-list", 524_288)]
    [InlineData("ddl-diff", 262_144)]
    [InlineData("approval-card", 65_536)]
    [InlineData("file-download", 8_192)]
    [InlineData("markdown-report", 524_288)]
    public void GetPolicy_returns_expected_max_payload_bytes(string typeId, int expected)
    {
        var registry = CreateRegistry();
        var policy = registry.GetPolicy(WidgetType.Parse(typeId));
        policy.MaxPayloadBytes.Should().Be(expected);
    }

    [Fact]
    public void GetPolicy_capability_flags_match_catalog()
    {
        var registry = CreateRegistry();

        registry.GetPolicy(WidgetType.ApprovalCard).CanRequestInput.Should().BeTrue();
        registry.GetPolicy(WidgetType.ResultTable).CanAttachArtifacts.Should().BeTrue();
        registry.GetPolicy(WidgetType.BarChart).CanAttachArtifacts.Should().BeFalse();
        registry.GetPolicy(WidgetType.ResultTable).MaxRows.Should().Be(5_000);
        registry.GetPolicy(WidgetType.BarChart).MaxSeries.Should().Be(50);
    }

    [Fact]
    public void IsAllowed_returns_true_for_any_persona_when_no_restrictions()
    {
        var registry = CreateRegistry();

        registry.IsAllowed("performance", WidgetType.BarChart).Should().BeTrue();
        registry.IsAllowed("explorer", WidgetType.ResultTable).Should().BeTrue();
        registry.IsAllowed("anyone", WidgetType.MarkdownReport).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_respects_persona_widget_policy()
    {
        var policy = new StubPersonaPolicy(new Dictionary<string, IReadOnlyList<WidgetType>>(StringComparer.Ordinal)
        {
            ["analyst"] = new[] { WidgetType.ResultTable },
        });
        var registry = CreateRegistry(policy);

        registry.IsAllowed("analyst", WidgetType.ResultTable).Should().BeTrue();
        registry.IsAllowed("analyst", WidgetType.BarChart).Should().BeFalse();
        // Personas without an entry remain unrestricted.
        registry.IsAllowed("performance", WidgetType.BarChart).Should().BeTrue();
    }

    public static IEnumerable<object[]> ValidPropsByType() => new[]
    {
        new object[] { WidgetType.ResultTable,
            """{"columns":[{"name":"id","type":"int"}],"rows":[{"id":1}],"pageSize":50,"totalKnown":true}""" },
        new object[] { WidgetType.BarChart,
            """{"title":"t","xLabel":"x","yLabel":"y","series":[{"name":"s","points":[{"x":1,"y":2.5}]}]}""" },
        new object[] { WidgetType.KpiCards,
            """{"cards":[{"label":"CPU","value":42,"unit":"ms","trend":"up"}]}""" },
        new object[] { WidgetType.FindingsList,
            """{"findings":[{"id":"F1","severity":"High","title":"t","description":"d","remediation":"r"}]}""" },
        new object[] { WidgetType.DdlDiff,
            """{"before":"a","after":"b","language":"tsql"}""" },
        new object[] { WidgetType.ApprovalCard,
            """{"title":"Apply?","summary":"s","payloadPreview":"p","inputRequestId":"11111111-1111-1111-1111-111111111111","options":[{"id":"approve","label":"Approve"}]}""" },
        new object[] { WidgetType.FileDownload,
            """{"artifactId":"22222222-2222-2222-2222-222222222222","filename":"r.pdf","contentType":"application/pdf","sizeBytes":1024}""" },
        new object[] { WidgetType.MarkdownReport,
            """{"markdown":"# hello"}""" },
    };

    public static IEnumerable<object[]> MissingRequiredFieldByType() => new[]
    {
        // result-table missing rows
        new object[] { WidgetType.ResultTable, """{"columns":[{"name":"id","type":"int"}],"pageSize":50,"totalKnown":true}""" },
        // bar-chart missing series
        new object[] { WidgetType.BarChart, """{"title":"t","xLabel":"x","yLabel":"y"}""" },
        // kpi-cards missing cards
        new object[] { WidgetType.KpiCards, """{}""" },
        // findings-list missing findings
        new object[] { WidgetType.FindingsList, """{}""" },
        // ddl-diff missing language
        new object[] { WidgetType.DdlDiff, """{"before":"a","after":"b"}""" },
        // approval-card missing inputRequestId
        new object[] { WidgetType.ApprovalCard, """{"title":"t","summary":"s","payloadPreview":"p","options":[{"id":"a","label":"A"}]}""" },
        // file-download missing sizeBytes
        new object[] { WidgetType.FileDownload, """{"artifactId":"22222222-2222-2222-2222-222222222222","filename":"r.pdf","contentType":"application/pdf"}""" },
        // markdown-report missing markdown
        new object[] { WidgetType.MarkdownReport, """{}""" },
    };

    public static IEnumerable<object[]> ExtraRootFieldByType() => new[]
    {
        new object[] { WidgetType.ResultTable,
            """{"columns":[{"name":"id","type":"int"}],"rows":[],"pageSize":50,"totalKnown":true,"unexpected":1}""" },
        new object[] { WidgetType.BarChart,
            """{"title":"t","xLabel":"x","yLabel":"y","series":[],"unexpected":1}""" },
        new object[] { WidgetType.KpiCards, """{"cards":[],"extra":true}""" },
        new object[] { WidgetType.FindingsList, """{"findings":[],"extra":true}""" },
        new object[] { WidgetType.DdlDiff, """{"before":"a","after":"b","language":"tsql","extra":true}""" },
        new object[] { WidgetType.ApprovalCard,
            """{"title":"t","summary":"s","payloadPreview":"p","inputRequestId":"11111111-1111-1111-1111-111111111111","options":[{"id":"a","label":"A"}],"extra":true}""" },
        new object[] { WidgetType.FileDownload,
            """{"artifactId":"22222222-2222-2222-2222-222222222222","filename":"r.pdf","contentType":"application/pdf","sizeBytes":1,"extra":true}""" },
        new object[] { WidgetType.MarkdownReport, """{"markdown":"# hi","extra":true}""" },
    };

    private sealed class StubPersonaPolicy(IReadOnlyDictionary<string, IReadOnlyList<WidgetType>> map)
        : IPersonaWidgetPolicy
    {
        public IReadOnlyList<WidgetType>? GetAllowedWidgets(string personaId) =>
            map.TryGetValue(personaId, out var list) ? list : null;
    }
}
