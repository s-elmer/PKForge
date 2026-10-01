namespace PKForge.Domain;

/// <summary>
/// The IV rank as PKHeX shows it: one to four stars from the six IVs' total.
/// 0-90 one star, 91-120 two, 121-150 three, 151-186 four.
/// </summary>
public static class IvRank
{
    /// <summary>The star count for an IV total (Gen 3 onward, IVs 0-31).</summary>
    public static int Stars(int total) => total switch
    {
        <= 90 => 1,
        <= 120 => 2,
        <= 150 => 3,
        _ => 4,
    };

    public static int Stars(IReadOnlyList<int> ivs) => Stars(ivs.Sum());
}
