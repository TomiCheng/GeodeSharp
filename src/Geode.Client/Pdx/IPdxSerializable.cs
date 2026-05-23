namespace Geode.Client.Pdx;

/// <summary>Intrusive PDX serialization; the type itself reads / writes its fields.</summary>
public interface IPdxSerializable<TSelf>
    where TSelf : IPdxSerializable<TSelf>
{
    /// <summary>Serialize this instance's fields.</summary>
    void ToData(IPdxWriter writer);

    /// <summary>Reconstruct an instance from the reader.</summary>
    static abstract TSelf FromData(IPdxReader reader);
}
