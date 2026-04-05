using System.Reflection;
using System.IO.Compression;

namespace ForRest.Maui.Services;

public static class RoslynRuntimeDirectoryBootstrapper
{
	public const string AppContextDataKey = "ForRest.RoslynRuntimeDirectory";

#if ANDROID
	private const string AssetFolderName = "roslyn-runtime";
	private const string BrotliCompressedAssemblySuffix = ".dll.br";
	private const string StampFileName = ".stamp";
	private const string DotnetLibraryPrefix = "lib_";
	private const string DotnetLibrarySuffix = ".dll.so";
	private const string FastDevOverrideDirectoryName = ".__override__";
#endif

	public static void Initialize()
	{
#if ANDROID
		string runtimeDirectory = Path.Combine(FileSystem.AppDataDirectory, AssetFolderName);
		Directory.CreateDirectory(runtimeDirectory);

		string stamp = GetStampValue();
		string stampPath = Path.Combine(runtimeDirectory, StampFileName);
		if (!string.Equals(ReadStamp(stampPath), stamp, StringComparison.Ordinal))
		{
			RefreshAssets(runtimeDirectory);
			File.WriteAllText(stampPath, stamp);
		}

		AppContext.SetData(AppContextDataKey, runtimeDirectory);
#endif
	}

#if ANDROID
	private static string GetStampValue()
	{
		Assembly assembly = typeof(App).Assembly;
		return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(static attribute => string.Equals(attribute.Key, "ForRestBuildDateUtc", StringComparison.Ordinal))
			?.Value ?? assembly.GetName().Version?.ToString() ?? "unknown";
	}

