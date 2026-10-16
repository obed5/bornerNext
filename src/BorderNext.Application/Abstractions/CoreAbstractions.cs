using BorderNext.Domain.Entities;

namespace BorderNext.Application.Abstractions;

/// <summary>Contexte de l'utilisateur connecté.</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    short? RoleId { get; }
    string? RoleCode { get; }
    short? BorderPointId { get; }
    string MachineName { get; }
    string? PcNumber { get; }
    bool IsAuthenticated { get; }
    void Set(User user, string roleCode);
    void SetPcNumber(string? pcNumber);
    void Clear();
}

/// <summary>Hachage et vérification des mots de passe (PBKDF2).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>Journal d'audit applicatif (table AuditLog).</summary>
public interface IAuditWriter
{
    Task WriteAsync(string eventType, string? entity = null, string? entityId = null, string? details = null, CancellationToken ct = default);
}

/// <summary>Horloge (testable).</summary>
public interface IClock
{
    DateTime Now { get; }
}

/// <summary>Résultat d'une tentative de connexion.</summary>
public sealed record LoginResult(bool Success, string? ErrorCode, User? User, string? RoleCode)
{
    public static LoginResult Ok(User user, string roleCode) => new(true, null, user, roleCode);
    public static LoginResult Fail(string errorCode) => new(false, errorCode, null, null);
}

/// <summary>
/// Erreur de validation métier : <see cref="ErrorKey"/> est une clé de ressource
/// i18n (ex. Admin_Error_DuplicateCode) traduite par la couche de présentation.
/// </summary>
public sealed class DomainValidationException : Exception
{
    public string ErrorKey { get; }

    public DomainValidationException(string errorKey, string? detail = null)
        : base(detail ?? errorKey) => ErrorKey = errorKey;
}
