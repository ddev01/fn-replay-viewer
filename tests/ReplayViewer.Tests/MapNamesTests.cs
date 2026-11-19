namespace ReplayViewer.Tests;

public class MapNamesTests
{
    [Theory]
    [InlineData("Playlist_Habanero_NoBuild_RopeSmile_Duos", "Reload - Springfield")]
    [InlineData("Playlist_Habanero_NoBuild_PiperBoot_Duos", "Reload - Surf City")]
    [InlineData("Playlist_Habanero_NoBuild_TimberStake_Duos", "Reload - Squid Grounds")]
    [InlineData("Playlist_ShowdownTournament_PiperBootNoBuildDuos_PBM", "Reload - Surf City")]
    [InlineData("Playlist_PlaygroundV2", "Creative")]
    public void Reload_codenames_become_map_names(string playlist, string expected)
    {
        Assert.Equal(expected, MapNames.ForPlaylist(playlist));
    }

    [Fact]
    public void Unknown_playlist_has_no_map()
    {
        Assert.Null(MapNames.ForPlaylist("Playlist_DefaultDuo"));
    }
}
