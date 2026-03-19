using ForRest.Domain;
using ForRest.Maui.Theming;
using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;
using ForRest.Repositories;
using ForRest.Services;
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
		builder.Services.AddSingleton<IThemeService, ThemeService>();
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
		builder.Services.AddSingleton<IScriptEngine, RoslynScriptEngine>();
		builder.Services.AddSingleton<IRepeatRunnerService, RepeatRunnerService>();
		builder.Services.AddSingleton<IRequestExecutionService, RequestExecutionService>();
		builder.Services.AddSingleton<IForRestScriptExecutionService, ForRestScriptExecutionService>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
