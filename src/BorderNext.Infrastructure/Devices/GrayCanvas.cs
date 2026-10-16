namespace BorderNext.Infrastructure.Devices;

/// <summary>
/// Encodeur BMP niveau de gris 8 bits — sans dépendance externe (le simulateur
/// d'appareils ne doit pas embarquer de bibliothèque graphique propriétaire).
/// Les pilotes matériels réels fourniront du JPEG/WSQ ; le stockage accepte tout
/// format binaire (héritage : varbinary).
/// </summary>
internal sealed class GrayCanvas
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _pixels;
    private readonly Random _random;

    public GrayCanvas(int width, int height, int seed)
    {
        Width = width;
        Height = height;
        _pixels = new byte[width * height];
        _random = new Random(seed);
    }

    public void Fill(byte value) => Array.Fill(_pixels, value);

    public void SetPixel(int x, int y, byte value)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height) _pixels[y * Width + x] = value;
    }

    public byte GetPixel(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height ? _pixels[y * Width + x] : (byte)0;

    /// <summary>Dégradé vertical entre deux niveaux.</summary>
    public void VerticalGradient(byte top, byte bottom)
    {
        for (var y = 0; y < Height; y++)
        {
            var value = (byte)(top + (bottom - top) * y / Math.Max(1, Height - 1));
            for (var x = 0; x < Width; x++) SetPixel(x, y, value);
        }
    }

    /// <summary>Ellipse pleine (cx, cy = centre ; rx, ry = rayons).</summary>
    public void FillEllipse(double cx, double cy, double rx, double ry, byte value)
    {
        var x0 = (int)Math.Max(0, cx - rx);
        var x1 = (int)Math.Min(Width - 1, cx + rx);
        var y0 = (int)Math.Max(0, cy - ry);
        var y1 = (int)Math.Min(Height - 1, cy + ry);
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                if (dx * dx + dy * dy <= 1.0) SetPixel(x, y, value);
            }
    }

    /// <summary>Bruit déterministe (capteur simulé).</summary>
    public void Noise(int amplitude)
    {
        for (var i = 0; i < _pixels.Length; i++)
        {
            var delta = _random.Next(-amplitude, amplitude + 1);
            _pixels[i] = (byte)Math.Clamp(_pixels[i] + delta, 0, 255);
        }
    }

    public byte[] ToBmp()
    {
        const int headerSize = 14 + 40 + 256 * 4;
        var rowSize = (Width + 3) / 4 * 4;
        var imageSize = rowSize * Height;
        using var stream = new MemoryStream(headerSize + imageSize);
        using var writer = new BinaryWriter(stream);

        // En-tête fichier
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(headerSize + imageSize);
        writer.Write(0);
        writer.Write(headerSize);

        // En-tête BITMAPINFOHEADER
        writer.Write(40);
        writer.Write(Width);
        writer.Write(Height);          // positif = lignes de bas en haut
        writer.Write((short)1);
        writer.Write((short)8);        // 8 bits par pixel
        writer.Write(0);               // BI_RGB
        writer.Write(imageSize);
        writer.Write(2835); writer.Write(2835);
        writer.Write(256); writer.Write(256);

        // Palette niveaux de gris
        for (var i = 0; i < 256; i++)
        {
            writer.Write((byte)i); writer.Write((byte)i); writer.Write((byte)i); writer.Write((byte)0);
        }

        // Données (bas en haut, lignes alignées sur 4 octets)
        for (var y = Height - 1; y >= 0; y--)
        {
            writer.Write(_pixels, y * Width, Width);
            for (var pad = Width; pad < rowSize; pad++) writer.Write((byte)0);
        }

        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>Gabarit d'empreinte du simulateur (format propre, non VeriFinger).</summary>
internal static class SimulatedTemplate
{
    public const int Size = 64;
    private const byte Magic0 = (byte)'B';
    private const byte Magic1 = (byte)'N';

    /// <summary>
    /// 64 octets : magic « BN », version, doigt, empreinte de l'identité simulée
    /// (12 octets), qualité, paramètres de crêtes, somme de contrôle SHA-256.
    /// </summary>
    public static byte[] Create(string identity, short fingerNumber, int quality)
    {
        var bytes = new byte[Size];
        bytes[0] = Magic0;
        bytes[1] = Magic1;
        bytes[2] = 1;
        bytes[3] = 0;
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 2), fingerNumber);
        var identityHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"SIM:{identity}:{fingerNumber}"));
        identityHash.AsSpan(0, 12).CopyTo(bytes.AsSpan(6));
        BitConverter.TryWriteBytes(bytes.AsSpan(18, 2), (short)quality);
        // Paramètres de crêtes déterministes (fréquence, décalage du cœur, angle).
        var ridge = System.Security.Cryptography.SHA256.HashData(identityHash);
        ridge.AsSpan(0, 16).CopyTo(bytes.AsSpan(20));
        var checksum = System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, 36));
        checksum.AsSpan(0, Size - 36).CopyTo(bytes.AsSpan(36));
        return bytes;
    }

    /// <summary>Structure valide (magic, version, somme de contrôle) ?</summary>
    public static bool IsValid(byte[]? template)
        => template is { Length: Size } t
           && t[0] == Magic0 && t[1] == Magic1 && t[2] == 1
           && System.Security.Cryptography.SHA256.HashData(t.AsSpan(0, 36)).AsSpan(0, Size - 36)
               .SequenceEqual(t.AsSpan(36));

    public static short FingerOf(byte[] template) => BitConverter.ToInt16(template, 4);
}
