namespace ReplayViewer.Tests;

public sealed class OwnerIdentitiesTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N"),
        "owners.json"
    );

    [Fact]
    public void Learns_the_owner_and_remembers_it_across_restarts()
    {
        new OwnerIdentities(_path).Learn(Match(youId: "ABC"));

        var reopened = new OwnerIdentities(_path);
        Assert.Contains("ABC", reopened.Ids);
    }

    [Fact]
    public void A_match_without_an_owner_teaches_nothing()
    {
        var identities = new OwnerIdentities(_path);
        identities.Learn(Match(youId: null));

        Assert.Empty(identities.Ids);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Ids_are_case_insensitive_and_not_duplicated()
    {
        var identities = new OwnerIdentities(_path);
        identities.Learn(Match(youId: "abc"));
        identities.Learn(Match(youId: "ABC"));

        Assert.Single(identities.Ids);
    }

    [Fact]
    public void A_picked_id_is_remembered()
    {
        new OwnerIdentities(_path).Add("picked-id");

        Assert.Contains("picked-id", new OwnerIdentities(_path).Ids);
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MatchDocument Match(string? youId) =>
        new(
            "a.replay",
            DateTimeOffset.UnixEpoch,
            "Playlist_DefaultDuo",
            2,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            [
                new TeamRoster(
                    1,
                    1,
                    [
                        new RosterPlayer(
                            youId,
                            "Me",
                            "WIN",
                            1,
                            IsYou: youId is not null,
                            false,
                            false,
                            false,
                            FillStatus.None
                        ),
                    ]
                ),
            ],
            []
        );
}
