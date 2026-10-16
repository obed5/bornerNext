using System.Security.Cryptography;
using System.Text;
using BorderNext.Application.Abstractions;

namespace BorderNext.Infrastructure.Security;

/// <summary>
/// Protection des secrets par DPAPI (Windows) : portée utilisateur courant — le secret
/// n'est lisible que par le compte de service qui l'a écrit, sur cette machine.
/// Hors Windows (tests, CI), repli explicite sur un encodage Base64 marqué comme tel :
/// la valeur n'est pas chiffrée et l'appelant en est averti via <see cref="IsProtected"/>.
/// Le contenu protégé est binaire : en-tête ASCII suivi des octets chiffrés.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary>Marqueur de tête des valeurs protégées par DPAPI.</summary>
    public const string ProtectedPrefix = "dpapi:";

    /// <summary>Marqueur des valeurs de repli (développement et CI uniquement).</summary>
    public const string PlainPrefix = "plain:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BorderNext.Secret.v1");
    private static readonly byte[] ProtectedHeader = Encoding.ASCII.GetBytes(ProtectedPrefix);
    private static readonly byte[] PlainHeader = Encoding.ASCII.GetBytes(PlainPrefix);

    public bool IsProtected => OperatingSystem.IsWindows();

    public byte[] Protect(string secret)
    {
        var raw = Encoding.UTF8.GetBytes(secret);
        if (!OperatingSystem.IsWindows())
            return Concat(PlainHeader, Encoding.ASCII.GetBytes(Convert.ToBase64String(raw)));

        // DPAPI chiffre côté machine/utilisateur : la valeur n'est pas lisible ailleurs.
        return Concat(ProtectedHeader, ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser));
    }

    public string Unprotect(byte[] protectedSecret)
    {
        if (protectedSecret.Length == 0) return string.Empty;

        if (StartsWith(protectedSecret, PlainHeader))
        {
            var base64 = Encoding.ASCII.GetString(protectedSecret, PlainHeader.Length,
                protectedSecret.Length - PlainHeader.Length);
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        if (!StartsWith(protectedSecret, ProtectedHeader))
            throw new CryptographicException("Secret non protégé : format inconnu.");

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Un secret protégé par DPAPI ne peut être lu que sous Windows.");

        var payload = new byte[protectedSecret.Length - ProtectedHeader.Length];
        Array.Copy(protectedSecret, ProtectedHeader.Length, payload, 0, payload.Length);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser));
    }

    private static byte[] Concat(byte[] head, byte[] tail)
    {
        var result = new byte[head.Length + tail.Length];
        head.CopyTo(result, 0);
        tail.CopyTo(result, head.Length);
        return result;
    }

    private static bool StartsWith(byte[] value, byte[] prefix)
    {
        if (value.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (value[i] != prefix[i]) return false;
        return true;
    }
}
