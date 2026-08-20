namespace ForRest.Scripting;

using System.Collections.Immutable;
using System.IO;
using Microsoft.CSharp.RuntimeBinder;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

public sealed class RoslynScriptEngine(ILogger<RoslynScriptEngine> logger) : IScriptEngine
{
    #region Private Fields

    private const string RoslynRuntimeDirectoryDataKey = "ForRest.RoslynRuntimeDirectory";
    private const string ScriptPreamble =
        """
        var request = global::ForRest.Scripting.ScriptRuntimeContext.Globals.request;
        dynamic response = global::ForRest.Scripting.ScriptRuntimeContext.Globals.response;
        var variables = global::ForRest.Scripting.ScriptRuntimeContext.Globals.variables;
        var tests = global::ForRest.Scripting.ScriptRuntimeContext.Globals.tests;
        var console = global::ForRest.Scripting.ScriptRuntimeContext.Globals.console;
        var time = global::ForRest.Scripting.ScriptRuntimeContext.Globals.time;
        var strings = global::ForRest.Scripting.ScriptRuntimeContext.Globals.strings;
        var convert = global::ForRest.Scripting.ScriptRuntimeContext.Globals.convert;
        var json = global::ForRest.Scripting.ScriptRuntimeContext.Globals.json;
        var encoding = global::ForRest.Scripting.ScriptRuntimeContext.Globals.encoding;
        var crypto = global::ForRest.Scripting.ScriptRuntimeContext.Globals.crypto;
        var regex = global::ForRest.Scripting.ScriptRuntimeContext.Globals.regex;
        var random = global::ForRest.Scripting.ScriptRuntimeContext.Globals.random;
        var payloads = global::ForRest.Scripting.ScriptRuntimeContext.Globals.payloads;
        var fuzz = global::ForRest.Scripting.ScriptRuntimeContext.Globals.fuzz;
        var workspace = global::ForRest.Scripting.ScriptRuntimeContext.Globals.workspace;
        dynamic stash = global::ForRest.Scripting.ScriptRuntimeContext.Globals.stash;
        var snapshot = global::ForRest.Scripting.ScriptRuntimeContext.Globals.snapshot;
        var browser = global::ForRest.Scripting.ScriptRuntimeContext.Globals.browser;

        """;
    private static readonly string[] DefaultImports =
    [
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.Text",
        "System.Text.Json.Nodes",
        "System.Text.RegularExpressions",
        "System.Threading.Tasks",
        "ForRest.Scripting",
        "ForRest.Models",
    ];

