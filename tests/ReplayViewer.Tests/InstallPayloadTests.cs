namespace ReplayViewer.Tests;

public sealed class InstallPayloadTests : IDisposable
{
    private static readonly string[] Preserved = ["logs", "skins"];
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReplayViewerTests",
        Guid.NewGuid().ToString("N")
    );

    private string Source => Path.Combine(_root, "source");

    private string Destination => Path.Combine(_root, "destination");

    public InstallPayloadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Sync_copies_new_files_and_overwrites_changed_ones()
    {
        Write(Path.Combine(Source, "ReplayViewer.exe"), "v2");
        Write(Path.Combine(Source, "wwwroot", "app.css"), "new");
        Write(Path.Combine(Destination, "ReplayViewer.exe"), "v1");

        InstallPayload.Sync(Source, Destination, Preserved);

        Assert.Equal("v2", Read(Path.Combine(Destination, "ReplayViewer.exe")));
        Assert.Equal("new", Read(Path.Combine(Destination, "wwwroot", "app.css")));
    }

    [Fact]
    public void Sync_removes_stale_files_and_directories_but_keeps_user_data()
    {
        Write(Path.Combine(Source, "ReplayViewer.exe"), "v2");
        Write(Path.Combine(Destination, "ReplayViewer.exe"), "v1");
        Write(Path.Combine(Destination, "old.dll"), "stale");
        Write(Path.Combine(Destination, "wwwroot", "old.svg"), "stale");
        Write(Path.Combine(Destination, "unins000.dat"), "installer");
        Write(Path.Combine(Destination, "logs", "replay-viewer.log"), "log");
        Write(Path.Combine(Destination, "skins", "hero.png"), "skin");

        InstallPayload.Sync(Source, Destination, Preserved);

        Assert.False(File.Exists(Path.Combine(Destination, "old.dll")));
        Assert.False(Directory.Exists(Path.Combine(Destination, "wwwroot")));
        Assert.True(File.Exists(Path.Combine(Destination, "unins000.dat")));
        Assert.True(File.Exists(Path.Combine(Destination, "logs", "replay-viewer.log")));
        Assert.True(File.Exists(Path.Combine(Destination, "skins", "hero.png")));
    }

    [Fact]
    public void Sync_is_a_no_op_when_source_equals_destination()
    {
        Write(Path.Combine(Source, "ReplayViewer.exe"), "v1");

        InstallPayload.Sync(Source, Source, Preserved);

        Assert.True(File.Exists(Path.Combine(Source, "ReplayViewer.exe")));
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string path) => File.ReadAllText(path);
}
