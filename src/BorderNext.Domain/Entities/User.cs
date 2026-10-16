namespace BorderNext.Domain.Entities;

/// <summary>Rôle utilisateur (table héritée : lUserRole).</summary>
public class UserRole
{
    public short Id { get; set; }

    /// <summary>Code stable utilisé par le code (ex. ADMIN, IMMIG_OFF).</summary>
    public string Code { get; set; } = string.Empty;

    public short? SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }

    public ICollection<UserRoleCulture> Cultures { get; set; } = new List<UserRoleCulture>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

/// <summary>Traduction du libellé d'un rôle (table héritée : lUserRoleCultures).</summary>
public class UserRoleCulture
{
    public int Id { get; set; }
    public short OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public UserRole? Owner { get; set; }
}

/// <summary>Compte utilisateur (table héritée : Users).</summary>
public class User
{
    /// <summary>Identifiant métier (héritage : varchar(20)).</summary>
    public string Id { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 (jamais de mot de passe en clair ; héritage corrigé).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>À changer à la première connexion (politique héritée, conservée).</summary>
    public bool MustChangePassword { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    public short UserRoleId { get; set; }
    public UserRole? UserRole { get; set; }

    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    // Biométrie opérateur (héritage : Users.FirstFingerPrint / SecondFingerPrint
    // + gabarits) — deux doigts enrôlés par utilisateur, contrôle à la connexion
    // (héritage : UserLoginForm.CheckFingerAgainstUsers).
    public byte[]? FirstFingerprint { get; set; }
    public byte[]? FirstFingerprintTemplate { get; set; }
    public byte[]? SecondFingerprint { get; set; }
    public byte[]? SecondFingerprintTemplate { get; set; }
}
