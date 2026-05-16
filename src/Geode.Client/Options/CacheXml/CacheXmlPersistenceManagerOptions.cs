namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>&lt;persistence-manager&gt;</c>. Extends
/// <see cref="CacheXmlLibraryOptions"/> with a free-form
/// <c>&lt;properties&gt;&lt;property name= value=&gt;</c> bag.
/// </summary>
public class CacheXmlPersistenceManagerOptions : CacheXmlLibraryOptions
{
    public CacheXmlPersistenceManagerOptions() { }

    public CacheXmlPersistenceManagerOptions(CacheXmlPersistenceManagerOptions other) : base(other)
    {
        Properties = new Dictionary<string, string>(other.Properties);
    }

    /// <summary>
    /// Nested <c>&lt;property name="..." value="..."/&gt;</c> entries.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <inheritdoc />
    /// <remarks>
    /// Covariant return — a base-typed slot dispatches virtually to
    /// this override and gets the subclass runtime type back.
    /// </remarks>
    public override CacheXmlPersistenceManagerOptions Clone() => new(this);

    /// <inheritdoc />
    public override IEnumerable<string> Validate(string prefix)
    {
        foreach (var f in base.Validate(prefix)) yield return f;
        // No structural rules for this subclass currently — parity stub.
    }
}
