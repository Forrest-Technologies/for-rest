using System.Collections.Concurrent;

namespace ForRest.Services.AI.OAuth;

/// <summary>
/// Persistence seam for provider OAuth tokens. Application heads choose platform-protected
/// storage or a process-lifetime store; the service depends only on this abstraction.
/// </summary>
public interface IProviderTokenStore
{
    Task Save(AiProviderKind provider, ProviderOAuthToken token, CancellationToken cancellationToken = default);

    Task<ProviderOAuthToken?> Load(AiProviderKind provider, CancellationToken cancellationToken = default);

    Task Clear(AiProviderKind provider, CancellationToken cancellationToken = default);
}

/// <summary>
/// Volatile, process-lifetime token store. Default registration so the service is usable in tests
/// and headless contexts, and in ad-hoc Mac builds without provisioned Keychain access.
/// </summary>
public sealed class InMemoryProviderTokenStore : IProviderTokenStore
{
    #region Private Fields

    private readonly ConcurrentDictionary<AiProviderKind, ProviderOAuthToken> tokens = new();

    #endregion

    #region Public Methods

    public Task Save(AiProviderKind provider, ProviderOAuthToken token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tokens[provider] = token;
        return Task.CompletedTask;
    }

    public Task<ProviderOAuthToken?> Load(AiProviderKind provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tokens.TryGetValue(provider, out ProviderOAuthToken? token) ? token : null);
    }

    public Task Clear(AiProviderKind provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tokens.TryRemove(provider, out _);
        return Task.CompletedTask;
    }

    #endregion
}
