namespace Geode.Client.Options;

/// <summary>
/// cppcache log levels (from
/// <c>cppcache/include/geode/util/LogLevel.hpp</c>). Kept here verbatim
/// so we don't take a hard dependency on
/// <c>Microsoft.Extensions.Logging.LogLevel</c> from the options layer.
/// </summary>
/// <remarks>
/// Strong candidate for deletion once Phase 5 wiring is in: we plan to
/// route logging through <c>ILogger</c>, so duplicating the level set
/// here is purely for parity with cppcache during the audit window.
/// </remarks>
public enum LogLevel
{
    None,
    Error,
    Warning,
    Info,
    /// <summary>cppcache default.</summary>
    Default,
    Config,
    Fine,
    Finer,
    Finest,
    Debug,
    All,
}

/// <summary>
/// File-logging settings mirrored from cppcache <c>SystemProperties</c>
/// (<c>log-file</c>, <c>log-level</c>, <c>log-file-size-limit</c>,
/// <c>log-disk-space-limit</c>).
/// </summary>
/// <remarks>
/// CLAUDE.md routes logging through <c>ILogger</c>, so this whole group
/// is on the deletion shortlist. It is included now only so the audit
/// window can prove no consumer needs it; remove before Phase 5 ships if
/// nothing reads from it.
/// </remarks>
public class LogOptions : ICloneable
{
    public LogOptions() { }

    public LogOptions(LogOptions other)
    {
        Filename = other.Filename;
        Level = other.Level;
        FileSizeLimit = other.FileSizeLimit;
        DiskSpaceLimit = other.DiskSpaceLimit;
    }

    /// <summary>
    /// Path to the log file. Mirrors cppcache <c>log-file</c>; default
    /// empty (= stdout in cppcache).
    /// </summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Minimum severity emitted. Mirrors cppcache <c>log-level</c>;
    /// default <see cref="Geode.Client.Options.LogLevel.Config"/>.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Config;

    /// <summary>
    /// Maximum size of a single log file in megabytes before rolling.
    /// Mirrors cppcache <c>log-file-size-limit</c>; default 0 (=
    /// unlimited).
    /// </summary>
    public uint FileSizeLimit { get; set; }

    /// <summary>
    /// Maximum total disk space in megabytes for rolled log files.
    /// Mirrors cppcache <c>log-disk-space-limit</c>; default 0 (=
    /// unlimited).
    /// </summary>
    public uint DiskSpaceLimit { get; set; }

    /// <summary>Deep clone via copy constructor.</summary>
    public LogOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
