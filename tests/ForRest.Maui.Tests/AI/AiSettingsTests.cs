using ForRest.Services.AI;

namespace ForRest.Maui.Tests.AI;

public sealed class AiSettingsTests
{
    [TestMethod]
    public void Default_settings_are_disabled_and_hide_secret_editor()
    {
        AiSettings settings = new();

        Assert.IsFalse(settings.Enabled);
        Assert.IsFalse(settings.ShouldShowSettingsSection);
        Assert.IsFalse(settings.ShouldShowSecretEditor);
        Assert.AreEqual(string.Empty, settings.ApiKey.DisplayValue);
        Assert.IsTrue(settings.Conversation.StreamResponses);
        Assert.AreEqual(2, settings.Conversation.MaxClarificationTurns);

        AiSettingsEditorProjection projection = settings.ToEditorProjection();

        Assert.IsFalse(projection.Visible);
        Assert.AreEqual(string.Empty, projection.ApiKeyEditorValue);
    }

    [TestMethod]
    public void Secret_setting_masks_editor_value_when_plaintext_exists()
    {
        AiSecretSetting secret = new()
        {
            Value = "super-secret",
        };

        Assert.IsTrue(secret.HasUsableValue);
        Assert.AreEqual(AiSecretSetting.MaskedValue, secret.DisplayValue);
    }

    [TestMethod]
    public void Enabled_openai_settings_require_model_and_api_key()
    {
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Model = string.Empty,
            },
            ApiKey = new AiSecretSetting
            {
                IsConfigured = false,
            },
        };

        IReadOnlyList<AiSettingsIssue> issues = new AiSettingsValidator().Validate(settings);

        CollectionAssert.Contains(issues.ToArray(), new AiSettingsIssue(AiSettingsIssueSeverity.Error, "ai.openai.model.required", "OpenAI model is required when AI is enabled."));
        CollectionAssert.Contains(issues.ToArray(), new AiSettingsIssue(AiSettingsIssueSeverity.Error, "ai.api-key.required", "An API key must be configured when AI is enabled."));
    }

    [TestMethod]
    public void Enabled_azure_settings_require_endpoint_and_deployment()
    {
        AiSettings settings = new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.AzureOpenAI,
                Endpoint = "not-a-url",
                DeploymentName = string.Empty,
            },
            ApiKey = new AiSecretSetting
            {
                Value = "secret",
                IsConfigured = true,
            },
        };

        IReadOnlyList<AiSettingsIssue> issues = new AiSettingsValidator().Validate(settings);
        string[] codes = [.. issues.Select(static issue => issue.Code)];

        CollectionAssert.Contains(codes, "ai.azure.endpoint.invalid");
        CollectionAssert.Contains(codes, "ai.azure.deployment.required");
        Assert.IsFalse(codes.Contains("ai.api-key.required"));
    }
}
