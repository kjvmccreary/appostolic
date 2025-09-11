namespace Appostolic.Api.Domain.Agents;

public record class AgentTrace
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }

    private int _stepNumber = 1;
    public int StepNumber
    {
        get => _stepNumber;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(StepNumber), "StepNumber must be >= 1");
            _stepNumber = value;
        }
    }

    public TraceKind Kind { get; set; }

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

    private string _inputJson = string.Empty;
    public string InputJson
    {
        get => _inputJson;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("InputJson is required", nameof(InputJson));
            _inputJson = value;
        }
    }

    private string _outputJson = string.Empty;
    public string OutputJson
    {
        get => _outputJson;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("OutputJson is required", nameof(OutputJson));
            _outputJson = value;
        }
    }

    private int _durationMs;
    public int DurationMs
    {
        get => _durationMs;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(DurationMs), "DurationMs must be >= 0");
            _durationMs = value;
        }
    }

    private int _promptTokens;
    public int PromptTokens
    {
        get => _promptTokens;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(PromptTokens), "PromptTokens must be >= 0");
            _promptTokens = value;
        }
    }

    private int _completionTokens;
    public int CompletionTokens
    {
        get => _completionTokens;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(CompletionTokens), "CompletionTokens must be >= 0");
            _completionTokens = value;
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AgentTrace() { }

    public AgentTrace(Guid id, Guid taskId, int stepNumber, TraceKind kind, string name, string inputJson, string outputJson)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required", nameof(taskId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        TaskId = taskId;
        StepNumber = stepNumber;
        Kind = kind;
        Name = name;
        InputJson = inputJson;
        OutputJson = outputJson;
        CreatedAt = DateTime.UtcNow;
    }
}
