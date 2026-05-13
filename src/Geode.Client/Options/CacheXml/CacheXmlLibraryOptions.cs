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
public class CacheXmlLibraryOptions
{
    /// <summary><c>library-name</c> attribute (optional).</summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary><c>library-function-name</c> attribute (required).</summary>
    public string LibraryFunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Deep clone. Virtual so subclasses (e.g.
    /// <see cref="CacheXmlPersistenceManagerOptions"/>) can extend
    /// it; properties typed as <see cref="CacheXmlLibraryOptions"/>
    /// will clone polymorphically.
    /// </summary>
    public virtual CacheXmlLibraryOptions DeepClone()
    {
        // Only primitives + string at this level — MemberwiseClone
        // preserves the runtime type, so subclass-only fields come
        // along (subclasses override DeepClone to deep-copy their
        // own reference-typed members).
        return (CacheXmlLibraryOptions)MemberwiseClone();
    }

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
