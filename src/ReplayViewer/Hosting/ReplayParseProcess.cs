using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ReplayViewer.Core;

namespace ReplayViewer;

/// <summary>
/// Parses one replay in a child process. FortniteReplayReader has no cancellation token, so a
/// replay it stalls on would keep a thread at full CPU until the app exits; a child can be killed.
/// </summary>
internal static class ReplayParseProcess
{
    public const string Switch = "--parse";

    private const string Unreadable = "This replay could not be read.";

    private const string Stalled = "The replay parser stalls on this file, so it cannot be opened.";

    /// <summary>Reads the replay, or throws once <paramref name="timeout"/> passes and the child is killed.</summary>
    public static async Task<MatchDocument> ParseAsync(
        string replayPath,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var output = Path.Combine(Path.GetTempPath(), $"replay-parse-{Guid.NewGuid():N}.json");
        var start = new ProcessStartInfo(
            Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot locate the app executable.")
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Switch);
        start.ArgumentList.Add(replayPath);
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        // A crafted file that allocates without bound should take down this child, not the app.
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x80000000";

        using var child = new Process { StartInfo = start };
        try
        {
            child.Start();
            try
            {
                // Backfill parses many replays while the game may be running.
                child.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            var errors = child.StandardError.ReadToEndAsync(CancellationToken.None);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);

            try
            {
                await child.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Stop(child);
                throw new TimeoutException(
                    $"Replay parse timed out after {timeout.TotalSeconds:0}s."
                );
            }
            catch (OperationCanceledException)
            {
                Stop(child);
                throw;
            }

            if (child.ExitCode != 0)
            {
                if (child.ExitCode == 3)
                {
                    throw new UnreadableReplayException(Stalled) { IsPermanent = true };
                }

                var message = (await errors.ConfigureAwait(false)).Trim();
                throw new UnreadableReplayException(message.Length > 0 ? message : Unreadable);
            }

            try
            {
                return JsonSerializer.Deserialize<MatchDocument>(
                        await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false),
                        HttpJson.Options
                    ) ?? throw new UnreadableReplayException(Unreadable);
            }
            catch (JsonException ex)
            {
                throw new UnreadableReplayException(Unreadable, ex);
            }
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp file must not turn a finished parse into a failure.
            }
        }
    }

    private static void Stop(Process child)
    {
        try
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The child exited between the timeout and the kill.
        }
    }
}

/// <summary>The child half: parses one replay and writes the assembled match as JSON.</summary>
internal static class ReplayParseCommand
{
    public static int Run(string replayPath, string outputPath, string? parentId)
    {
        try
        {
            if (int.TryParse(parentId, out var parent) && !WatchParent(parent))
            {
                return 1;
            }

            if (new FileInfo(replayPath).Length > ReplayFileAccess.MaxReplayBytes)
            {
                Console.Error.WriteLine("This replay file is too large to be a Fortnite replay.");
                return 1;
            }

            // The parent owns the deadline (and kills this process), so a second, equal timer here
            // would only race it and report the same slow file as 422 instead of 504.
            var probe = new FortniteReplayProbe { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            var snapshot = probe.Read(replayPath, CancellationToken.None);
            snapshot = snapshot with { SourceFileName = Path.GetFileName(replayPath) };
            var match = MatchAssembler.Build(snapshot, DateTimeOffset.UtcNow);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(match, HttpJson.Options));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// A parsing loop cannot be cancelled, so a child orphaned by a force-closed app would keep
    /// burning a core. Exit as soon as the app goes; false means it is already gone.
    /// </summary>
    private static bool WatchParent(int parentId)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(parentId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        new Thread(() =>
        {
            parent.WaitForExit();
            Environment.Exit(1);
        })
        {
            IsBackground = true,
            Name = "watch parent",
        }.Start();
        return true;
    }
}
