namespace ForRest.Scripting;

public interface IScriptEngine
{
    ScriptValidationResult Validate(string script);

    Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default);
}
