using System.Security.Cryptography;

namespace ReplayViewer;

/// <summary>
/// Checks a downloaded update against a key that never leaves the maintainer's machine.
/// GitHub's asset digest only proves the file matches what GitHub stores, so a compromised
/// account could publish a matching pair. This proves the release came from the signing key.
/// </summary>
internal static class ReleaseSignature
{
    // ECDSA P-256, SubjectPublicKeyInfo DER, base64. The private half lives offline; see
    // scripts/sign-release.sh for the signing side.
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEQFnlZjkhuh4PCWb4SJdqZTKAMHZv+UMmHCJe0oib4AA4yB0BteAwgRj6QOWto/sLgbEnRHaD195Hr6DHlzlVOg==";

    public static bool Verify(byte[] data, byte[] signature) => Verify(data, signature, PublicKey);

    internal static bool Verify(byte[] data, byte[] signature, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            // openssl dgst -sign emits a DER SEQUENCE, which is Rfc3279DerSequence.
            return key.VerifyData(
                data,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence
            );
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
