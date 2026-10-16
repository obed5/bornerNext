using Microsoft.Extensions.DependencyInjection;

namespace BorderNext.UI;

/// <summary>
/// Portées de services liées aux sessions utilisateur.
/// Les services « scoped » (DbContext, AuthService, SettingsService…) vivent le temps
/// d'une session : ils sont créés à la connexion et libérés à la déconnexion.
/// </summary>
public sealed class SessionScopes : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private IServiceScope? _current;

    public SessionScopes(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Services de la session courante (créés à la demande).</summary>
    public IServiceProvider Current
    {
        get
        {
            _current ??= _scopeFactory.CreateScope();
            return _current.ServiceProvider;
        }
    }

    /// <summary>Démarre une nouvelle session : libère la précédente puis crée une portée.</summary>
    public IServiceProvider NewSession()
    {
        _current?.Dispose();
        _current = _scopeFactory.CreateScope();
        return _current.ServiceProvider;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
    }
}
