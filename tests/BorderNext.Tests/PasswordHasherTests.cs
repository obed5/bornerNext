using BorderNext.Infrastructure.Security;

namespace BorderNext.Tests;

public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ThenVerify_ReturnsTrue()
    {
        var hash = _hasher.Hash("MotDePasse!2026");
        Assert.True(_hasher.Verify("MotDePasse!2026", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("MotDePasse!2026");
        Assert.False(_hasher.Verify("AutreChose!2026", hash));
    }

    [Fact]
    public void Hash_TwoCalls_ProduceDifferentSalts()
    {
        var h1 = _hasher.Hash("SamePassword");
        var h2 = _hasher.Hash("SamePassword");
        Assert.NotEqual(h1, h2);
        Assert.True(_hasher.Verify("SamePassword", h1));
        Assert.True(_hasher.Verify("SamePassword", h2));
    }

    [Fact]
    public void Hash_Format_ContainsIterationsAndBase64()
    {
        var hash = _hasher.Hash("x");
        var parts = hash.Split('$');
        Assert.Equal(5, parts.Length);
        Assert.Equal("PBKDF2", parts[0]);
        Assert.Equal("SHA256", parts[1]);
        Assert.True(int.Parse(parts[2]) >= 100_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2$SHA256$1000$invalid$invalid")]
    [InlineData("PBKDF2$MD5$100000$AAAA$BBBB")]
    public void Verify_InvalidHashFormats_ReturnFalse(string hash)
    {
        Assert.False(_hasher.Verify("password", hash));
    }

    [Fact]
    public void Verify_MalformedBase64_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("password", "PBKDF2$SHA256$210000$!!!$###"));
    }
}
