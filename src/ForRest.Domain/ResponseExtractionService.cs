using System.Text.RegularExpressions;
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
        if (response is null)
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
            rootNode = null;
        }

        return
        [
            .. extractions
                .Where(static extraction => extraction.IsEnabled && !string.IsNullOrWhiteSpace(extraction.TargetVariableName))
                .Select(extraction => BuildVariable(response, rootNode, extraction))
                .Where(static item => item is not null)
                .Select(static item => item!),
        ];
    }

    private VariableDefinition? BuildVariable(ResponseSnapshot response, JsonNode? rootNode, ExtractionDefinition extraction)
    {
        var value = extraction.Source switch
        {
            ExtractionSource.Body => ApplyRegex(response.Body, extraction.Pattern, extraction.Group),
            ExtractionSource.Header => ApplyRegex(FindHeaderValue(response, extraction.Selector), extraction.Pattern, extraction.Group),
            ExtractionSource.Json when string.IsNullOrWhiteSpace(extraction.Pattern) => jsonNodeSelector.Select(rootNode, extraction.Selector),
            ExtractionSource.Json => ApplyRegex(jsonNodeSelector.Select(rootNode, extraction.Selector), extraction.Pattern, extraction.Group),
            _ => null,
        };

        if (value is null)
        {
            return null;
        }

        return new()
        {
            Key = extraction.TargetVariableName,
            Value = value,
            Scope = extraction.TargetScope,
            IsSecret = extraction.IsSecret,
        };
    }

    private static string? FindHeaderValue(ResponseSnapshot response, string headerName)
    {
        foreach (var header in response.Headers)
        {
            if (string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return null;
    }

    private static string? ApplyRegex(string? input, string pattern, int group)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        try
        {
            var match = Regex.Match(input, pattern, RegexOptions.CultureInvariant);
            if (!match.Success || group < 0 || group >= match.Groups.Count)
            {
                return null;
            }

            return match.Groups[group].Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    #endregion
}
