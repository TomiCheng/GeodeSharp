/*
namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>library-type</c> in the XSD —
/// <c>&lt;cache-loader&gt;</c>, <c>&lt;cache-listener&gt;</c>,
/// <c>&lt;cache-writer&gt;</c>, <c>&lt;partition-resolver&gt;</c>.
/// </summary>
/// <remarks>
/// These pointers reference a native shared library + entry function
/// used by cppcache to construct the callback. On the .NET side this
/// translates to a delegate / DI-registered type; the field is kept
/// here for parity only and is unlikely to ship in the .NET API.
/// </remarks>
public class CacheLibraryOptions : ICloneable
{
    public CacheLibraryOptions() { }

    public CacheLibraryOptions(CacheLibraryOptions other)
    {
        LibraryName = other.LibraryName;
        LibraryFunctionName = other.LibraryFunctionName;
    }

    /// <summary><c>library-name</c> attribute (optional).</summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary><c>library-function-name</c> attribute (required).</summary>
    public string LibraryFunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Deep clone via copy constructor. Virtual so a slot typed as
    /// <see cref="CacheLibraryOptions"/> but holding a subclass
    /// instance (e.g. <see cref="CachePersistenceManagerOptions"/>)
    /// dispatches to the subclass's <c>Clone</c> and copies its
    /// extra members.
    /// </summary>
    public virtual CacheLibraryOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>
    /// Validate. No structural rules at this level (cppcache parity
    /// stub — see CLAUDE.md "mirror then prune"). Subclasses override
    /// to add their own checks.
    /// </summary>
    public virtual IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}

*/