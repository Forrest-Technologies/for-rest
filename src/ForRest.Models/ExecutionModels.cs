namespace ForRest.Models;

public sealed record ConsoleEntry
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonConverter(typeof(JsonStringEnumConverter<ConsoleEntryLevel>))]
    public ConsoleEntryLevel Level { get; init; } = ConsoleEntryLevel.Info;

    public string Message { get; init; } = string.Empty;
}

public sealed record TestResult
{
    public string Name { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<TestOutcomeState>))]
    public TestOutcomeState State { get; init; } = TestOutcomeState.Passed;

    public string Message { get; init; } = string.Empty;
}

public sealed record StashRow
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Dictionary<string, string> Values { get; init; } = [];
}

public sealed record StashTable
{
    public List<string> Columns { get; init; } = [];

    public List<StashRow> Rows { get; init; } = [];
}

public sealed record ResponseSnapshot
{
    public int StatusCode { get; init; }

    public string ReasonPhrase { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public long DurationMilliseconds { get; init; }

    public string Body { get; init; } = string.Empty;

    public string RawResponse { get; init; } = string.Empty;

    public List<KeyValueDefinition> Headers { get; init; } = [];

    public List<KeyValueDefinition> Cookies { get; init; } = [];

    public DateTimeOffset ReceivedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RequestSnapshot
{
    public string Method { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string Body { get; init; } = string.Empty;

    public string RawRequest { get; init; } = string.Empty;

    public List<KeyValueDefinition> Headers { get; init; } = [];

    public DateTimeOffset SentUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ExecutionRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkspaceId { get; init; }

    public Guid RequestId { get; init; }

    public string RequestName { get; init; } = string.Empty;

    public int Iteration { get; init; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter<ExecutionState>))]
    public ExecutionState State { get; init; } = ExecutionState.Completed;

    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedUtc { get; init; }

    public string TargetUri { get; init; } = string.Empty;

    public string RawRequest { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public ResponseSnapshot? Response { get; init; }

    public List<ResponseSnapshot> Responses { get; init; } = [];

    public List<RequestSnapshot> Requests { get; init; } = [];

    public List<TestResult> Tests { get; init; } = [];

    public List<ConsoleEntry> ConsoleEntries { get; init; } = [];

    public List<VariableDefinition> RuntimeVariables { get; init; } = [];

    public StashTable Stash { get; init; } = new();
}

public sealed record RequestExecutionResult
{
    [JsonConverter(typeof(JsonStringEnumConverter<ExecutionState>))]
    public ExecutionState State { get; init; } = ExecutionState.Completed;

    public List<ExecutionRun> Runs { get; init; } = [];

    public ResponseSnapshot? LatestResponse { get; init; }

    public List<TestResult> Tests { get; init; } = [];

    public List<ConsoleEntry> ConsoleEntries { get; init; } = [];

    public List<VariableDefinition> RuntimeVariables { get; init; } = [];

    public StashTable Stash { get; init; } = new();
}

public sealed record ExecutionPreset
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkspaceId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int RepeatCount { get; init; } = 1;

    public int DelayMilliseconds { get; init; }

    public int IntervalMilliseconds { get; init; }
}
