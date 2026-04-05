using System.IO;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class RoslynRuntimeDirectoryBootstrapperSourceTests
{
	[TestMethod]
	public void RoslynRuntimeDirectoryBootstrapper_seeds_android_references_from_fastdev_override_directory()
	{
		string sourcePath = Path.GetFullPath(
			Path.Combine(
				AppContext.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"..",
				"src",
				"ForRest.Maui",
				"Services",
				"RoslynRuntimeDirectoryBootstrapper.cs"));
		string source = File.ReadAllText(sourcePath);

		StringAssert.Contains(source, "FastDevOverrideDirectoryName = \".__override__\"");
		StringAssert.Contains(source, "TryExtractAssembliesFromPackagedAssets(stagingDirectory)");
		StringAssert.Contains(source, "TryExtractAssembliesFromInstalledPackages(stagingDirectory)");
		StringAssert.Contains(source, "TryExtractAssembliesFromFastDevOverrideDirectory(stagingDirectory)");
		StringAssert.Contains(source, "Path.Combine(FileSystem.AppDataDirectory, FastDevOverrideDirectoryName)");
		StringAssert.Contains(source, "Directory.EnumerateFiles(overrideRoot, \"*.dll\", SearchOption.AllDirectories)");
	}

	[TestMethod]
	public void RoslynRuntimeDirectoryBootstrapper_supports_brotli_compressed_packaged_runtime_assets()
	{
		string sourcePath = Path.GetFullPath(
			Path.Combine(
				AppContext.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"..",
				"src",
				"ForRest.Maui",
				"Services",
				"RoslynRuntimeDirectoryBootstrapper.cs"));
		string source = File.ReadAllText(sourcePath);

		StringAssert.Contains(source, "BrotliCompressedAssemblySuffix = \".dll.br\"");
		StringAssert.Contains(source, "name.EndsWith(BrotliCompressedAssemblySuffix, StringComparison.OrdinalIgnoreCase)");
		StringAssert.Contains(source, "assetName[..^3]");
		StringAssert.Contains(source, "new BrotliStream(input, CompressionMode.Decompress, leaveOpen: false)");
	}
}
