namespace ForRest.Models;

public sealed record KeyValueDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;

    public string Description { get; init; } = string.Empty;
}

public sealed record VariableDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<VariableScope>))]
    public VariableScope Scope { get; init; }

    public bool IsSecret { get; init; }

    public bool IsEnabled { get; init; } = true;

    public string Description { get; init; } = string.Empty;
}

public sealed record RequestAuthDefinition
{
    [JsonConverter(typeof(JsonStringEnumConverter<AuthMode>))]
    public AuthMode Mode { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string BearerToken { get; init; } = string.Empty;

    public string ApiKeyName { get; init; } = string.Empty;

    public string ApiKeyValue { get; init; } = string.Empty;

    public string HeaderName { get; init; } = string.Empty;

    public string HeaderValue { get; init; } = string.Empty;

    public string QueryParameterName { get; init; } = string.Empty;

    public string Scheme { get; init; } = string.Empty;

    public bool UseDefaultCredentials { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Authority { get; init; } = string.Empty;

    public string TokenUrl { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string Scopes { get; init; } = string.Empty;

    public string Resource { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<ApiKeyLocation>))]
    public ApiKeyLocation ApiKeyLocation { get; init; }
}

public sealed record RequestBodyDefinition
{
    [JsonConverter(typeof(JsonStringEnumConverter<RequestBodyMode>))]
    public RequestBodyMode Mode { get; init; }

    public string RawContent { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public List<KeyValueDefinition> FormValues { get; init; } = [];
}

public sealed record ExtractionDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<ExtractionSource>))]
    public ExtractionSource Source { get; init; } = ExtractionSource.Json;

    public string Selector { get; init; } = string.Empty;

    public string Pattern { get; init; } = string.Empty;

    public int Group { get; init; } = 1;

    public string TargetVariableName { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<VariableScope>))]
    public VariableScope TargetScope { get; init; } = VariableScope.Runtime;

    public bool IsEnabled { get; init; } = true;
}

public enum ExtractionSource
{
    Body,
    Header,
    Json,
}

public sealed record ScheduleDefinition
{
    public int RepeatCount { get; init; } = 1;

    public int DelayMilliseconds { get; init; }

    public int IntervalMilliseconds { get; init; }
}

public sealed record RetryDefinition
{
    public int Count { get; init; }

    public int IntervalMilliseconds { get; init; }
}

public sealed record RequestDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkspaceId { get; init; }

    public string Name { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<HttpMethodKind>))]
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Get;

    public string UrlTemplate { get; init; } = string.Empty;

    public List<KeyValueDefinition> QueryParameters { get; init; } = [];

    public List<KeyValueDefinition> Headers { get; init; } = [];

    public RequestAuthDefinition Auth { get; init; } = new();

    public RequestBodyDefinition Body { get; init; } = new();

    public List<VariableDefinition> Variables { get; init; } = [];

    public List<ExtractionDefinition> Extractions { get; init; } = [];

    public string PreRequestScript { get; init; } = string.Empty;

    public string TestsScript { get; init; } = string.Empty;

    public ScheduleDefinition Schedule { get; init; } = new();

    public RetryDefinition Retry { get; init; } = new();

    public int TimeoutMilliseconds { get; init; } = 30_000;

    public bool FollowRedirects { get; init; } = true;

    public bool ValidateSsl { get; init; } = true;

    public bool SaveResponseToHistory { get; init; } = true;

    public int MaxSendIterations { get; init; } = 3;

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
