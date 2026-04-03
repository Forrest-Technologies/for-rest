using System.Collections.Generic;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class RoslynScriptEngineReferencePathTests
{
    [TestMethod]
    public void BuildReferenceProbeDirectories_includes_fastdev_override_subdirectories()
    {
        string appBaseDirectory = Path.Combine(Path.GetTempPath(), $"forrest-probes-{Guid.NewGuid():N}");
        string overrideAbiDirectory = Path.Combine(appBaseDirectory, ".__override__", "arm64-v8a");
        Directory.CreateDirectory(overrideAbiDirectory);

        try
        {
            IReadOnlyList<string> directories = RoslynScriptEngine.BuildReferenceProbeDirectories(
                appBaseDirectory,
                roslynRuntimeDirectory: null,
                runtimeDirectory: null,
                assemblyLocation: "/System.Private.CoreLib");

            CollectionAssert.Contains(directories.ToArray(), Path.Combine(appBaseDirectory, ".__override__"));
            CollectionAssert.Contains(directories.ToArray(), overrideAbiDirectory);
        }
        finally
        {
            Directory.Delete(appBaseDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveReferenceFilePath_finds_assembly_inside_fastdev_override_abi_directory()
    {
        string appBaseDirectory = Path.Combine(Path.GetTempPath(), $"forrest-resolve-{Guid.NewGuid():N}");
        string overrideAbiDirectory = Path.Combine(appBaseDirectory, ".__override__", "arm64-v8a");
        Directory.CreateDirectory(overrideAbiDirectory);
        string candidatePath = Path.Combine(overrideAbiDirectory, "System.Private.CoreLib.dll");
        File.WriteAllText(candidatePath, string.Empty);

        try
        {
            string? resolvedPath = RoslynScriptEngine.ResolveReferenceFilePath(
                "System.Private.CoreLib",
                appBaseDirectory,
                roslynRuntimeDirectory: null,
                runtimeDirectory: null,
                assemblyLocation: "/System.Private.CoreLib");

            Assert.AreEqual(candidatePath, resolvedPath);
        }
        finally
        {
            Directory.Delete(appBaseDirectory, recursive: true);
        }
    }
}
