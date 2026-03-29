using ForRest.Domain;
using ForRest.Maui.Theming;
using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;
using ForRest.Repositories;
using ForRest.Services;
using ForRest.Services.AI;
using ForRest.Services.Licensing;
using ForRest.Scripting;
using Microsoft.Extensions.Logging;

namespace ForRest.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ThemeCatalog>();
		builder.Services.AddSingleton<SettingsTomlTemplate>();
		builder.Services.AddSingleton<ThemeConfigParser>();
		builder.Services.AddSingleton<ThemeConfigNormalizer>();
		builder.Services.AddSingleton<ThemeConfigStore>();
		builder.Services.AddSingleton<SettingsTomlDocumentService>();
		builder.Services.AddSingleton<IWorkbenchAiSettingsProvider, WorkbenchAiSettingsProvider>();
		builder.Services.AddSingleton<IThemeService, ThemeService>();
		builder.Services.AddSingleton<IBuildMetadataProvider, BuildMetadataProvider>();
		builder.Services.AddSingleton(
			new LicenseValidationOptions(
				ForRestLicenseProfile.PublicKey,
				GracePeriodDays: 30));
		builder.Services.AddSingleton<ILicenseValidationService, StandardLicenseValidationService>();
		builder.Services.AddSingleton<IAppActivationService, AppActivationService>();
		builder.Services.AddSingleton<RequestWorkbenchStateStore>();
		builder.Services.AddSingleton<IExecutionHistoryRepository, InMemoryExecutionHistoryRepository>();
		builder.Services.AddSingleton<VariableResolver>();
		builder.Services.AddSingleton<JsonEditorService>();
		builder.Services.AddSingleton<RequestCompiler>();
		builder.Services.AddSingleton<ResponseExtractionService>();
		builder.Services.AddSingleton<ForRestScriptParser>();
		builder.Services.AddSingleton<ForRestScriptDocumentTextService>();
		builder.Services.AddSingleton<IForRestScriptCompiler, ForRestScriptCompiler>();
		builder.Services.AddSingleton<ForRestRuntimeVariableSeedEvaluator>();
		builder.Services.AddSingleton<IRepeatRunnerService, RepeatRunnerService>();
		builder.Services.AddSingleton<IRequestAuthenticationService, RequestAuthenticationService>();
		builder.Services.AddSingleton<IScriptEngine, RoslynScriptEngine>();
		builder.Services.AddSingleton<IRequestExecutionService, RequestExecutionService>();
		builder.Services.AddSingleton<IForRestScriptExecutionService, ForRestScriptExecutionService>();
		builder.Services.AddSingleton<IAiSettingsValidator, AiSettingsValidator>();
		builder.Services.AddSingleton<IAiToolCatalog, AiLocalToolCatalog>();
		builder.Services.AddSingleton<IAiKnowledgeCatalog, ForRestAiKnowledgeCatalog>();
		builder.Services.AddSingleton<IAiPromptManifestBuilder, AiPromptManifestBuilder>();
		builder.Services.AddSingleton<IAiDocumentPatchService, AiDocumentPatchService>();
		builder.Services.AddSingleton<IAiDocumentationSearchService>(static services =>
			new AiDocumentationSearchService(services.GetRequiredService<IAiKnowledgeCatalog>().GetDocuments()));
		builder.Services.AddSingleton<IAiRuntimeFactory, AgentFrameworkAiRuntimeFactory>();
		builder.Services.AddSingleton<IAiTurnExecutor, AgentFrameworkAiTurnExecutor>();
		builder.Services.AddSingleton<IAiInlineConversationService, AiInlineConversationService>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton(static _ => new AndroidMainPage());

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
