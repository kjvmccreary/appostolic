using System.Text.Json;

namespace Appostolic.Api.Domain.Agents;

public record class Agent
{
    public Guid Id { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Name is required", nameof(Name));
            if (value.Length > 120) throw new ArgumentOutOfRangeException(nameof(Name), "Name must be <= 120 characters");
            _name = value;
        }
    }

    public string SystemPrompt { get; set; } = string.Empty;

    // Stored as JSONB in DB; here represented as string[]
    public string[] ToolAllowlist { get; set; } = Array.Empty<string>();

    private string _model = string.Empty;
    public string Model
    {
        get => _model;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Model is required", nameof(Model));
            if (value.Length > 80) throw new ArgumentOutOfRangeException(nameof(Model), "Model must be <= 80 characters");
            _model = value;
        }
    }

    private double _temperature = 0.2;
    public double Temperature
    {
        get => _temperature;
        set
        {
            if (value < 0 || value > 2) throw new ArgumentOutOfRangeException(nameof(Temperature), "Temperature must be between 0 and 2");
            _temperature = value;
        }
    }

    private int _maxSteps = 8;
    public int MaxSteps
    {
        get => _maxSteps;
        set
        {
            if (value < 1 || value > 50) throw new ArgumentOutOfRangeException(nameof(MaxSteps), "MaxSteps must be between 1 and 50");
            _maxSteps = value;
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public bool IsEnabled { get; set; } = true;

    // EF Core requires parameterless ctor
    public Agent() { }

    public Agent(Guid id, string name, string systemPrompt, string[] toolAllowlist, string model, double temperature = 0.2, int maxSteps = 8)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        // Invariants via Guard helpers
        Name = Application.Validation.Guard.MaxLength(
            Application.Validation.Guard.NotNullOrWhiteSpace(name, nameof(name)), 120, nameof(name));
        SystemPrompt = systemPrompt ?? string.Empty;
        ToolAllowlist = toolAllowlist ?? Array.Empty<string>();
        Model = Application.Validation.Guard.MaxLength(
            Application.Validation.Guard.NotNullOrWhiteSpace(model, nameof(model)), 80, nameof(model));
        Temperature = Application.Validation.Guard.InRange(temperature, 0.0, 2.0, nameof(temperature));
        MaxSteps = Application.Validation.Guard.InRange(maxSteps, 1, 50, nameof(maxSteps));
        CreatedAt = DateTime.UtcNow;
    IsEnabled = true;
    }
}
