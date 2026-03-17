namespace ForRest.Scripting;

public interface IScriptEngine
{
    Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default);
}
