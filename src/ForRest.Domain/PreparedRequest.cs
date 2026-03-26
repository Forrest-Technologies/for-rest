using ForRest.Models;

namespace ForRest.Domain;

public sealed record PreparedRequest
{
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Get;

    public Uri Uri { get; init; } = new("https://localhost");

    public List<KeyValueDefinition> Headers { get; init; } = [];

    public RequestBodyDefinition Body { get; init; } = new();

    public RequestAuthDefinition Auth { get; init; } = new();

    public int TimeoutMilliseconds { get; init; }

    public bool FollowRedirects { get; init; } = true;

    public bool ValidateSsl { get; init; } = true;

    public string RawRequest { get; init; } = string.Empty;

    public VariableResolutionPreview Variables { get; init; } = new();
}
