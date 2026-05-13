namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>&lt;persistence-manager&gt;</c>. Extends
/// <see cref="CacheXmlLibraryOptions"/> with a free-form
/// <c>&lt;properties&gt;&lt;property name= value=&gt;</c> bag.
/// </summary>
public class CacheXmlPersistenceManagerOptions : CacheXmlLibraryOptions
{
    /// <summary>
    /// Nested <c>&lt;property name="..." value="..."/&gt;</c> entries.
    /// </summary>
    /// <remarks>
    /// Settable (rather than init-only) so <see cref="DeepClone"/> can
    /// reassign with a new dict instance — needed because
    /// <see cref="object.MemberwiseClone"/> only copies the reference,
    /// leaving the clone aliased to the original until we replace it.
    /// </remarks>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <inheritdoc />
    /// <remarks>
    /// Covariant return: callers with a static
    /// <c>CacheXmlPersistenceManagerOptions</c> reference get the
    /// subclass type back without a cast; callers via a base
    /// <see cref="CacheXmlLibraryOptions"/> reference still dispatch
    /// here virtually and receive the right runtime type.
    /// </remarks>
    public override CacheXmlPersistenceManagerOptions DeepClone()
    {
        var clone = (CacheXmlPersistenceManagerOptions)base.DeepClone();
        // string keys + values — Dictionary copy ctor is sufficient.
        clone.Properties = new Dictionary<string, string>(Properties);
        return clone;
    }

    /// <inheritdoc />
    public override IEnumerable<string> Validate(string prefix)
    {
        foreach (var f in base.Validate(prefix)) yield return f;
        // No structural rules for this subclass currently — parity stub.
    }
}
