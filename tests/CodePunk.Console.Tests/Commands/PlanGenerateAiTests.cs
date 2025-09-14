using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiTests : ConsoleTestBase
{
    [Fact]
    public void PlanGenerateAi_Json_EmitsSchemaAndFields()
    {
        var output = Run("plan generate --ai --goal \"Add caching layer\" --json");
        var obj = JsonLast(output);
        obj.GetProperty("schema").GetString().Should().Be("plan.generate.ai.v1");
        obj.GetProperty("goal").GetString().Should().Be("Add caching layer");
        obj.GetProperty("planId").GetString().Should().NotBeNullOrWhiteSpace();
        obj.GetProperty("provider").GetString().Should().Be("stub");
        obj.GetProperty("model").GetString().Should().Be("stub-model");
        var files = obj.GetProperty("files").EnumerateArray();
        files.Should().HaveCount(1);
        var first = files.First();
        first.GetProperty("path").GetString().Should().Be("README.md");
        first.GetProperty("rationale").GetString().Should().Contain("Placeholder");
    }
}
