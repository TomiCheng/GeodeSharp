namespace Geode.Client.Options;

/// <summary>PDX options.</summary>
public class CachePdxOptions : ICloneable
{
    public CachePdxOptions() { }

    public CachePdxOptions(CachePdxOptions other)
    {
        IgnoreUnreadFields = other.IgnoreUnreadFields;
        ReadSerialized = other.ReadSerialized;
    }

    /// <summary>
    /// Drop fields the local schema doesn't know about on read.
    /// </summary>
    public bool? IgnoreUnreadFields { get; set; }

    /// <summary>
    /// Keep PDX values serialised on read.
    /// </summary>
    public bool? ReadSerialized { get; set; }

    /// <summary>
    /// Deep clone.
    /// </summary>
    public CachePdxOptions Clone() => new(this);

    object ICloneable.Clone() => Clone();

    /// <summary>
    /// Validate this section.
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
