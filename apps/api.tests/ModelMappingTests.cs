using System.Linq;
using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Appostolic.Api.Tests;

public class ModelMappingTests
{
    private static AppDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static AppDbContext CreateRelationalDbContext()
    {
        // Use Npgsql provider for relational metadata; connection won't be used
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=55432;Database=appdb;Username=appuser;Password=apppassword")
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public void AgentTask_Status_is_converted_as_string()
    {
        using var db = CreateRelationalDbContext();
        var et = db.Model.FindEntityType(typeof(AgentTask));
        et.Should().NotBeNull();
        var statusProp = et!.FindProperty(nameof(AgentTask.Status));
        statusProp.Should().NotBeNull();
        // Assert the converter maps to string
        var conv = statusProp!.GetValueConverter();
        if (conv is not null)
        {
            conv.ProviderClrType.Should().Be(typeof(string));
        }
        else
        {
            statusProp.GetProviderClrType().Should().Be(typeof(string));
        }
    }

    [Fact]
    public void AgentTrace_Kind_is_mapped_as_int()
    {
        using var db = CreateRelationalDbContext();
        var et = db.Model.FindEntityType(typeof(AgentTrace));
        et.Should().NotBeNull();
        var kindProp = et!.FindProperty(nameof(AgentTrace.Kind));
        kindProp.Should().NotBeNull();
        // Default enum mapping should be numeric (no value converter configured)
        kindProp!.GetValueConverter().Should().BeNull();
        kindProp.ClrType.Should().Be(typeof(TraceKind));
    }

    [Fact]
    public void Agent_ToolAllowlist_is_jsonb_and_roundtrips_like_array()
    {
        // Using InMemory provider: we can't assert jsonb column type,
        // but we can assert the CLR shape and round-trip behavior at the entity level.
        using var db = CreateDbContext();
        var agent = new Agent(Guid.NewGuid(), "Test", "", new[] { "filesystem", "web.research" }, "gpt-4o-mini", 0.2, 8);
        db.Add(agent);
        db.SaveChanges();

        var got = db.Set<Agent>().Single(a => a.Id == agent.Id);
        got.ToolAllowlist.Should().BeEquivalentTo(new[] { "filesystem", "web.research" });
    }

    [Fact]
    public void Agent_ToolAllowlist_has_column_type_jsonb()
    {
        using var db = CreateRelationalDbContext();
        var et = db.Model.FindEntityType(typeof(Agent));
        et.Should().NotBeNull();
        var prop = et!.FindProperty(nameof(Agent.ToolAllowlist));
        prop.Should().NotBeNull();
        // Requires EFCore.Relational for GetColumnType
        prop!.GetColumnType().Should().Be("jsonb");
    }

    [Fact]
    public void Agent_check_constraints_are_configured_in_model()
    {
        using var db = CreateRelationalDbContext();
        // Use design-time model to access relational annotations like check constraints
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var et = designModel.FindEntityType(typeof(Agent));
        et.Should().NotBeNull();
        var checks = ((IEntityType)et!).GetCheckConstraints();
        checks.Select(c => c.Name).Should().Contain(new[]
        {
            "ck_agents_max_steps_range",
            "ck_agents_temperature_range"
        });
    }

    [Fact]
    public void Agent_indexes_are_configured_in_model()
    {
        using var db = CreateDbContext();
        var et = db.Model.FindEntityType(typeof(Agent));
        et.Should().NotBeNull();
        var indexes = et!.GetIndexes();
        // names depend on provider conventions but unique on Name is expected
        var hasUniqueName = indexes.Any(ix => ix.IsUnique && ix.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Agent.Name) }));
        hasUniqueName.Should().BeTrue();
    }
}
