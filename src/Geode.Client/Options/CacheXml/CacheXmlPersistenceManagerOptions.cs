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
    public Dictionary<string, string> Properties { get; } = new();
}
