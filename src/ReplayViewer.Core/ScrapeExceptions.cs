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

public sealed class UnreadableReplayException : InvalidOperationException
{
    public UnreadableReplayException(Exception? inner = null)
        : base(
            "This replay could not be read. It may still be recording — try again once the match ends.",
            inner
        ) { }
}
