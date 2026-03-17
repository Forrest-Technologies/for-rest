using ForRest.Models;

namespace ForRest.Domain;

public sealed class VariableResolver
{
    #region Private Fields

    private static readonly VariableScope[] Precedence =
    [
        VariableScope.System,
        VariableScope.Global,
        VariableScope.Workspace,
        VariableScope.Environment,
        VariableScope.RequestLocal,
        VariableScope.Runtime,
    ];

    private static readonly Regex TokenPattern = new(@"\{\{(?<key>[\w\.\-]+)\}\}", RegexOptions.Compiled);

    #endregion

    #region Public Methods

    public VariableResolutionPreview Preview(
        string template,
        IEnumerable<VariableDefinition> systemVariables,
        IEnumerable<VariableDefinition> globalVariables,
        IEnumerable<VariableDefinition> workspaceVariables,
        IEnumerable<VariableDefinition> environmentVariables,
        IEnumerable<VariableDefinition> requestVariables,
        IEnumerable<VariableDefinition> runtimeVariables)
    {
        var resolvedVariables = Resolve(systemVariables, globalVariables, workspaceVariables, environmentVariables, requestVariables, runtimeVariables);

        return new()
        {
            Variables = resolvedVariables,
            RenderedText = RenderTemplate(template, resolvedVariables),
        };
    }

    public List<ResolvedVariable> Resolve(
        IEnumerable<VariableDefinition> systemVariables,
        IEnumerable<VariableDefinition> globalVariables,
        IEnumerable<VariableDefinition> workspaceVariables,
        IEnumerable<VariableDefinition> environmentVariables,
        IEnumerable<VariableDefinition> requestVariables,
        IEnumerable<VariableDefinition> runtimeVariables)
    {
        Dictionary<VariableScope, IEnumerable<VariableDefinition>> variablesByScope = new()
        {
            [VariableScope.System] = systemVariables,
            [VariableScope.Global] = globalVariables,
            [VariableScope.Workspace] = workspaceVariables,
            [VariableScope.Environment] = environmentVariables,
            [VariableScope.RequestLocal] = requestVariables,
            [VariableScope.Runtime] = runtimeVariables,
        };

        Dictionary<string, List<VariableSource>> chains = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, bool> secretFlags = new(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in Precedence)
        {
            foreach (var variable in variablesByScope[scope].Where(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key)))
            {
                if (!chains.TryGetValue(variable.Key, out var chain))
                {
                    chain = [];
                    chains[variable.Key] = chain;
                }

                chain.Add(new()
                {
                    Scope = scope,
                    Value = variable.Value,
                });

                secretFlags[variable.Key] = secretFlags.TryGetValue(variable.Key, out var secretFlag)
                    ? secretFlag || variable.IsSecret
                    : variable.IsSecret;
            }
        }

        return chains
            .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(
                static item =>
                {
                    var effective = item.Value.Last();
                    return new ResolvedVariable
                    {
                        Key = item.Key,
                        Value = effective.Value,
                        EffectiveScope = effective.Scope,
                        OverrideChain = [.. item.Value],
                    };
                })
            .Select(
                item => item with
                {
                    IsSecret = secretFlags[item.Key],
                })
            .ToList();
    }

    public string RenderTemplate(string template, IEnumerable<ResolvedVariable> variables)
    {
        var lookup = variables.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase);

        return TokenPattern.Replace(
            template ?? string.Empty,
            match =>
            {
                var key = match.Groups["key"].Value;
                return lookup.TryGetValue(key, out var value) ? value : match.Value;
            });
    }

    #endregion
}
