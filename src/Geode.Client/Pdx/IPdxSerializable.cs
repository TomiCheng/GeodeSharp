namespace Geode.Client.Pdx;

/// <summary>Intrusive PDX serialization; the type itself reads / writes its fields.</summary>
public interface IPdxSerializable<TSelf>
    where TSelf : IPdxSerializable<TSelf>
{
    /// <summary>Serialize this instance's fields.</summary>
    void ToData(IPdxWriter writer);

    /// <summary>Reconstruct an instance from the reader.</summary>
    static abstract TSelf FromData(IPdxReader reader);

    /// <summary>
    /// 估算本實例的記憶體佔用(bytes),供 heap-LRU 帳本累計。對應
    /// cppcache <c>Serializable::objectSize()</c>(default 回 0)。
    /// 預設 DIM 回 0 = 不參與 heap 控管,跟 cppcache 行為一致;
    /// 個別型別視需要 override(例:<c>buffer_.size()</c> 已序列化的 PDX
    /// instance 直接回 buffer 長度)。
    /// </summary>
    int GetObjectSize() => 0;
}
