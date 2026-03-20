namespace ForRest.Scripting;

public sealed record ScriptExecutionRequest
{
    public string Script { get; init; } = string.Empty;

    public PreparedRequest PreparedRequest { get; init; } = new();

    public ResponseSnapshot? Response { get; init; }

    public WorkspaceDefinition Workspace { get; init; } = new();

    public List<VariableDefinition> GlobalVariables { get; init; } = [];

    public List<VariableDefinition> WorkspaceVariables { get; init; } = [];

    public List<VariableDefinition> EnvironmentVariables { get; init; } = [];

    public List<VariableDefinition> RequestVariables { get; init; } = [];

    public List<VariableDefinition> RuntimeVariables { get; init; } = [];

    public Func<PreparedRequest, Task<ResponseSnapshot?>>? SendAsync { get; init; }

    public int MaxSendIterations { get; init; }
}

public sealed record ScriptExecutionResult
{
    public PreparedRequest PreparedRequest { get; init; } = new();

    public ResponseSnapshot? Response { get; init; }

    public List<VariableDefinition> RuntimeVariables { get; init; } = [];

    public List<TestResult> Tests { get; init; } = [];

    public List<ConsoleEntry> ConsoleEntries { get; init; } = [];

    public string ErrorMessage { get; init; } = string.Empty;
}
