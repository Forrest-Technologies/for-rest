namespace ForRest.Scripting;

public interface IForRestScriptCompiler
{
    ForRestScriptParseResult Parse(string source);

    ForRestScriptCompilationResult Compile(string source, ForRestScriptCompilationOptions options);
}
