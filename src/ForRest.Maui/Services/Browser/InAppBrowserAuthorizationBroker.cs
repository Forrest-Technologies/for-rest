using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Services;
using Microsoft.Extensions.Logging;

namespace ForRest.Maui.Services.Browser;

// ForRest.Maui.Services is imported for IWorkbenchOAuthSettingsProvider; this file's own
// ForRest.Maui.Services.Browser namespace covers InAppOAuthBrowserProvider/IInAppOAuthBrowser.

/// <summary>
/// Routes the interactive leg of the authorization-code grant either through the embedded browser pane
/// (when <c>oauth.use_internal_browser = true</c> and a pane is loaded) or, by default, through the
/// system-browser loopback broker. The in-app path needs no loopback HTTP server: the WebView intercepts
/// the redirect, so it works for loopback and non-loopback redirect URIs alike.
/// </summary>
public sealed class InAppBrowserAuthorizationBroker(
	InAppOAuthBrowserProvider inAppBrowserProvider,
	IWorkbenchOAuthSettingsProvider oauthSettingsProvider,
	ILogger<InAppBrowserAuthorizationBroker> logger) : IInteractiveAuthorizationBroker
{
	#region Private Fields

	private readonly SystemBrowserLoopbackBroker fallbackBroker = new(logger);

	#endregion

	#region Public Methods

	public Task<string> AcquireAuthorizationCode(InteractiveAuthorizationContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (ShouldUseInternalBrowser(out IInAppOAuthBrowser? inAppBrowser) && inAppBrowser is not null)
		{
			logger.LogInformation("Running OAuth sign-in in the embedded browser pane (oauth.use_internal_browser = true).");
			return inAppBrowser.Authorize(context.AuthorizationUrl, context.RedirectUri, context.State, cancellationToken);
		}

		return fallbackBroker.AcquireAuthorizationCode(context, cancellationToken);
	}

	#endregion

	#region Private Methods

	private bool ShouldUseInternalBrowser(out IInAppOAuthBrowser? inAppBrowser)
	{
		inAppBrowser = null;

		ForRestOAuthSettings settings = oauthSettingsProvider.GetCurrentSettings();
		if (!settings.UseInternalBrowser)
		{
			return false;
		}

		inAppBrowser = inAppBrowserProvider.Current;
		if (inAppBrowser is null)
		{
			logger.LogWarning(
				"oauth.use_internal_browser is enabled but no browser pane is loaded; falling back to the system browser.");
			return false;
		}

		return true;
	}

	#endregion
}
