using System.Text.Json;
using ForRest.Services.AI;
using ForRest.Services.AI.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ForRest.Maui.Services;

/// <summary>
/// Durable, protected <see cref="IProviderTokenStore"/> backed by MAUI <see cref="SecureStorage"/>
/// (Keychain on Apple platforms, the Android Keystore-backed store on Android, and DPAPI-protected
/// storage on Windows). Provider OAuth tokens are JSON-serialized and held per provider so a sign-in
/// survives app restarts without the access/refresh tokens ever touching plain settings or the
/// workbench state file.
/// </summary>
public sealed class SecureStorageProviderTokenStore(ILogger<SecureStorageProviderTokenStore> logger) : IProviderTokenStore
{
    #region Public Methods

    public async Task Save(AiProviderKind provider, ProviderOAuthToken token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(Key(provider), JsonSerializer.Serialize(token));
    }

    public async Task<ProviderOAuthToken?> Load(AiProviderKind provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? json = await SecureStorage.Default.GetAsync(Key(provider));
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProviderOAuthToken>(json);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Stored OAuth token for {Provider} could not be read; clearing it.", provider);
            SecureStorage.Default.Remove(Key(provider));
            return null;
        }
    }

    public Task Clear(AiProviderKind provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(Key(provider));
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private static string Key(AiProviderKind provider) => $"forrest.oauth.token.{provider}";

    #endregion
}
