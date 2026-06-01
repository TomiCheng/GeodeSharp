namespace Geode.Client.Pdx;

/// <summary>External PDX serializer for types that can't (or shouldn't) implement <see cref="IPdxSerializable{TSelf}"/>.</summary>
public interface IPdxSerializer<T>
{
    /// <summary>Serialize <paramref name="obj"/>'s fields.</summary>
    void ToData(T obj, IPdxWriter writer);

    /// <summary>Reconstruct a <typeparamref name="T"/> instance from the reader.</summary>
    T FromData(IPdxReader reader);

    /// <summary>
    /// 估算 <paramref name="obj"/> 的記憶體佔用(bytes),供 heap-LRU 帳本
    /// 累計。對應 cppcache <c>PdxSerializer::objectSize(obj)</c> 模式
    /// (外部 serializer 替「不能改的型別」報尺寸)。預設 DIM 回 0 =
    /// 該型別不參與 heap 控管,跟 cppcache 行為一致;serializer 視需要 override。
    /// </summary>
    int GetObjectSize(T obj) => 0;
}
