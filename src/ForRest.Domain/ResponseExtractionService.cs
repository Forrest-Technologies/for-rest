using ForRest.Models;

namespace ForRest.Domain;

public sealed class ResponseExtractionService
{
    #region Private Fields

    private readonly JsonNodeSelector jsonNodeSelector = new();

    #endregion

    #region Public Methods

    public List<VariableDefinition> Extract(ResponseSnapshot? response, IEnumerable<ExtractionDefinition> extractions)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Body))
        {
            return [];
        }

        JsonNode? rootNode;

        try
        {
            rootNode = JsonNode.Parse(response.Body);
        }
        catch (JsonException)
        {
            return [];
        }

        return
        [
            .. extractions
                .Where(static extraction => extraction.IsEnabled && !string.IsNullOrWhiteSpace(extraction.TargetVariableName))
                .Select(extraction => BuildVariable(rootNode, extraction))
                .Where(static item => item is not null)
                .Select(static item => item!),
        ];
    }

    #endregion

    #region Private Methods

    private VariableDefinition? BuildVariable(JsonNode? rootNode, ExtractionDefinition extraction)
    {
        var value = jsonNodeSelector.Select(rootNode, extraction.Selector);
        if (value is null)
        {
            return null;
        }

        return new()
        {
            Key = extraction.TargetVariableName,
            Value = value,
            Scope = extraction.TargetScope,
        };
    }

    #endregion
}
