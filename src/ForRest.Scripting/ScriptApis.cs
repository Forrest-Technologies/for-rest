namespace ForRest.Scripting;

public sealed class ScriptRequestApi
{
    #region Constructors

    public ScriptRequestApi(PreparedRequest request)
    {
        Method = request.Method.ToString().ToUpperInvariant();
        Url = request.Uri.ToString();
        Body = request.Body.RawContent;
        ContentType = request.Body.ContentType;
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers.Where(static item => item.IsEnabled))
        {
            Headers[header.Key] = header.Value;
        }
    }

    #endregion

    #region Properties

    public string Method { get; set; }

    public string Url { get; set; }

    public string Body { get; set; }

    public string ContentType { get; set; }

    public Dictionary<string, string> Headers { get; }

    #endregion

    #region Public Methods

    public void SetHeader(string key, string value)
    {
        Headers[key] = value;
    }

    public void RemoveHeader(string key)
    {
        Headers.Remove(key);
    }

    #endregion
}

public sealed class ScriptResponseApi(ResponseSnapshot? response)
{
    #region Properties

    public int Status => response?.StatusCode ?? 0;

    public string Body => response?.Body ?? string.Empty;

    public string ContentType => response?.ContentType ?? string.Empty;

    public Dictionary<string, string> Headers => response?.Headers.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Methods

    public JsonNode? Json()
    {
        return string.IsNullOrWhiteSpace(response?.Body) ? null : JsonNode.Parse(response.Body);
    }

    #endregion
}

public sealed class VariablesApi(IEnumerable<VariableDefinition> seedVariables)
{
    #region Private Fields

    private readonly Dictionary<string, VariableDefinition> variables = seedVariables.ToDictionary(static item => item.Key, StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Methods

    public string Get(string key, string defaultValue = "")
    {
        return variables.TryGetValue(key, out var variable) ? variable.Value : defaultValue;
    }

    public void Set(string key, string value, VariableScope scope = VariableScope.Runtime, bool isSecret = false)
    {
        variables[key] = new()
        {
            Key = key,
            Value = value,
            Scope = scope,
            IsSecret = isSecret,
        };
    }

    public IReadOnlyCollection<VariableDefinition> All()
    {
        return variables.Values.ToList();
    }

    #endregion
}

public sealed class TestsApi
{
    #region Private Fields

    private readonly List<TestResult> testResults = [];

    #endregion

    #region Public Methods

    public IReadOnlyList<TestResult> All()
    {
        return testResults;
    }

    public void Assert(bool condition, string message)
    {
        testResults.Add(new()
        {
            Name = message,
            Message = message,
            State = condition ? TestOutcomeState.Passed : TestOutcomeState.Failed,
        });

        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Equal<T>(T expected, T actual, string message)
    {
        var success = EqualityComparer<T>.Default.Equals(expected, actual);
        Assert(success, success ? message : $"{message} Expected '{expected}', received '{actual}'.");
    }

    public void Fail(string message)
    {
        Assert(false, message);
    }

    public void Pass(string message)
    {
        testResults.Add(new()
        {
            Name = message,
            Message = message,
            State = TestOutcomeState.Passed,
        });
    }

    #endregion
}

public sealed class ConsoleApi
{
    #region Private Fields

    private readonly List<ConsoleEntry> entries = [];

    #endregion

    #region Public Methods

    public IReadOnlyList<ConsoleEntry> All()
    {
        return entries;
    }

    public void Log(object? value)
    {
        Add(ConsoleEntryLevel.Info, value);
    }

    public void Warn(object? value)
    {
        Add(ConsoleEntryLevel.Warning, value);
    }

    public void Error(object? value)
    {
        Add(ConsoleEntryLevel.Error, value);
    }

    #endregion

    #region Private Methods

    private void Add(ConsoleEntryLevel level, object? value)
    {
        entries.Add(new()
        {
            Level = level,
            Message = value?.ToString() ?? string.Empty,
        });
    }

    #endregion
}

public sealed class TimeApi
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset Now => DateTimeOffset.Now;
}

public sealed class JsonApi
{
    #region Private Fields

    private readonly JsonNodeSelector selector = new();

    #endregion

    #region Public Methods

    public JsonNode? Parse(string content)
    {
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    public string Stringify(JsonNode? node, bool writeIndented = true)
    {
        return node?.ToJsonString(new()
        {
            WriteIndented = writeIndented,
        }) ?? string.Empty;
    }

    public string? Select(JsonNode? node, string selectorText)
    {
        return selector.Select(node, selectorText);
    }

    #endregion
}

public sealed class RandomApi
{
    #region Private Fields

    private readonly Random random = Random.Shared;

    #endregion

    #region Public Methods

    public Guid Guid()
    {
        return System.Guid.NewGuid();
    }

    public int Number(int minimumInclusive, int maximumExclusive)
    {
        return random.Next(minimumInclusive, maximumExclusive);
    }

    #endregion
}

public sealed class WorkspaceApi(WorkspaceDefinition workspace)
{
    public Guid Id => workspace.Id;

    public string Name => workspace.Name;
}

public sealed class ScriptGlobals
{
    public required ScriptRequestApi request { get; init; }

    public required ScriptResponseApi response { get; init; }

    public required VariablesApi variables { get; init; }

    public required TestsApi tests { get; init; }

    public required ConsoleApi console { get; init; }

    public required TimeApi time { get; init; }

    public required JsonApi json { get; init; }

    public required RandomApi random { get; init; }

    public required WorkspaceApi workspace { get; init; }
}
