namespace ReplayViewer.Core;

public sealed class LiveReplayException : InvalidOperationException
{
    public LiveReplayException()
        : base("Replay header is still live.") { }
}

public sealed class ReplayStillWritingException : InvalidOperationException
{
    public ReplayStillWritingException()
        : base("Newest replay is still being written.") { }
}
