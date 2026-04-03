namespace ForRest.Scripting;

using System.Collections.Immutable;
using Microsoft.CSharp.RuntimeBinder;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

public sealed class RoslynScriptEngine(ILogger<RoslynScriptEngine> logger) : IScriptEngine
{
    #region Private Fields

    private const string RoslynRuntimeDirectoryDataKey = "ForRest.RoslynRuntimeDirectory";

    private static readonly Lazy<ScriptRuntimeConfiguration> ScriptRuntime = new(
        CreateScriptRuntimeConfiguration,
        LazyThreadSafetyMode.ExecutionAndPublication);

    #endregion

    #region Public Methods

    public ScriptValidationResult Validate(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return new();
        }

        try
        {
            Script<object> compiledScript = CreateScript(script);
            ImmutableArray<Diagnostic> diagnostics = compiledScript.Compile();
            string message = string.Join(
                Environment.NewLine,
                diagnostics
                    .Where(static item => item.Severity == DiagnosticSeverity.Error)
                    .Select(static item => item.ToString()));
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
                workspace = new WorkspaceApi(
                    request.Workspace,
                    variablesApi,
                    responseApi,
                    testsApi,
                    consoleApi,
                    stashApi,
                    request.ExecuteWorkspaceRequestAsync),
                stash = stashApi,
            };

            Script<object> script = CreateScript(request.Script);
            await script.RunAsync(globals, cancellationToken: cancellationToken);
        }
        catch (CompilationErrorException exception)
        {
            var message = string.Join(Environment.NewLine, exception.Diagnostics.Select(static item => item.ToString()));
            logger.LogWarning("Script compilation failed: {Message}", message);
            consoleApi.Error(message);
            return BuildResult(request, requestApi, responseApi, variablesApi, testsApi, consoleApi, stashApi, message);
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

    private static Script<object> CreateScript(string script)
    {
        ScriptRuntimeConfiguration scriptRuntime = ScriptRuntime.Value;
        return CSharpScript.Create(
            script,
            scriptRuntime.Options,
            typeof(ScriptGlobals),
            CreateAssemblyLoader(scriptRuntime.ReferenceAssemblies));
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
        var references = assemblies
            .Select(CreateMetadataReference)
            .ToArray();

        var options = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
           .WithReferences(references)
           .AddImports(
               "System",
               "System.Linq",
               "System.Collections.Generic",
               "System.Text",
               "System.Text.Json.Nodes",
               "System.Text.RegularExpressions",
               "ForRest.Scripting",
               "ForRest.Models");

        return new(options, assemblies);
    }

    private static IReadOnlyList<Assembly> GetReferenceAssemblies()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();

        foreach (var assembly in GetReferenceAssemblyRoots())
        {
            Enqueue(assembly);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(ShouldIncludeAssembly))
        {
            Enqueue(assembly);
        }

        foreach (var referenceName in GetDefaultReferenceNames())
        {
            if (TryResolveAssembly(referenceName, out var assembly))
            {
                Enqueue(assembly);
            }
        }

        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!ShouldIncludeAssemblyName(reference.Name))
                {
                    continue;
                }

                if (TryResolveAssembly(reference, out var resolvedAssembly))
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

            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName) || assemblies.ContainsKey(assemblyName))
            {
                return;
            }

            assemblies[assemblyName] = assembly;
            pending.Enqueue(assembly);
        }
    }

    private static InteractiveAssemblyLoader CreateAssemblyLoader(IReadOnlyList<Assembly> assemblies)
    {
        var loader = new InteractiveAssemblyLoader();
        foreach (var assembly in assemblies)
        {
            loader.RegisterDependency(assembly);
            if (TryGetAssemblyFilePath(assembly) is { } assemblyPath)
            {
                loader.RegisterDependency(AssemblyIdentity.FromAssemblyDefinition(assembly), assemblyPath);
            }
        }

        return loader;
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
        AddDirectory(string.IsNullOrWhiteSpace(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation));

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
        if (!string.IsNullOrWhiteSpace(location) && Path.IsPathRooted(location) && File.Exists(location))
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
    ];

    private static IEnumerable<string> GetDefaultReferenceNames() =>
        Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default.MetadataReferences
            .Select(GetReferenceName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)!;

    private static string? GetReferenceName(MetadataReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Display))
        {
            return null;
        }

        const string unresolvedPrefix = "Unresolved: ";
        return reference.Display.StartsWith(unresolvedPrefix, StringComparison.OrdinalIgnoreCase)
            ? reference.Display[unresolvedPrefix.Length..].Trim()
            : Path.GetFileNameWithoutExtension(reference.Display);
    }

    private static bool TryResolveAssembly(string simpleName, out Assembly assembly)
    {
        var assemblyName = new AssemblyName(simpleName);
        return TryResolveAssembly(assemblyName, out assembly);
    }

    private static bool TryResolveAssembly(AssemblyName assemblyName, out Assembly assembly)
    {
        var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
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

        var filePath = GetAssemblyLocation(assembly);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            filePath = null;
        }

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

    private sealed record ScriptRuntimeConfiguration(
        ScriptOptions Options,
        IReadOnlyList<Assembly> ReferenceAssemblies);

    #endregion
}