	private static string ReadStamp(string stampPath)
	{
		try
		{
			return File.Exists(stampPath) ? File.ReadAllText(stampPath) : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static void RefreshAssets(string runtimeDirectory)
	{
		string stagingDirectory = Path.Combine(runtimeDirectory, ".staging");
		RecreateDirectory(stagingDirectory);

		bool extractedAny = false;
		extractedAny |= TryExtractAssembliesFromPackagedAssets(stagingDirectory);
		if (!extractedAny)
		{
			extractedAny |= TryExtractAssembliesFromInstalledPackages(stagingDirectory);
		}

		// If a fast-deploy override exists, let it replace packaged/reference copies for the local build.
		extractedAny |= TryExtractAssembliesFromFastDevOverrideDirectory(stagingDirectory);
		if (!extractedAny)
		{
			return;
		}

		foreach (string filePath in Directory.EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly))
		{
			File.Delete(filePath);
		}

		foreach (string stagedFilePath in Directory.EnumerateFiles(stagingDirectory, "*.dll", SearchOption.TopDirectoryOnly))
		{
			string destinationPath = Path.Combine(runtimeDirectory, Path.GetFileName(stagedFilePath));
			File.Copy(stagedFilePath, destinationPath, overwrite: true);
		}
	}

	private static bool TryExtractAssembliesFromFastDevOverrideDirectory(string runtimeDirectory)
	{
		string overrideRoot = Path.Combine(FileSystem.AppDataDirectory, FastDevOverrideDirectoryName);
		if (!Directory.Exists(overrideRoot))
		{
			return false;
		}

		bool extractedAny = false;
		foreach (string sourcePath in Directory.EnumerateFiles(overrideRoot, "*.dll", SearchOption.AllDirectories))
		{
			if (sourcePath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string destinationPath = Path.Combine(runtimeDirectory, Path.GetFileName(sourcePath));
			File.Copy(sourcePath, destinationPath, overwrite: true);
			extractedAny = true;
		}

		return extractedAny;
	}

	private static bool TryExtractAssembliesFromPackagedAssets(string runtimeDirectory)
	{
		Android.Content.Context context = Android.App.Application.Context
			?? throw new InvalidOperationException("Android application context is unavailable.");
		Android.Content.Res.AssetManager assetManager = context.Assets!;
		string[] assetNames = assetManager.List(AssetFolderName) ?? Array.Empty<string>();
		if (assetNames.Length == 0)
		{
			return false;
		}

		bool extractedAny = false;
		HashSet<string> extractedAssemblyNames = new(StringComparer.OrdinalIgnoreCase);
		foreach (string assetName in assetNames
			.OrderByDescending(static name => name.EndsWith(BrotliCompressedAssemblySuffix, StringComparison.OrdinalIgnoreCase)))
		{
			bool isCompressedAssembly = assetName.EndsWith(BrotliCompressedAssemblySuffix, StringComparison.OrdinalIgnoreCase);
			bool isRawAssembly = assetName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
			if (!isCompressedAssembly && !isRawAssembly)
			{
				continue;
			}

			string destinationFileName = isCompressedAssembly
				? assetName[..^3]
				: assetName;
			if (!extractedAssemblyNames.Add(destinationFileName))
			{
				continue;
			}

			string destinationPath = Path.Combine(runtimeDirectory, destinationFileName);
			using Stream input = assetManager.Open($"{AssetFolderName}/{assetName}");
			using Stream assemblyStream = isCompressedAssembly
				? new BrotliStream(input, CompressionMode.Decompress, leaveOpen: false)
				: input;
			using FileStream output = File.Create(destinationPath);
			assemblyStream.CopyTo(output);
			extractedAny = true;
		}

		return extractedAny;
	}

	private static void RecreateDirectory(string directoryPath)
	{
		if (Directory.Exists(directoryPath))
		{
			Directory.Delete(directoryPath, recursive: true);
		}

		Directory.CreateDirectory(directoryPath);
	}

	private static bool TryExtractAssembliesFromInstalledPackages(string runtimeDirectory)
	{
		Android.Content.Context context = Android.App.Application.Context
			?? throw new InvalidOperationException("Android application context is unavailable.");
		Android.Content.PM.ApplicationInfo applicationInfo = context.ApplicationInfo
			?? throw new InvalidOperationException("Android application info is unavailable.");

		string[] packagePaths =
		[
			.. GetExistingPackagePaths(applicationInfo),
		];
		if (packagePaths.Length == 0)
		{
			return false;
		}

		string[] abiNames =
		[
			.. Android.OS.Build.SupportedAbis
				.Where(static abi => !string.IsNullOrWhiteSpace(abi))
				.Distinct(StringComparer.OrdinalIgnoreCase),
		];
		if (abiNames.Length == 0)
		{
			return false;
		}

		var extractedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string abiName in abiNames)
		{
			bool extractedForAbi = false;
			foreach (string packagePath in packagePaths)
			{
				extractedForAbi |= ExtractAssembliesFromPackage(packagePath, abiName, runtimeDirectory, extractedAssemblies);
			}

			if (extractedForAbi)
			{
				break;
			}
		}

		return extractedAssemblies.Count > 0;
	}

	private static IEnumerable<string> GetExistingPackagePaths(Android.Content.PM.ApplicationInfo applicationInfo)
	{
		if (!string.IsNullOrWhiteSpace(applicationInfo.SourceDir) && File.Exists(applicationInfo.SourceDir))
		{
			yield return applicationInfo.SourceDir;
		}

		if (applicationInfo.SplitSourceDirs is null)
		{
			yield break;
		}

		foreach (string packagePath in applicationInfo.SplitSourceDirs)
		{
			if (!string.IsNullOrWhiteSpace(packagePath) && File.Exists(packagePath))
			{
				yield return packagePath;
			}
		}
	}

	private static bool ExtractAssembliesFromPackage(
		string packagePath,
		string abiName,
		string runtimeDirectory,
		HashSet<string> extractedAssemblies)
	{
		string packageEntryPrefix = $"lib/{abiName}/{DotnetLibraryPrefix}";
		bool extractedAny = false;

		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (!entry.FullName.StartsWith(packageEntryPrefix, StringComparison.OrdinalIgnoreCase) ||
				!entry.FullName.EndsWith(DotnetLibrarySuffix, StringComparison.OrdinalIgnoreCase) ||
				entry.FullName.EndsWith(".resources.dll.so", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string entryFileName = entry.Name;
			if (!entryFileName.StartsWith(DotnetLibraryPrefix, StringComparison.OrdinalIgnoreCase) ||
				!entryFileName.EndsWith(DotnetLibrarySuffix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string destinationFileName = entryFileName[DotnetLibraryPrefix.Length..^3];
			if (!extractedAssemblies.Add(destinationFileName))
			{
				continue;
			}

			string destinationPath = Path.Combine(runtimeDirectory, destinationFileName);
			using Stream input = entry.Open();
			using FileStream output = File.Create(destinationPath);
			input.CopyTo(output);
			extractedAny = true;
		}

		return extractedAny;
	}
#endif
}
