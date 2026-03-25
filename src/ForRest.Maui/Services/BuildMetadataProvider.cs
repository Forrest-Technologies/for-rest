using System.Globalization;
using System.Reflection;

namespace ForRest.Maui.Services;

public interface IBuildMetadataProvider
{
	DateTimeOffset GetBuildDateUtc();
}

public sealed class BuildMetadataProvider : IBuildMetadataProvider
{
	private const string BuildDateMetadataKey = "ForRestBuildDateUtc";
	private readonly Assembly _assembly;

	public BuildMetadataProvider()
		: this(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
	{
	}

	internal BuildMetadataProvider(Assembly assembly)
	{
		_assembly = assembly;
	}

	public DateTimeOffset GetBuildDateUtc()
	{
		string? metadataValue = _assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(static attribute => string.Equals(attribute.Key, BuildDateMetadataKey, StringComparison.Ordinal))
			?.Value;

		if (DateTimeOffset.TryParse(
			    metadataValue,
			    CultureInfo.InvariantCulture,
			    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			    out DateTimeOffset buildDate))
		{
			return buildDate;
		}

		try
		{
			string assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{_assembly.GetName().Name}.dll");
			return File.Exists(assemblyPath)
				? File.GetLastWriteTimeUtc(assemblyPath)
				: DateTimeOffset.UtcNow;
		}
		catch
		{
			return DateTimeOffset.UtcNow;
		}
	}
}
