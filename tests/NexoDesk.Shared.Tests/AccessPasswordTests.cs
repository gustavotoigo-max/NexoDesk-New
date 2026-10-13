using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Security;

namespace NexoDesk.Shared.Tests;

public class AccessPasswordTests
{
    private const string Password = "Senha-forte-2026!";
    private static readonly string Client = new('A', 64);
    private static readonly string Server = new('B', 64);
    private static readonly PasswordVerifier Verifier = AccessPassword.Create(Password);

    [Fact]
    public void CorrectPassword_Authenticates()
    {
        var challenge = AccessPassword.Challenge(Verifier);
        string proof = AccessPassword.Prove(Password, challenge, Client, Server, SessionMode.Control);
        Assert.True(AccessPassword.Verify(Verifier, challenge, proof, Client, Server, SessionMode.Control));
        Assert.DoesNotContain(Password, Verifier.ToString());
    }

    [Fact]
    public void Proof_CannotBeReplayedOrTransferred()
    {
        var challenge = AccessPassword.Challenge(Verifier);
        string proof = AccessPassword.Prove(Password, challenge, Client, Server, SessionMode.Control);
        Assert.False(AccessPassword.Verify(Verifier, AccessPassword.Challenge(Verifier), proof, Client, Server, SessionMode.Control));
        Assert.False(AccessPassword.Verify(Verifier, challenge, proof, Server, Client, SessionMode.Control));
        Assert.False(AccessPassword.Verify(Verifier, challenge, proof, Client, Server, SessionMode.View));
    }

    [Fact]
    public void WrongPassword_IsRejected()
    {
        var challenge = AccessPassword.Challenge(Verifier);
        string proof = AccessPassword.Prove("Outra-senha-2026!", challenge, Client, Server, SessionMode.Control);
        Assert.False(AccessPassword.Verify(Verifier, challenge, proof, Client, Server, SessionMode.Control));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void MalformedProof_IsRejected(string? proof)
    {
        Assert.False(AccessPassword.Verify(Verifier, AccessPassword.Challenge(Verifier), proof, Client, Server, SessionMode.Control));
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("            ")]
    public void WeakPassword_IsRejected(string password) => Assert.Throws<ArgumentException>(() => AccessPassword.Create(password));

    [Fact]
    public void ExcessiveWorkFactor_IsRejectedBeforeDerivation()
    {
        var challenge = AccessPassword.Challenge(Verifier) with { Iterations = int.MaxValue };
        Assert.Throws<ArgumentException>(() => AccessPassword.Prove(Password, challenge, Client, Server, SessionMode.Control));
    }

    [Fact]
    public void SamePassword_UsesIndependentSalts()
    {
        var other = AccessPassword.Create(Password);
        Assert.NotEqual(Verifier.Salt, other.Salt);
        Assert.NotEqual(Verifier.Hash, other.Hash);
    }
}