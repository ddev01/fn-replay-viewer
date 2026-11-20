namespace ReplayViewer.Core;

public sealed class LiveReplayException : InvalidOperationException
{
    public LiveReplayException()
        : base("Replay header is still live.") { }
}

public sealed class ReplayStillWritingException : InvalidOperationException
{
    public ReplayStillWritingException()
        : base("The newest replay is still being written.") { }
}

/// <summary>The caller asked for a path that is not an openable replay in the Demos folder.</summary>
public sealed class InvalidReplayPathException : Exception
{
    public InvalidReplayPathException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class UnreadableReplayException : InvalidOperationException
{
    /// <summary>
    /// True when retrying cannot help (the parser stalls on this exact file). Anything else may
    /// be a locked file or a killed child and is worth trying again later.
    /// </summary>
    public bool IsPermanent { get; init; }

    public UnreadableReplayException(Exception? inner = null)
        : base(
            "This replay could not be read. It may still be recording — try again once the match ends.",
            inner
        )
    { }

    public UnreadableReplayException(string message, Exception? inner = null)
        : base(message, inner) { }
}
