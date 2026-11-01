using System.Security.Cryptography;

namespace ReplayViewer.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N") + ".zip"
    );

    public UpdateCheckerTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "payload");
    }

    [Fact]
    public void Matching_digest_passes()
    {
        UpdateChecker.VerifyDigest(_file, "sha256:" + Sha256(_file));
    }

    [Fact]
    public void Mismatching_digest_throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateChecker.VerifyDigest(_file, "sha256:" + new string('0', 64))
        );
    }

    // Deliberate: a release without a digest, or with an unknown algorithm, is accepted.
    // Failing closed instead is a decision to make on purpose, not by accident.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("md5:d41d8cd98f00b204e9800998ecf8427e")]
    public void Missing_or_unknown_digest_is_skipped(string? digest)
    {
        UpdateChecker.VerifyDigest(_file, digest);
    }

    public void Dispose() => File.Delete(_file);

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
