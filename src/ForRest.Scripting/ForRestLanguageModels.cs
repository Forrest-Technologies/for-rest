using System.Globalization;

namespace ForRest.Scripting;

public enum ForRestScriptDiagnosticSeverity
{
    Error,
    Warning,
}

public enum ForRestScriptVariableScope
{
    Request,
    Runtime,
    Secret,
}

public enum ForRestScriptValueKind
{
    String,
    Number,
    Boolean,
    Identifier,
    FunctionCall,
}

public enum ForRestScriptAssertionTarget
{
    Status,
    Body,
    Header,
    Json,
}

public enum ForRestScriptComparisonOperator
{
    Equal,
    NotEqual,
    Contains,
    StartsWith,
    EndsWith,
    Exists,
    NotExists,
    RegexMatch,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

public enum ForRestScriptExtractionSource
{
    Body,
    Header,
    Json,
}

public enum ForRestRuntimeSeedKind
{
    Literal,
    Guid,
    Now,
    UtcNow,
    RandomNumber,
}

public enum ForRestScriptHandlerKind
{
    OnError,
    OnStatus,
}

public sealed record ForRestScriptHandler(
    ForRestScriptHandlerKind Kind,
    int? StatusCode,
    string Body);

public sealed record ForRestScriptScenario(
    string Name,
    Dictionary<string, ForRestScriptValueExpression> Auth,
    List<ForRestScriptNamedValue> Headers,
    List<ForRestScriptAssertion> Tests,
    string Flow);

public sealed record ForRestScriptDiagnostic(
    ForRestScriptDiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column);

public sealed record ForRestScriptParseResult(
    ForRestScriptDocument? Document,
    IReadOnlyList<ForRestScriptDiagnostic> Diagnostics)
{
    public bool Succeeded => Document is not null && Diagnostics.All(static diagnostic => diagnostic.Severity != ForRestScriptDiagnosticSeverity.Error);
}

public sealed record ForRestScriptCompilationOptions
{
    public Guid WorkspaceId { get; init; }

    public string DefaultRequestName { get; init; } = "Untitled Request";

    public Func<string, string?>? ResolveImport { get; init; }
}

public sealed record ForRestScriptCompilationResult(
    ForRestScriptDocument? Document,
    ForRestExecutionPayload? Payload,
    IReadOnlyList<ForRestScriptDiagnostic> Diagnostics)
{
    public bool Succeeded => Payload is not null && Diagnostics.All(static diagnostic => diagnostic.Severity != ForRestScriptDiagnosticSeverity.Error);

    public List<ForRestExecutionPayload> ScenarioPayloads { get; init; } = [];
}

public sealed record ForRestExecutionPayload
{
    public string SourceText { get; init; } = string.Empty;

    public RequestDefinition Request { get; init; } = new();

    public List<ForRestRuntimeVariableSeed> RuntimeSeeds { get; init; } = [];

    public string? ScenarioName { get; init; }
}

public sealed record ForRestRuntimeVariableSeed(
    string Key,
    ForRestRuntimeSeedKind Kind,
    string LiteralValue,
    int? MinimumInclusive = null,
    int? MaximumExclusive = null,
    bool IsSecret = false);

public sealed record ForRestScriptDocument
{
    public Dictionary<string, ForRestScriptValueExpression> Meta { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ForRestScriptVariableDeclaration> Variables { get; init; } = [];

    public Dictionary<string, ForRestScriptValueExpression> Request { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ForRestScriptValueExpression> Auth { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ForRestScriptNamedValue> QueryParameters { get; init; } = [];

    public List<ForRestScriptNamedValue> Headers { get; init; } = [];

    public ForRestScriptBodySection? Body { get; init; }

    public List<ForRestScriptNamedValue> FormValues { get; init; } = [];

    public List<ForRestScriptNamedValue> MultipartValues { get; init; } = [];

    public List<ForRestScriptExtraction> Extractions { get; init; } = [];

    public string Flow { get; init; } = string.Empty;

    public List<ForRestScriptAssertion> Tests { get; init; } = [];

    public Dictionary<string, ForRestScriptValueExpression> Repeat { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ForRestScriptValueExpression> Retry { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ForRestScriptHandler> Handlers { get; init; } = [];

    public List<ForRestScriptScenario> Scenarios { get; init; } = [];

    public List<string> Imports { get; init; } = [];
}

public abstract record ForRestScriptValueExpression(ForRestScriptValueKind Kind)
{
    public virtual string AsInvariantString()
    {
        return Kind switch
        {
            ForRestScriptValueKind.String => ((ForRestScriptStringExpression)this).Value,
            ForRestScriptValueKind.Number => ((ForRestScriptNumberExpression)this).Value.ToString(CultureInfo.InvariantCulture),
            ForRestScriptValueKind.Boolean => ((ForRestScriptBooleanExpression)this).Value ? "true" : "false",
            ForRestScriptValueKind.Identifier => ((ForRestScriptIdentifierExpression)this).Value,
            _ => throw new InvalidOperationException($"Cannot convert {Kind} directly to an invariant string."),
        };
    }
}

public sealed record ForRestScriptStringExpression(string Value) : ForRestScriptValueExpression(ForRestScriptValueKind.String);

public sealed record ForRestScriptNumberExpression(int Value) : ForRestScriptValueExpression(ForRestScriptValueKind.Number);

public sealed record ForRestScriptBooleanExpression(bool Value) : ForRestScriptValueExpression(ForRestScriptValueKind.Boolean);

public sealed record ForRestScriptIdentifierExpression(string Value) : ForRestScriptValueExpression(ForRestScriptValueKind.Identifier);

public sealed record ForRestScriptFunctionCallExpression(
    string Name,
    IReadOnlyList<ForRestScriptValueExpression> Arguments) : ForRestScriptValueExpression(ForRestScriptValueKind.FunctionCall);

public sealed record ForRestScriptVariableDeclaration(
    ForRestScriptVariableScope Scope,
    string Key,
    ForRestScriptValueExpression Expression);

public sealed record ForRestScriptNamedValue(
    string Key,
    ForRestScriptValueExpression Value);

public sealed record ForRestScriptBodySection(
    RequestBodyMode Mode,
    string Content);

public sealed record ForRestScriptExtraction(
    VariableScope TargetScope,
    string TargetVariableName,
    ForRestScriptExtractionSource Source,
    string Selector,
    string Pattern = "",
    int Group = 1);

public sealed record ForRestScriptAssertion(
    ForRestScriptAssertionTarget Target,
    ForRestScriptComparisonOperator Operator,
    string Message,
    string? HeaderName = null,
    string? Selector = null,
    ForRestScriptValueExpression? Value = null,
    bool IsDescriptionOnly = false);
