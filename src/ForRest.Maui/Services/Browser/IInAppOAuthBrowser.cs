namespace ForRest.Maui.Services.Browser;

/// <summary>
/// Drives an interactive OAuth authorization-code sign-in inside the embedded browser pane instead of
/// the external system browser. The browser pane implements this and registers itself with the
/// <see cref="InAppOAuthBrowserProvider"/> while it is loaded.
///
/// Behavior contract:
/// <list type="bullet">
/// <item>Save the current UX state (at minimum the selected center tab) so it can be restored afterward.</item>
/// <item>Switch the center tab to the browser pane and navigate it to <paramref name="authorizeUrl"/>.</item>
/// <item>Intercept WebView navigation; when the target matches <paramref name="redirectUri"/>
/// (scheme + host + port + path, ignoring the query), cancel that navigation, extract the authorization
/// code, and complete.</item>
/// <item>Honor the cancellation token (e.g. the run/stop control) and a bounded timeout; never hang.</item>
/// <item>Always restore the saved UX state, whether the flow succeeds, is cancelled, or fails.</item>
/// </list>
/// </summary>
public interface IInAppOAuthBrowser
{
	Task<string> Authorize(string authorizeUrl, string redirectUri, string state, CancellationToken cancellationToken);
}

/// <summary>
/// App-wide holder for the currently available <see cref="IInAppOAuthBrowser"/>. The live browser pane
/// calls <see cref="Connect"/> when it loads and <see cref="Disconnect"/> when it tears down. Until a
/// pane connects, <see cref="Current"/> is null, so the authorization broker falls back to the system
/// browser. This mirrors <see cref="BrowserAutomationProvider"/> as the single seam the broker resolves
/// through, keeping it unaware of the WebView itself.
/// </summary>
public sealed class InAppOAuthBrowserProvider
{
	#region Private Fields

	private volatile IInAppOAuthBrowser? current;

	#endregion

	#region Properties

	public IInAppOAuthBrowser? Current => current;

	#endregion

	#region Public Methods

	public void Connect(IInAppOAuthBrowser browser) => current = browser;

	public void Disconnect(IInAppOAuthBrowser browser)
	{
		// Only clear if the disconnecting pane is the one currently registered, so a stale teardown from
		// a replaced pane cannot drop the live one.
		if (ReferenceEquals(current, browser))
		{
			current = null;
		}
	}

	#endregion
}
