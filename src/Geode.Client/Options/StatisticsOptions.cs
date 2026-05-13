namespace Geode.Client.Options;

/// <summary>
/// Statistics-archive settings mirrored from cppcache
/// <c>SystemProperties</c> (<c>statistic-*</c>). Included for parity
/// during the cppcache audit window.
/// </summary>
/// <remarks>
/// CLAUDE.md replaces the cppcache statistics archive with
/// <c>EventCounters</c> / OpenTelemetry, so this whole group is on the
/// deletion shortlist. Remove once we confirm no consumer reads from it.
/// </remarks>
public class StatisticsOptions
{
    /// <summary>
    /// Whether to write a statistics archive at all. Mirrors cppcache
    /// <c>statistic-sampling-enabled</c>; default <c>false</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Sampling cadence. Mirrors cppcache
    /// <c>statistic-sample-rate</c>; default 1 second.
    /// </summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Path to the statistics archive file. Mirrors cppcache
    /// <c>statistic-archive-file</c>; default <c>"statArchive.gfs"</c>.
    /// </summary>
    public string ArchiveFile { get; set; } = "statArchive.gfs";

    /// <summary>
    /// Maximum size of a single archive file in megabytes before rolling.
    /// Mirrors cppcache <c>archive-file-size-limit</c>; default 0 (=
    /// unlimited).
    /// </summary>
    public uint FileSizeLimit { get; set; }

    /// <summary>
    /// Maximum total disk space in megabytes for rolled archive files.
    /// Mirrors cppcache <c>archive-disk-space-limit</c>; default 0 (=
    /// unlimited).
    /// </summary>
    public uint DiskSpaceLimit { get; set; }

    /// <summary>
    /// Whether to capture per-operation timing statistics. Mirrors
    /// cppcache <c>enable-time-statistics</c>; default <c>false</c>.
    /// </summary>
    public bool TimeStatisticsEnabled { get; set; }

    /// <summary>Deep clone. Only primitives / string / TimeSpan — MemberwiseClone is sufficient.</summary>
    public StatisticsOptions DeepClone() => (StatisticsOptions)MemberwiseClone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
