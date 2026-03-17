using ForRest.Models;

namespace ForRest.Domain;

public sealed class ResponseExtractionService
{
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

    private static VariableDefinition? BuildVariable(JsonNode? rootNode, ExtractionDefinition extraction)
    {
        var value = TrySelect(rootNode, extraction.Selector);
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

    private static string? TrySelect(JsonNode? rootNode, string selector)
    {
        if (rootNode is null || string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var normalizedSelector = selector.Trim();
        if (normalizedSelector.StartsWith("$.", StringComparison.Ordinal))
        {
            normalizedSelector = normalizedSelector[2..];
        }

        if (normalizedSelector == "$")
        {
            return rootNode.ToJsonString();
        }

        JsonNode? currentNode = rootNode;
        foreach (var segment in Tokenize(normalizedSelector))
        {
            currentNode = segment switch
            {
                { IsArrayIndex: true } when currentNode is JsonArray array && segment.ArrayIndex < array.Count => array[segment.ArrayIndex],
                { IsArrayIndex: false } when currentNode is JsonObject jsonObject && jsonObject.TryGetPropertyValue(segment.PropertyName, out var nextNode) => nextNode,
                _ => null,
            };

            if (currentNode is null)
            {
                return null;
            }
        }

        return currentNode switch
        {
            JsonValue value => value.ToJsonString().Trim('"'),
            _ => currentNode.ToJsonString(),
        };
    }

    private static List<PathToken> Tokenize(string selector)
    {
        var tokens = new List<PathToken>();
        foreach (var segment in selector.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segmentRemainder = segment;
            while (!string.IsNullOrWhiteSpace(segmentRemainder))
            {
                var bracketIndex = segmentRemainder.IndexOf('[');
                if (bracketIndex < 0)
                {
                    tokens.Add(PathToken.ForProperty(segmentRemainder));
                    break;
                }

                if (bracketIndex > 0)
                {
                    tokens.Add(PathToken.ForProperty(segmentRemainder[..bracketIndex]));
                }

                var closeIndex = segmentRemainder.IndexOf(']', bracketIndex);
                if (closeIndex < 0)
                {
                    break;
                }

                if (int.TryParse(segmentRemainder[(bracketIndex + 1)..closeIndex], out var arrayIndex))
                {
                    tokens.Add(PathToken.ForArrayIndex(arrayIndex));
                }

                segmentRemainder = closeIndex + 1 < segmentRemainder.Length ? segmentRemainder[(closeIndex + 1)..] : string.Empty;
            }
        }

        return tokens;
    }

    #endregion

    private sealed record PathToken
    {
        public bool IsArrayIndex { get; init; }

        public int ArrayIndex { get; init; }

        public string PropertyName { get; init; } = string.Empty;

        public static PathToken ForArrayIndex(int arrayIndex)
        {
            return new()
            {
                IsArrayIndex = true,
                ArrayIndex = arrayIndex,
            };
        }

        public static PathToken ForProperty(string propertyName)
        {
            return new()
            {
                PropertyName = propertyName,
            };
        }
    }
}
