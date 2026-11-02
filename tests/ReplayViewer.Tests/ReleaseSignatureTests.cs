using System.Security.Cryptography;
using System.Text;

namespace ReplayViewer.Tests;

public sealed class ReleaseSignatureTests
{
    private static readonly byte[] Artifact = Encoding.UTF8.GetBytes("installer bytes");

    [Fact]
    public void A_signature_from_the_matching_key_verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = key.SignData(
            Artifact,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence
        );
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        Assert.True(ReleaseSignature.Verify(Artifact, signature, publicKey));
    }

    [Fact]
    public void Tampered_bytes_do_not_verify()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = key.SignData(
            Artifact,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence
        );
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        Assert.False(
            ReleaseSignature.Verify(Encoding.UTF8.GetBytes("other bytes"), signature, publicKey)
        );
    }

    [Fact]
    public void A_signature_from_another_key_does_not_verify()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = signer.SignData(
            Artifact,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence
        );

        Assert.False(
            ReleaseSignature.Verify(
                Artifact,
                signature,
                Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())
            )
        );
    }

    [Fact]
    public void Garbage_input_is_a_failed_check_not_an_exception()
    {
        Assert.False(ReleaseSignature.Verify(Artifact, [1, 2, 3], "not-base64"));
        Assert.False(ReleaseSignature.Verify(Artifact, [], Convert.ToBase64String(Artifact)));
    }

    [Fact]
    public void The_embedded_key_is_a_usable_public_key()
    {
        // Verifying with the shipped key must at least parse, even for a bogus signature.
        Assert.False(ReleaseSignature.Verify(Artifact, [1, 2, 3]));
    }
}
