using System.Collections.Generic;
using System.Linq;

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

    [TestMethod]
    public void IsUsableAssemblyFilePath_rejects_android_style_non_file_locations()
    {
        Assert.IsFalse(RoslynScriptEngine.IsUsableAssemblyFilePath("System.Private.CoreLib.dll"));
        Assert.IsFalse(RoslynScriptEngine.IsUsableAssemblyFilePath("/System.Private.CoreLib.dll"));
    }

    [TestMethod]
    public void BuildReferenceProbeDirectories_ignores_invalid_rooted_assembly_locations()
    {
        string appBaseDirectory = Path.Combine(Path.GetTempPath(), $"forrest-invalid-location-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appBaseDirectory);
        string invalidAssemblyLocation = $"{Path.DirectorySeparatorChar}System.Private.CoreLib.dll";
        string? invalidDirectory = Path.GetDirectoryName(invalidAssemblyLocation);

        try
        {
            IReadOnlyList<string> directories = RoslynScriptEngine.BuildReferenceProbeDirectories(
                appBaseDirectory,
                roslynRuntimeDirectory: null,
                runtimeDirectory: null,
                assemblyLocation: invalidAssemblyLocation);

            Assert.IsFalse(
                directories.Any(directory => string.Equals(directory, invalidDirectory, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(appBaseDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void BuildReferenceProbeDirectories_includes_existing_assembly_directory()
    {
        string appBaseDirectory = Path.Combine(Path.GetTempPath(), $"forrest-existing-location-{Guid.NewGuid():N}");
        string assemblyDirectory = Path.Combine(appBaseDirectory, "assemblies");
        Directory.CreateDirectory(assemblyDirectory);
        string assemblyPath = Path.Combine(assemblyDirectory, "System.Private.CoreLib.dll");
        File.WriteAllText(assemblyPath, string.Empty);

        try
        {
            IReadOnlyList<string> directories = RoslynScriptEngine.BuildReferenceProbeDirectories(
                appBaseDirectory,
                roslynRuntimeDirectory: null,
                runtimeDirectory: null,
                assemblyLocation: assemblyPath);

            CollectionAssert.Contains(directories.ToArray(), assemblyDirectory);
        }
        finally
        {
            Directory.Delete(appBaseDirectory, recursive: true);
        }
    }

}