    private static readonly Lazy<ScriptRuntimeConfiguration> ScriptRuntime = new(
        CreateScriptRuntimeConfiguration,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly object cacheLock = new();
    private readonly Dictionary<string, CachedScript> compilationCache = new(StringComparer.Ordinal);
    private long cacheClock;
    private int compilationCount;

    #endregion

    #region Public Methods

    public ScriptValidationResult Validate(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return new();
        }

        if (TryGetCachedScript(script) is not null)
        {
            return new();
        }

        try
        {
            ScriptCompilationResult compilation = CompileScript(script);
            ImmutableArray<Diagnostic> diagnostics = compilation.Compilation.GetDiagnostics();
            string message = BuildFriendlyCompileErrorMessage(diagnostics);
            if (string.IsNullOrWhiteSpace(message))
            {
                return new();
            }

            logger.LogWarning("Script validation failed: {Message}", message);
            return new()
            {
                ErrorMessage = message,
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Script validation failed");
            string message = ShouldReportReferenceDiagnostics(exception)
                ? BuildFriendlyRuntimeErrorMessage(exception) + Environment.NewLine + BuildReferenceDiagnostics()
                : BuildFriendlyRuntimeErrorMessage(exception);
            return new()
            {
                ErrorMessage = message,
            };
        }
    }

    public async Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Script))
        {
            return new()
            {
                PreparedRequest = request.PreparedRequest,
                RuntimeVariables = request.RuntimeVariables,
            };
        }

        var testsApi = new TestsApi();
        var consoleApi = new ConsoleApi();
        var stashApi = new StashApi();
        ScriptRequestApi? requestApi = null;
        ScriptResponseApi? responseApi = null;
        VariablesApi? variablesApi = null;
        ScriptGlobals? globals = null;

        try
        {
            responseApi = new ScriptResponseApi(request.Response);
            variablesApi = new VariablesApi(
            [
                .. request.GlobalVariables,
                .. request.WorkspaceVariables,
                .. request.EnvironmentVariables,
                .. request.RequestVariables,
                .. request.RuntimeVariables,
            ]);
            requestApi = new ScriptRequestApi(
                request.PreparedRequest,
                responseApi,
                variablesApi,
                request.SendAsync,
                request.MaxSendIterations);
            globals = new ScriptGlobals
            {
                request = requestApi,
                response = responseApi,
                variables = variablesApi,
                tests = testsApi,
                console = consoleApi,
                time = new TimeApi(),
                strings = new StringsApi(),
                convert = new ConvertApi(),
                json = new JsonApi(),
                encoding = new EncodingApi(),
                crypto = new CryptoApi(),
                regex = new RegexApi(),
                random = new RandomApi(),
                payloads = new PayloadsApi(),
                fuzz = new FuzzApi(consoleApi, requestApi),
                workspace = new WorkspaceApi(
                    request.Workspace,
                    variablesApi,
                    responseApi,
                    testsApi,
                    consoleApi,
                    stashApi,
                    request.ExecuteWorkspaceRequestAsync),
                stash = stashApi,
                snapshot = new SnapshotApi(),
                browser = new ScriptBrowserApi(request.BrowserBridge ?? NullBrowserBridge.Instance),
            };

            var cached = TryGetCachedScript(request.Script);
            if (cached is null)
            {
                ScriptCompilationResult compilation = CompileScript(request.Script);
                ImmutableArray<Diagnostic> diagnostics = compilation.Compilation.GetDiagnostics(cancellationToken)
                    .Where(static item => item.Severity == DiagnosticSeverity.Error)
                    .ToImmutableArray();
                if (!diagnostics.IsEmpty)
                {
                    string message = BuildFriendlyCompileErrorMessage(diagnostics);
                    logger.LogWarning("Script compilation failed: {Message}", message);
                    consoleApi.Error(message);
                    return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, stashApi, message);
                }

                cached = CacheOrReuseLoadedScript(request.Script, EmitAndLoadScript(compilation, cancellationToken));
            }

            using IDisposable scope = ScriptRuntimeContext.Enter(globals);
            await InvokeCompiledScript(cached, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Script execution failed");
            if (ShouldReportReferenceDiagnostics(exception))
            {
                consoleApi.Error(BuildReferenceDiagnostics());
            }

            string message = BuildFriendlyRuntimeErrorMessage(exception);
            consoleApi.Error(message);
            return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, stashApi, message);
        }

        return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, stashApi, string.Empty);
    }

    #endregion

    #region Internal Cache Instrumentation

    internal int CacheCapacity { get; set; } = 32;

    internal int CompilationCount => Volatile.Read(ref compilationCount);

    internal int CacheSize
    {
        get
        {
            lock (cacheLock)
            {
                return compilationCache.Count;
            }
        }
    }

    internal void ClearCache()
    {
        List<CachedScript> evicted;
        lock (cacheLock)
        {
            evicted = [.. compilationCache.Values];
            compilationCache.Clear();
        }

        foreach (var entry in evicted)
        {
            entry.LoadContext.Unload();
        }
    }

    #endregion

    #region Private Methods

    private static ScriptExecutionResult BuildResult(
        ScriptExecutionRequest originalRequest,
        ScriptRequestApi? requestApi,
        ScriptResponseApi? responseApi,
        VariablesApi? variablesApi,
        TestsApi testsApi,
        ConsoleApi consoleApi,
        StashApi stashApi,
        string errorMessage)
    {
        if (requestApi is null || variablesApi is null)
        {
            return new()
            {
                PreparedRequest = originalRequest.PreparedRequest,
                Response = originalRequest.Response,
                SentResponse = requestApi?.LastSentResponse,
                SentResponses = requestApi?.SentResponses is null ? [] : [.. requestApi.SentResponses],
                SentRequests = requestApi?.SentRequests is null ? [] : [.. requestApi.SentRequests],
                SendCount = requestApi?.SendCount ?? 0,
                RuntimeVariables =
                [
                    .. originalRequest.RuntimeVariables.Where(static item => item.Scope == VariableScope.Runtime),
                ],
                Tests =
                [
                    .. testsApi.All(),
                ],
                ConsoleEntries =
                [
                    .. consoleApi.All(),
                ],
                ErrorMessage = errorMessage,
                Stash = stashApi.BuildTable(),
            };
        }

        return new()
        {
            PreparedRequest = BuildPreparedRequestOrFallback(originalRequest.PreparedRequest, requestApi),
            Response = responseApi?.Snapshot ?? originalRequest.Response,
            SentResponse = requestApi.LastSentResponse,
            SentResponses = [.. requestApi.SentResponses],
            SentRequests = [.. requestApi.SentRequests],
            SendCount = requestApi.SendCount,
            RuntimeVariables =
            [
                .. variablesApi.All().Where(static item => item.Scope == VariableScope.Runtime),
            ],
            Tests =
            [
                .. testsApi.All(),
            ],
            ConsoleEntries =
            [
                .. consoleApi.All(),
            ],
            ErrorMessage = errorMessage,
            Stash = stashApi.BuildTable(),
            };
    }

    private ScriptCompilationResult CompileScript(string script)
    {
        Interlocked.Increment(ref compilationCount);
        ScriptRuntimeConfiguration scriptRuntime = ScriptRuntime.Value;
        string scriptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        string typeName = $"GeneratedScript_{scriptHash}";
        string assemblyName = $"ForRest.Script.{scriptHash}";
        string preparedScript = BuildScriptSource(typeName, script);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            preparedScript,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: "script.cs");
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            scriptRuntime.References,
            new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release)
                .WithNullableContextOptions(NullableContextOptions.Enable)
                .WithUsings(DefaultImports));
        return new(compilation, typeName);
    }

    private static string BuildScriptSource(string typeName, string script)
    {
        string imports = string.Join(
            Environment.NewLine,
            DefaultImports.Select(static item => $"using global::{item};"));
        return
            $$"""
            {{imports}}
            using StringComparison = global::System.StringComparison;

            namespace ForRest.Scripting.Generated;

            internal static class {{typeName}}
            {
                public static async global::System.Threading.Tasks.Task<object?> RunAsync()
                {
            {{IndentScriptBody(ScriptPreamble)}}    #line 1
            {{IndentScriptBody(script)}}
                    return null;
                }
            }
            """;
    }

    private static string IndentScriptBody(string script)
    {
        if (string.IsNullOrEmpty(script))
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        using StringReader reader = new(script);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            builder.Append("        ");
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static CachedScript EmitAndLoadScript(ScriptCompilationResult compilation, CancellationToken cancellationToken)
    {
        using MemoryStream assemblyStream = new();
        EmitResult emitResult = compilation.Compilation.Emit(assemblyStream, cancellationToken: cancellationToken);
        if (!emitResult.Success)
        {
            string message = BuildFriendlyCompileErrorMessage(emitResult.Diagnostics);
            throw new InvalidOperationException(message);
        }

        assemblyStream.Position = 0;

        ScriptAssemblyLoadContext loadContext = new();
        try
        {
            Assembly assembly = loadContext.LoadFromStream(assemblyStream);
            Type scriptType = assembly.GetType($"ForRest.Scripting.Generated.{compilation.TypeName}", throwOnError: true)!;
            MethodInfo method = scriptType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(scriptType.FullName, "RunAsync");
            return new(loadContext, method);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    private static async Task InvokeCompiledScript(CachedScript cached, CancellationToken cancellationToken)
    {
        try
        {
            var executionTask = (Task)(cached.RunMethod.Invoke(null, null)
                ?? throw new InvalidOperationException("Compiled script did not return a task."));
            await executionTask.WaitAsync(cancellationToken);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private CachedScript? TryGetCachedScript(string script)
    {
        lock (cacheLock)
        {
            if (compilationCache.TryGetValue(script, out var cached))
            {
                cached.LastUsed = ++cacheClock;
                return cached;
            }

            return null;
        }
    }

    private CachedScript CacheOrReuseLoadedScript(string script, CachedScript loaded)
    {
        List<CachedScript> evicted = [];
        CachedScript result;
        lock (cacheLock)
        {
            if (compilationCache.TryGetValue(script, out var existing))
            {
                existing.LastUsed = ++cacheClock;
                evicted.Add(loaded);
                result = existing;
            }
            else
            {
                loaded.LastUsed = ++cacheClock;
                compilationCache[script] = loaded;
                while (compilationCache.Count > CacheCapacity)
                {
                    var oldest = compilationCache.MinBy(static item => item.Value.LastUsed);
                    compilationCache.Remove(oldest.Key);
                    evicted.Add(oldest.Value);
                }

                result = loaded;
            }
        }

        foreach (var entry in evicted)
        {
            entry.LoadContext.Unload();
        }

        return result;
    }

    private static string BuildFriendlyCompileErrorMessage(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(
            Environment.NewLine,
            diagnostics
                .Where(static item => item.Severity == DiagnosticSeverity.Error)
                .Select(static item => BuildFriendlyDiagnosticLine(item)));
    }

    private static string BuildFriendlyDiagnosticLine(Diagnostic diagnostic)
    {
        var mappedSpan = diagnostic.Location.GetMappedLineSpan();
        var line = mappedSpan.IsValid ? mappedSpan.StartLinePosition.Line + 1 : 0;
        var friendly = BuildFriendlyDiagnosticText(diagnostic, line);
        var prefix = line > 0 ? $"Script error (line {line}): " : "Script error: ";
        return $"{prefix}{friendly} | details: {diagnostic}";
    }

    private static string BuildFriendlyDiagnosticText(Diagnostic diagnostic, int line)
    {
        var message = diagnostic.GetMessage();
        switch (diagnostic.Id)
        {
            case "CS0103":
            {
                var match = Regex.Match(message, "The name '([^']+)' does not exist");
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    return IsGeneratedIdentifier(name)
                        ? $"Internal script translation error — please report this script. ({message})"
                        : $"Unknown name '{name}'. Declare it with 'let {name} = ...' or check the spelling.";
                }

                break;
            }

            case "CS1061":
            {
                var match = Regex.Match(message, "'([^']+)' does not contain a definition for '([^']+)'");
                if (match.Success)
                {
                    var typeName = match.Groups[1].Value;
                    var memberName = match.Groups[2].Value;
                    return IsGeneratedIdentifier(typeName)
                        ? $"'{memberName}' is not available here. Check the member name."
                        : $"'{memberName}' is not available on '{typeName}'. Check the member name.";
                }

                break;
            }

            case "CS1002":
            case "CS1513":
            case "CS1026":
            {
                return line > 0
                    ? $"Incomplete statement near line {line} — check for a missing closing brace, parenthesis, or unfinished expression."
                    : "Incomplete statement — check for a missing closing brace, parenthesis, or unfinished expression.";
            }
        }

        return $"error {diagnostic.Id}: {StripGeneratedReferences(message)}";
    }

    private static bool IsGeneratedIdentifier(string name)
    {
        return name.StartsWith("__", StringComparison.Ordinal) ||
               name.Contains("GeneratedScript_", StringComparison.Ordinal) ||
               name.StartsWith("ForRest.Scripting.Generated", StringComparison.Ordinal) ||
               name.StartsWith("dynamic", StringComparison.Ordinal);
    }

    private static string StripGeneratedReferences(string message)
    {
        return Regex.Replace(message, @"(ForRest\.Scripting\.Generated\.)?GeneratedScript_[0-9A-Fa-f]+", "script");
    }

    private static string BuildFriendlyRuntimeErrorMessage(Exception exception)
    {
        if (exception is RuntimeBinderException runtimeBinderException
            && string.Equals(runtimeBinderException.Message, "Cannot perform runtime binding on a null reference", StringComparison.Ordinal))
        {
            return string.Join(
                " ",
                [
                    runtimeBinderException.Message + ".",
                    "A nested JSON value in the script resolved to null before the next member access.",
                    "Guard the parent value first, for example `if item.data != null { ... item.data.price ... }`.",
                    "For optional text fields, prefer `convert.ToString(...)` and the `strings` helpers."
                ]);
        }

        return exception.Message;
    }

    private static PreparedRequest BuildPreparedRequestOrFallback(PreparedRequest fallback, ScriptRequestApi requestApi)
    {
        try
        {
            return requestApi.ToPreparedRequest();
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private static ScriptRuntimeConfiguration CreateScriptRuntimeConfiguration()
    {
        var assemblies = GetReferenceAssemblies();
        IReadOnlyList<MetadataReference> references = BuildCompilationReferences(assemblies);

        return new(references, assemblies);
    }

    private static IReadOnlyList<MetadataReference> BuildCompilationReferences(IReadOnlyList<Assembly> assemblies)
    {
        List<MetadataReference> references = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> requiredAssemblyNames = assemblies
            .Select(static assembly => assembly.GetName().Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string referencePath in EnumerateCompilationReferenceFilePaths(assemblies, requiredAssemblyNames))
        {
            if (!seenPaths.Add(referencePath))
            {
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(referencePath));
            }
            catch (BadImageFormatException)
            {
                // Skip non-managed payloads if one slips into the runtime directory.
            }
            catch (FileNotFoundException)
            {
                // Skip disappearing files; curated assembly fallbacks below still apply.
            }
        }

        foreach (Assembly assembly in assemblies)
        {
            string? assemblyPath = TryGetAssemblyFilePath(assembly);
            if (assemblyPath is not null && seenPaths.Contains(assemblyPath))
            {
                continue;
            }

            if (TryCreateMetadataReferenceFromRawMetadata(assembly, out MetadataReference metadataReference))
            {
                references.Add(metadataReference);
            }
        }

        return references;
    }

    private static IEnumerable<string> EnumerateCompilationReferenceFilePaths(
        IReadOnlyList<Assembly> assemblies,
        ISet<string> requiredAssemblyNames)
    {
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (string path in trustedPlatformAssemblies.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string assemblyName = Path.GetFileNameWithoutExtension(path);
                if (requiredAssemblyNames.Contains(assemblyName) && File.Exists(path))
                {
                    yield return path;
                }
            }
        }

        string? roslynRuntimeDirectory = GetRoslynRuntimeDirectory();
        if (!string.IsNullOrWhiteSpace(roslynRuntimeDirectory) && Directory.Exists(roslynRuntimeDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(roslynRuntimeDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                string assemblyName = Path.GetFileNameWithoutExtension(path);
                if (requiredAssemblyNames.Contains(assemblyName))
                {
                    yield return path;
                }
            }
        }

        foreach (Assembly assembly in assemblies)
        {
            if (TryGetAssemblyFilePath(assembly) is { } assemblyPath)
            {
                yield return assemblyPath;
            }
        }
    }

    private static IReadOnlyList<Assembly> GetReferenceAssemblies()
    {
        Dictionary<string, Assembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
        Queue<Assembly> pending = new();

        foreach (Assembly assembly in GetReferenceAssemblyRoots())
        {
            Enqueue(assembly);
        }

        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (!ShouldIncludeAssemblyName(reference.Name))
                {
                    continue;
                }

                if (TryResolveAssembly(reference, out Assembly resolvedAssembly))
                {
                    Enqueue(resolvedAssembly);
                }
            }
        }

        return [.. assemblies.Values];

        void Enqueue(Assembly assembly)
        {
            if (!ShouldIncludeAssembly(assembly))
            {
                return;
            }

            string? assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName) || assemblies.ContainsKey(assemblyName))
            {
                return;
            }

            assemblies[assemblyName] = assembly;
            pending.Enqueue(assembly);
        }
    }

    private static string? GetRoslynRuntimeDirectory()
    {
        var runtimeDirectory = AppContext.GetData(RoslynRuntimeDirectoryDataKey) as string;
        return string.IsNullOrWhiteSpace(runtimeDirectory) ? null : runtimeDirectory;
    }

    private static string? TryGetRuntimeDirectory()
    {
        try
        {
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            return string.IsNullOrWhiteSpace(runtimeDirectory) ? null : runtimeDirectory;
        }
        catch
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> BuildReferenceProbeDirectories(
        string? appBaseDirectory,
        string? roslynRuntimeDirectory,
        string? runtimeDirectory,
        string? assemblyLocation)
    {
        var directories = new List<string>();

        AddDirectory(roslynRuntimeDirectory);
        AddDirectory(appBaseDirectory);

        foreach (var overrideDirectory in EnumerateFastDevOverrideDirectories(appBaseDirectory))
        {
            AddDirectory(overrideDirectory);
        }

        AddDirectory(runtimeDirectory);
        AddDirectory(IsUsableAssemblyFilePath(assemblyLocation) ? Path.GetDirectoryName(assemblyLocation) : null);

        return directories;

        void AddDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            directories.Add(directory);
        }
    }

    internal static string? ResolveReferenceFilePath(
        string? assemblyName,
        string? appBaseDirectory,
        string? roslynRuntimeDirectory,
        string? runtimeDirectory,
        string? assemblyLocation)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        foreach (var directory in BuildReferenceProbeDirectories(
                     appBaseDirectory,
                     roslynRuntimeDirectory,
                     runtimeDirectory,
                     assemblyLocation))
        {
            var candidatePath = Path.Combine(directory, $"{assemblyName}.dll");
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFastDevOverrideDirectories(string? appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            yield break;
        }

        var overrideRoot = Path.Combine(appBaseDirectory, ".__override__");
        yield return overrideRoot;

        if (!Directory.Exists(overrideRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(overrideRoot, "*", SearchOption.AllDirectories))
        {
            yield return directory;
        }
    }

    private static string? TryGetAssemblyFilePath(Assembly assembly)
    {
        var location = GetAssemblyLocation(assembly);
        if (IsUsableAssemblyFilePath(location))
        {
            return location;
        }

        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        return ResolveReferenceFilePath(
            assemblyName,
            AppContext.BaseDirectory,
            GetRoslynRuntimeDirectory(),
            TryGetRuntimeDirectory(),
            location);
    }

    internal static bool IsUsableAssemblyFilePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Path.IsPathRooted(path) &&
               File.Exists(path);
    }

    private static IEnumerable<Assembly> GetReferenceAssemblyRoots() =>
    [
        typeof(object).Assembly,
        typeof(Enumerable).Assembly,
        typeof(JsonNode).Assembly,
        typeof(Regex).Assembly,
        typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,
        typeof(ForRestFlowRuntime).Assembly,
        typeof(PreparedRequest).Assembly,
        typeof(VariableDefinition).Assembly,
        typeof(ScriptRuntimeContext).Assembly,
    ];

    private static bool TryResolveAssembly(AssemblyName assemblyName, out Assembly assembly)
    {
        Assembly? loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (loadedAssembly is not null)
        {
            assembly = loadedAssembly;
            return true;
        }

        try
        {
            assembly = Assembly.Load(assemblyName);
            return true;
        }
        catch
        {
            assembly = null!;
            return false;
        }
    }

    private static bool ShouldIncludeAssembly(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            return false;
        }

        return ShouldIncludeAssemblyName(assembly.GetName().Name);
    }

    private static bool ShouldIncludeAssemblyName(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        return assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
               assemblyName.StartsWith("ForRest.", StringComparison.Ordinal) ||
               string.Equals(assemblyName, "System", StringComparison.Ordinal) ||
               string.Equals(assemblyName, "System.Private.CoreLib", StringComparison.Ordinal) ||
               string.Equals(assemblyName, "Microsoft.CSharp", StringComparison.Ordinal) ||
               string.Equals(assemblyName, "netstandard", StringComparison.Ordinal) ||
               string.Equals(assemblyName, "mscorlib", StringComparison.Ordinal);
    }

    private static MetadataReference CreateMetadataReference(Assembly assembly)
    {
        if (TryCreateMetadataReferenceFromFilePath(assembly, out var fileReference))
        {
            return fileReference;
        }

        if (TryResolveAppBaseReference(assembly, out var appBaseReference))
        {
            return appBaseReference;
        }

        if (TryResolveTrustedPlatformReference(assembly, out var trustedPlatformReference))
        {
            return trustedPlatformReference;
        }

        if (TryCreateMetadataReferenceFromRawMetadata(assembly, out var metadataReference))
        {
            return metadataReference;
        }

        var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unknown>";
        throw new FileNotFoundException($"Unable to create a Roslyn metadata reference for '{assemblyName}'.");
    }

    private static bool TryCreateMetadataReferenceFromFilePath(Assembly assembly, out MetadataReference reference)
    {
        var location = TryGetAssemblyFilePath(assembly);
        if (!string.IsNullOrWhiteSpace(location))
        {
            reference = MetadataReference.CreateFromFile(location);
            return true;
        }

        reference = null!;
        return false;
    }

    private static string? GetAssemblyLocation(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;
            return string.IsNullOrWhiteSpace(location) ? null : location;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryResolveAppBaseReference(Assembly assembly, out MetadataReference reference)
    {
        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            reference = null!;
            return false;
        }

        string? candidatePath = ResolveReferenceFilePath(
            assemblyName,
            AppContext.BaseDirectory,
            GetRoslynRuntimeDirectory(),
            TryGetRuntimeDirectory(),
            GetAssemblyLocation(assembly));
        if (!string.IsNullOrWhiteSpace(candidatePath))
        {
            reference = MetadataReference.CreateFromFile(candidatePath);
            return true;
        }

        reference = null!;
        return false;
    }

    private static bool TryResolveTrustedPlatformReference(Assembly assembly, out MetadataReference reference)
    {
        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            reference = null!;
            return false;
        }

        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            reference = null!;
            return false;
        }

        var matchingPath = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                assemblyName,
                StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(matchingPath) || !File.Exists(matchingPath))
        {
            reference = null!;
            return false;
        }

        reference = MetadataReference.CreateFromFile(matchingPath);
        return true;
    }

    private static unsafe bool TryCreateMetadataReferenceFromRawMetadata(Assembly assembly, out MetadataReference reference)
    {
        if (!System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out var metadataBlob, out var metadataLength) ||
            metadataBlob == null ||
            metadataLength <= 0)
        {
            reference = null!;
            return false;
        }

        var filePath = TryGetAssemblyFilePath(assembly);
        var moduleMetadata = ModuleMetadata.CreateFromMetadata((nint)metadataBlob, metadataLength);
        var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
        reference = assemblyMetadata.GetReference(
            documentation: null,
            aliases: ImmutableArray<string>.Empty,
            embedInteropTypes: false,
            filePath: filePath,
            display: filePath);
        return true;
    }

    private static bool ShouldReportReferenceDiagnostics(Exception exception)
    {
        return exception is FileNotFoundException &&
               (exception.Message.Contains("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase) ||
                (exception as FileNotFoundException)?.FileName?.Contains("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string BuildReferenceDiagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Roslyn reference diagnostics:");
        builder.AppendLine("  ScriptHostMode: ambient-context");

        string? runtimeDirectory = TryGetRuntimeDirectory();
        string runtimeDirectoryDisplay = runtimeDirectory ?? "<unavailable>";

        builder.AppendLine($"  AppContext.BaseDirectory: {AppContext.BaseDirectory}");
        builder.AppendLine($"  RuntimeDirectory: {runtimeDirectoryDisplay}");
        string? roslynRuntimeDirectory = GetRoslynRuntimeDirectory();
        builder.AppendLine($"  RoslynRuntimeDirectory: {roslynRuntimeDirectory ?? "<unset>"}");

        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        builder.AppendLine($"  TrustedPlatformAssemblies available: {!string.IsNullOrWhiteSpace(trustedPlatformAssemblies)}");

        foreach (var assembly in GetReferenceAssemblies())
        {
            string name = assembly.GetName().Name ?? assembly.FullName ?? "<unknown>";
            string? location = GetAssemblyLocation(assembly);
            string? resolvedFilePath = TryGetAssemblyFilePath(assembly);

            builder.AppendLine($"  {name}:");
            builder.AppendLine($"    Assembly.Location: {location ?? "<null>"}");
            builder.AppendLine($"    Location exists: {!string.IsNullOrWhiteSpace(location) && File.Exists(location)}");
            builder.AppendLine($"    Resolved reference path: {resolvedFilePath ?? "<none>"}");

            foreach (string directory in BuildReferenceProbeDirectories(
                         AppContext.BaseDirectory,
                         roslynRuntimeDirectory,
                         runtimeDirectory,
                         location))
            {
                string candidatePath = Path.Combine(directory, $"{name}.dll");
                builder.AppendLine($"    Probe candidate: {candidatePath} (exists: {File.Exists(candidatePath)})");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private sealed record ScriptCompilationResult(
        CSharpCompilation Compilation,
        string TypeName);

    private sealed class CachedScript(ScriptAssemblyLoadContext loadContext, MethodInfo runMethod)
    {
        public ScriptAssemblyLoadContext LoadContext { get; } = loadContext;

        public MethodInfo RunMethod { get; } = runMethod;

        public long LastUsed { get; set; }
    }

    private sealed record ScriptRuntimeConfiguration(
        IReadOnlyList<MetadataReference> References,
        IReadOnlyList<Assembly> ReferenceAssemblies);

    private sealed class ScriptAssemblyLoadContext() : AssemblyLoadContext($"ForRestScript_{Guid.NewGuid():N}", isCollectible: true);

    #endregion
}
