namespace ReplayViewer.Core;

public enum EventKind
{
    Knock,
    Finish,
    SelfElim,
    Revive,
}

public enum FillStatus
{
    None,
    Fill,
    Unknown,
}

public sealed record ParsedReplaySnapshot(
    string SourceFileName,
    string? Playlist,
    int? TeamSize,
    DateTimeOffset? RecordedAt,
    double? SafeZonesStartTime,
    OwnerStatsSnapshot? OwnerStats,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<TeamSnapshot> Teams,
    IReadOnlyList<EventSnapshot> Events
);

public sealed record OwnerStatsSnapshot(
    int? Eliminations,
    double? DamageDealt,
    double? Accuracy,
    int? Assists,
    int? Revives,
    int? DamageTaken,
    int? Wood,
    int? Stone,
    int? Metal
);

public sealed record PlayerSnapshot(
    string? EpicId,
    string? DisplayName,
    string? RawPlatform,
    bool IsReplayOwner,
    bool IsBot,
    bool Disconnected,
    bool Anonymous,
    int? TeamIndex,
    int? Placement,
    string? PartyOwnerId,
    int? Kills,
    int? PlayerNumber = null
);

public sealed record TeamSnapshot(int TeamIndex, int? Placement);

public sealed record EventSnapshot(
    double TimeSeconds,
    EventKind Kind,
    string? ActorId,
    string? ActorName,
    string? TargetId,
    string? TargetName,
    string? Weapon,
    double? Distance,
    string? Cause
);

public sealed record MatchDocument(
    string SourceFile,
    DateTimeOffset ScrapedAt,
    string? Playlist,
    int? TeamSize,
    DateTimeOffset? RecordedAt,
    int? YourPlacement,
    int? TeamPlacement,
    double? SafeZonesStartTime,
    OwnerStatsSnapshot? OwnerStats,
    IReadOnlyList<TeamRoster> Teams,
    IReadOnlyList<MatchEvent> Events
);

public sealed record TeamRoster(
    int TeamIndex,
    int? Placement,
    IReadOnlyList<RosterPlayer> Players
);

public sealed record RosterPlayer(
    string? EpicId,
    string Name,
    string? Platform,
    int Kills,
    bool IsYou,
    bool IsBot,
    bool Disconnected,
    bool Anonymous,
    FillStatus Fill,
    int? PlayerNumber = null
);

public sealed record MatchEvent(
    double TimeSeconds,
    EventKind Kind,
    string? ActorId,
    string? ActorName,
    string? TargetId,
    string? TargetName,
    string? Weapon,
    double? Distance,
    string? Cause,
    int? ActorTeam,
    int? TargetTeam
);

public sealed record DemoListItem(
    string Name,
    string Path,
    long Length,
    DateTimeOffset LastWriteUtc
);

public interface IMatchScraper
{
    IReadOnlyList<DemoListItem> ListRecent(int take = 20);

    Task<MatchDocument> ScrapeAsync(string? replayPath, CancellationToken cancellationToken);
}
