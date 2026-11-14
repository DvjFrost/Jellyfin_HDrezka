using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class AnubisTests
{
    [Fact]
    public void ParsesAndSolvesFastProofOfWork()
    {
        const string html = "<script id='anubis_base_prefix'>\"\"</script><script id='anubis_challenge'>{\"rules\":{\"algorithm\":\"fast\",\"difficulty\":2},\"challenge\":{\"id\":\"test-id\",\"randomData\":\"hunter\"}}</script>";
        var challenge = Assert.IsType<AnubisChallenge>(Anubis.TryParse(html));
        var proof = Assert.IsType<AnubisProof>(Anubis.Solve(challenge, TimeSpan.FromSeconds(3), CancellationToken.None));
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hunter" + proof.Nonce)));
        Assert.Equal(expected, proof.Hash);
        Assert.StartsWith("00", proof.Hash);
    }
    [Fact]
    public void RejectsUnknownAlgorithmsAndUntrustedPrefixes()
    {
        const string unsupported = "<script id='anubis_challenge'>{\"rules\":{\"algorithm\":\"argon2id\",\"difficulty\":2},\"challenge\":{\"id\":\"a\",\"randomData\":\"b\"}}</script>";
        const string outside = "<script id='anubis_base_prefix'>\"//evil.example\"</script><script id='anubis_challenge'>{\"rules\":{\"algorithm\":\"fast\",\"difficulty\":2},\"challenge\":{\"id\":\"a\",\"randomData\":\"b\"}}</script>";
        Assert.Null(Anubis.TryParse(unsupported));
        Assert.Null(Anubis.TryParse(outside));
    }
    [Fact]
    public void CancelsPromptly()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => Anubis.Solve(new AnubisChallenge("x", "hunter", 7, ""), TimeSpan.FromSeconds(3), source.Token));
    }
}
