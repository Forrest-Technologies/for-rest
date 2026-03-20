using System.Dynamic;

namespace ForRest.Scripting;

public sealed class ScriptRequestApi
{
    #region Constructors

    public ScriptRequestApi(
        PreparedRequest request,
        ScriptResponseApi responseApi,
        Func<PreparedRequest, Task<ResponseSnapshot?>>? sendAsync = null,
        int maxSendIterations = 0)
    {
        originalRequest = request;
        this.responseApi = responseApi;
        this.sendAsync = sendAsync;
        this.maxSendIterations = Math.Max(0, maxSendIterations);
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

    private readonly PreparedRequest originalRequest;

    private readonly ScriptResponseApi responseApi;

    private readonly Func<PreparedRequest, Task<ResponseSnapshot?>>? sendAsync;

    private readonly int maxSendIterations;

    private int sendCount;

    public string Method { get; set; }

    public string Url { get; set; }

    public string Body { get; set; }

    public string ContentType { get; set; }

    public Dictionary<string, string> Headers { get; }

    public int MaxSendIterations => maxSendIterations;

    public int RemainingSendIterations => Math.Max(0, maxSendIterations - sendCount);

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

    public Task<ScriptResponseApi> send()
    {
        return SendAsync();
    }

    public async Task<ScriptResponseApi> SendAsync()
    {
        if (sendAsync is null || maxSendIterations <= 0)
        {
            throw new InvalidOperationException("request.send() is disabled for this request. Increase max_send_iterations to enable it.");
        }

        var nextSendCount = Interlocked.Increment(ref sendCount);
        if (nextSendCount > maxSendIterations)
        {
            throw new InvalidOperationException($"request.send() exceeded max_send_iterations ({maxSendIterations}).");
        }

        ResponseSnapshot? response = await sendAsync(ToPreparedRequest());
        responseApi.Update(response);
        return responseApi;
    }

    public PreparedRequest ToPreparedRequest()
    {
        var method = Enum.TryParse<HttpMethodKind>(Method, true, out var parsedMethod)
            ? parsedMethod
            : originalRequest.Method;

        var bodyMode = string.IsNullOrWhiteSpace(Body)
            ? RequestBodyMode.None
            : originalRequest.Body.Mode == RequestBodyMode.None
                ? RequestBodyMode.RawText
                : originalRequest.Body.Mode;

        return originalRequest with
        {
            Method = method,
            Uri = Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri : originalRequest.Uri,
            Headers =
            [
                .. Headers.Select(
                    item => new KeyValueDefinition
                    {
                        Key = item.Key,
                        Value = item.Value,
                    }),
            ],
            Body = originalRequest.Body with
            {
                Mode = bodyMode,
                RawContent = Body,
                ContentType = ContentType,
            },
        };
    }

    #endregion
}

public sealed class ScriptResponseApi(ResponseSnapshot? initialResponse) : DynamicObject
{
    #region Properties

    private ResponseSnapshot? response = initialResponse;

    private JsonNode? parsedJson;

    private bool jsonParsed;

    public int Status => response?.StatusCode ?? 0;

    public string Body => response?.Body ?? string.Empty;

    public string ContentType => response?.ContentType ?? string.Empty;

    public Dictionary<string, string> Headers => BuildHeaders(response);

    public ResponseSnapshot? Snapshot => response;

    #endregion

    #region Public Methods

    public void Update(ResponseSnapshot? nextResponse)
    {
        response = nextResponse;
        parsedJson = null;
        jsonParsed = false;
    }

    public JsonNode? Json()
    {
        if (jsonParsed)
        {
            return parsedJson;
        }

        jsonParsed = true;
        parsedJson = string.IsNullOrWhiteSpace(response?.Body) ? null : JsonNode.Parse(response.Body);
        return parsedJson;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        switch (binder.Name)
        {
            case nameof(Status):
                result = Status;
                return true;
            case nameof(Body):
                result = Body;
                return true;
            case nameof(ContentType):
                result = ContentType;
                return true;
            case nameof(Headers):
                result = Headers;
                return true;
        }

        if (Json() is JsonObject jsonObject && DynamicJsonObject.TryResolveMember(jsonObject, binder.Name, out result))
        {
            return true;
        }

        result = null;
        return false;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
    {
        if (Json() is not JsonObject jsonObject)
        {
            return [];
        }

        return jsonObject.Select(static item => item.Key);
    }

    #endregion

    #region Private Methods

    private static Dictionary<string, string> BuildHeaders(ResponseSnapshot? response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response is null)
        {
            return headers;
        }

        foreach (var header in response.Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            headers[header.Key] = header.Value;
        }

        return headers;
    }

    #endregion
}

internal sealed class DynamicJsonObject(JsonObject source) : DynamicObject
{
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        return TryResolveMember(source, binder.Name, out result);
    }

    public override bool TryGetIndex(GetIndexBinder binder, object?[] indexes, out object? result)
    {
        if (indexes.Length == 1 && indexes[0] is string key)
        {
            return TryResolveMember(source, key, out result);
        }

        result = null;
        return false;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
    {
        return source.Select(static item => item.Key);
    }

    public static bool TryResolveMember(JsonObject source, string memberName, out object? result)
    {
        if (source.TryGetPropertyValue(memberName, out JsonNode? node))
        {
            result = Wrap(node);
            return true;
        }

        result = null;
        return false;
    }

    private static object? Wrap(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject jsonObject => new DynamicJsonObject(jsonObject),
            JsonArray jsonArray => jsonArray.Select(Wrap).ToList(),
            JsonValue jsonValue => UnwrapScalar(jsonValue),
            _ => node.ToJsonString(),
        };
    }

    private static object? UnwrapScalar(JsonValue value)
    {
        if (value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        if (value.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        return value.ToJsonString().Trim('"');
    }
}

public sealed class VariablesApi(IEnumerable<VariableDefinition> seedVariables)
{
    #region Private Fields

    private readonly Dictionary<string, VariableDefinition> variables = BuildVariableMap(seedVariables);

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

    #region Private Methods

    private static Dictionary<string, VariableDefinition> BuildVariableMap(IEnumerable<VariableDefinition> seedVariables)
    {
        var variables = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in seedVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                continue;
            }

            variables[variable.Key] = variable;
        }

        return variables;
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

    public required dynamic response { get; init; }

    public required VariablesApi variables { get; init; }

    public required TestsApi tests { get; init; }

    public required ConsoleApi console { get; init; }

    public required TimeApi time { get; init; }

    public required JsonApi json { get; init; }

    public required RandomApi random { get; init; }

    public required WorkspaceApi workspace { get; init; }
}
