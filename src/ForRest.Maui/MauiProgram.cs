using ForRest.Browser;
using ForRest.Domain;
using ForRest.Maui.Theming;
using ForRest.Maui.Services;
using ForRest.Maui.Services.Browser;
using ForRest.Maui.ViewModels;
using ForRest.Repositories;
using ForRest.Services;
using ForRest.Services.AI;
using ForRest.Services.AI.OAuth;
using ForRest.Scripting;
using Microsoft.Extensions.Logging;
#if WINDOWS || MACCATALYST
using ForRest.Mcp;
using ForRest.Maui.Services.Mcp;
#endif

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
			})
#if ANDROID
			.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<Editor, ForRest.Maui.Platforms.Android.Handlers.SelectableEditorHandler>();
				handlers.AddHandler<WebView, ForRest.Maui.Platforms.Android.Handlers.ForRestWebViewHandler>();
				handlers.AddHandler<ForRest.Maui.Controls.SoraCodeEditorView, ForRest.Maui.Platforms.Android.Handlers.SoraCodeEditorViewHandler>();
			})
#endif
			;

		builder.Services.AddSingleton<ThemeCatalog>();
		builder.Services.AddSingleton<SettingsTomlTemplate>();
		builder.Services.AddSingleton<ThemeConfigParser>();
		builder.Services.AddSingleton<ThemeConfigNormalizer>();
		builder.Services.AddSingleton<ThemeConfigStore>();
		builder.Services.AddSingleton<SettingsTomlDocumentService>();
		builder.Services.AddSingleton<IWorkbenchAiSettingsProvider, WorkbenchAiSettingsProvider>();
		builder.Services.AddSingleton<IWorkbenchMcpSettingsProvider, WorkbenchMcpSettingsProvider>();
		builder.Services.AddSingleton<IWorkbenchOAuthSettingsProvider, WorkbenchOAuthSettingsProvider>();
		builder.Services.AddSingleton<IProviderTokenStore, SecureStorageProviderTokenStore>();
		builder.Services.AddSingleton<IProviderOAuthService, ProviderOAuthService>();
		builder.Services.AddSingleton<IThemeService, ThemeService>();
		builder.Services.AddSingleton<IBuildMetadataProvider, BuildMetadataProvider>();
		builder.Services.AddSingleton<RequestWorkbenchStateStore>();
		builder.Services.AddSingleton<BrowserAutomationProvider>();
		builder.Services.AddSingleton<IBrowserAutomationProvider>(serviceProvider => serviceProvider.GetRequiredService<BrowserAutomationProvider>());
		builder.Services.AddSingleton<InAppOAuthBrowserProvider>();
		builder.Services.AddSingleton<IInteractiveAuthorizationBroker, InAppBrowserAuthorizationBroker>();
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
		builder.Services.AddSingleton<IAiWorkspaceConversationService, AiWorkspaceConversationService>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<AndroidMainPage>();

#if WINDOWS || MACCATALYST
		// Desktop-only Model Context Protocol server host. Mobile targets
		// intentionally skip this because background TCP listeners are
		// hostile to the Android lifecycle.
		builder.Services.AddSingleton<McpLiveWorkbenchAccessor>();
		builder.Services.AddSingleton<ForRestMcpHost>();
		builder.Services.AddSingleton<ForRestMcpTools>(services =>
			new ForRestMcpTools(
				services.GetRequiredService<IAiKnowledgeCatalog>(),
				services.GetRequiredService<IAiDocumentationSearchService>(),
				() => services.GetService<ForRestMcpHost>()));
		builder.Services.AddSingleton<ForRestMcpServerLifecycle>();
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
