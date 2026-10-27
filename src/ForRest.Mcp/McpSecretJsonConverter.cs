namespace ForRest.Mcp;

internal sealed class McpSecretJsonConverter(IReadOnlyCollection<string> secretValues) : JsonConverter<string>
{
    #region Private Fields

    private readonly Func<string, string> scrub = McpSecretValueScrubber.CreateScrubber(secretValues);

    #endregion

    #region Public Methods

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Secret JSON conversion is used only when writing MCP output.");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(scrub(value));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WritePropertyName(scrub(value));

    #endregion
}
