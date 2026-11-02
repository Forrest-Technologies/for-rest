namespace ForRest.Services;

public static class ForRestScriptCompilationExtensions
{
    #region Public Methods

    public static ForRestScriptCompilationResult ValidateScripts(
        this ForRestScriptCompilationResult compilation,
        IScriptEngine scriptEngine)
    {
        if (!compilation.Succeeded || compilation.Payload is null)
        {
            return compilation;
        }

        List<ForRestScriptDiagnostic> diagnostics = [.. compilation.Diagnostics];
        var validatedScripts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in compilation.ScenarioPayloads.Prepend(compilation.Payload))
        {
            var context = payload.ScenarioName is null ? string.Empty : $"Scenario '{payload.ScenarioName}' ";
            Validate($"{context}Flow script", payload.Request.PreRequestScript);
            Validate($"{context}Tests script", payload.Request.TestsScript);
        }

        return compilation with { Diagnostics = diagnostics };

        void Validate(string context, string script)
        {
            if (string.IsNullOrWhiteSpace(script) || !validatedScripts.Add(script))
            {
                return;
            }

            var validation = scriptEngine.Validate(script);
            if (!validation.Succeeded)
            {
                diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error,
                    $"{context}: {validation.ErrorMessage}", 0, 0));
            }
        }
    }

    #endregion
}
