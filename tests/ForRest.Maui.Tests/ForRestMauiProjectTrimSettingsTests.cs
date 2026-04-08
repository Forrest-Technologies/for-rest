using System.IO;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ForRestMauiProjectTrimSettingsTests
{
	[TestMethod]
	public void Android_release_uses_partial_trimming_with_scripting_preservation_guards()
	{
		string project = ReadProjectFile();

		StringAssert.Contains(project, "<AndroidPackageFormats Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android' and '$(AndroidPackageFormats)' == ''\">apk</AndroidPackageFormats>");
		StringAssert.Contains(project, "<EmbedAssembliesIntoApk Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android' and '$(EmbedAssembliesIntoApk)' == ''\">true</EmbedAssembliesIntoApk>");
		StringAssert.Contains(project, "<PublishTrimmed Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android' and '$(Configuration)' == 'Release' and '$(PublishTrimmed)' == ''\">true</PublishTrimmed>");
		StringAssert.Contains(project, "<AndroidLinkMode Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android' and '$(Configuration)' == 'Release' and '$(AndroidLinkMode)' == ''\">SdkOnly</AndroidLinkMode>");
		StringAssert.Contains(project, "<TrimMode Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android' and '$(Configuration)' == 'Release' and '$(TrimMode)' == ''\">partial</TrimMode>");
		StringAssert.Contains(project, "<Target Name=\"ValidateAndroidScriptingBuildSettings\"");
		StringAssert.Contains(project, "PublishAot/NativeAOT builds are not supported");
		StringAssert.Contains(project, "only support SdkOnly/partial trimming");
		StringAssert.Contains(project, "device deploys and Roslyn metadata staging both work reliably");
	}

	[TestMethod]
	public void Android_project_preserves_the_roslyn_and_script_runtime_surface_when_trimming()
	{
		string project = ReadProjectFile();

		StringAssert.Contains(project, "<TrimmerRootAssembly Include=\"ForRest.Scripting\" RootMode=\"All\" />");
		StringAssert.Contains(project, "<TrimmerRootAssembly Include=\"Microsoft.CodeAnalysis.CSharp.Scripting\" RootMode=\"All\" />");
		StringAssert.Contains(project, "<TrimmerRootAssembly Include=\"System.Linq\" RootMode=\"All\" />");
		StringAssert.Contains(project, "<TrimmerRootAssembly Include=\"System.Text.Json\" RootMode=\"All\" />");
		StringAssert.Contains(project, "<TrimmerRootAssembly Include=\"System.Text.RegularExpressions\" RootMode=\"All\" />");
		StringAssert.Contains(project, "<LinkDescription Include=\"Linker\\ForRest.Android.Scripting.linker.xml\" />");
		StringAssert.Contains(project, "<MauiAsset Include=\"roslyn-runtime\\*.dll\" Exclude=\"roslyn-runtime\\ForRest.*.dll\" LogicalName=\"roslyn-runtime/%(Filename)%(Extension)\" />");
		StringAssert.Contains(project, "<MauiAsset Include=\"..\\ForRest.Domain\\bin\\$(Configuration)\\net10.0\\ForRest.Domain.dll\" LogicalName=\"roslyn-runtime/ForRest.Domain.dll\" />");
		StringAssert.Contains(project, "<MauiAsset Include=\"..\\ForRest.Scripting\\bin\\$(Configuration)\\net10.0\\ForRest.Scripting.dll\" LogicalName=\"roslyn-runtime/ForRest.Scripting.dll\" />");
		StringAssert.Contains(project, "<MauiAsset Include=\"..\\ForRest.Services\\bin\\$(Configuration)\\net10.0\\ForRest.Services.dll\" LogicalName=\"roslyn-runtime/ForRest.Services.dll\" />");
		StringAssert.Contains(project, "<MauiAsset Include=\"$(TargetDir)ForRest.Maui.dll\" LogicalName=\"roslyn-runtime/ForRest.Maui.dll\" />");
	}

	[TestMethod]
	public void Android_scripting_linker_descriptor_preserves_common_core_types()
	{
		string descriptorPath = Path.GetFullPath(
			Path.Combine(
				AppContext.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"..",
				"src",
				"ForRest.Maui",
				"Linker",
				"ForRest.Android.Scripting.linker.xml"));
		string descriptor = File.ReadAllText(descriptorPath);

		StringAssert.Contains(descriptor, "<assembly fullname=\"System.Private.CoreLib\">");
		StringAssert.Contains(descriptor, "<type fullname=\"System.String\" preserve=\"all\" />");
		StringAssert.Contains(descriptor, "<type fullname=\"System.Guid\" preserve=\"all\" />");
		StringAssert.Contains(descriptor, "<type fullname=\"System.Text.StringBuilder\" preserve=\"all\" />");
		StringAssert.Contains(descriptor, "<assembly fullname=\"System.Private.Uri\">");
		StringAssert.Contains(descriptor, "<type fullname=\"System.Uri\" preserve=\"all\" />");
	}

	private static string ReadProjectFile()
	{
		string projectPath = Path.GetFullPath(
			Path.Combine(
				AppContext.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"..",
				"src",
				"ForRest.Maui",
				"ForRest.Maui.csproj"));
		return File.ReadAllText(projectPath);
	}
}
