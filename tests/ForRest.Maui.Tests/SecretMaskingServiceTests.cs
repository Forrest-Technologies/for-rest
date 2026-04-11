using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class SecretMaskingServiceTests
{
	[TestMethod]
	public void MaskSecrets_replaces_string_secret_value_with_fixed_marker()
	{
		string source = """
			secret api_key = "super-secret-token"
			method GET
			""";

		string masked = SecretMaskingService.MaskSecrets(source);

		StringAssert.Contains(masked, "secret api_key = \"***\"");
		Assert.IsFalse(masked.Contains("super-secret-token", StringComparison.Ordinal));
		StringAssert.Contains(masked, "method GET");
	}

	[TestMethod]
	public void MaskSecrets_handles_function_call_and_identifier_value_forms()
	{
		string source = """
			secret token = lookup_token("prod")
			secret fallback = otherVar
			""";

		string masked = SecretMaskingService.MaskSecrets(source);

		StringAssert.Contains(masked, "secret token = \"***\"");
		StringAssert.Contains(masked, "secret fallback = \"***\"");
		Assert.IsFalse(masked.Contains("lookup_token", StringComparison.Ordinal));
		Assert.IsFalse(masked.Contains("otherVar", StringComparison.Ordinal));
	}

	[TestMethod]
	public void MaskSecrets_preserves_indentation_and_keeps_non_secret_lines_intact()
	{
		string source = "  secret api_key = \"value\"\nmethod GET\nurl \"https://api.example.test/\"";

		string masked = SecretMaskingService.MaskSecrets(source);

		StringAssert.Contains(masked, "  secret api_key = \"***\"");
		StringAssert.Contains(masked, "url \"https://api.example.test/\"");
	}

	[TestMethod]
	public void MaskSecrets_returns_input_unchanged_when_no_secrets_present()
	{
		string source = "method GET\nurl \"https://api.example.test/\"";

		string masked = SecretMaskingService.MaskSecrets(source);

		Assert.AreEqual(source, masked);
	}

	[TestMethod]
	public void MaskSecrets_returns_empty_for_null_or_empty_input()
	{
		Assert.AreEqual(string.Empty, SecretMaskingService.MaskSecrets(null));
		Assert.AreEqual(string.Empty, SecretMaskingService.MaskSecrets(string.Empty));
	}

	[TestMethod]
	public void ContainsSecrets_detects_any_declaration()
	{
		Assert.IsTrue(SecretMaskingService.ContainsSecrets("secret api = \"x\""));
		Assert.IsFalse(SecretMaskingService.ContainsSecrets("method GET"));
		Assert.IsFalse(SecretMaskingService.ContainsSecrets(null));
	}
}
