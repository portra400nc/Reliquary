namespace Reliquary;

// What the item picker needs to know about one item. Affixes are the item's stats by label, main stat included.
public sealed record PickerItem(ulong Guid, int Level, int Rarity, uint Set, string Secondary, IReadOnlyDictionary<string, float> Affixes, bool Equipped);

public sealed class PickerQuery
{
    public const int MaxSortAffixes = 3;

    public HashSet<uint> Sets { get; } = [];
    public HashSet<string> Secondary { get; } = [];
    public List<string> Sort { get; } = [];
    public bool OnlyFree { get; set; }

    // A fourth affix is refused, so the caller can say why.
    public bool ToggleSort(string affix)
    {
        if (Sort.Remove(affix))
            return true;
        if (Sort.Count >= MaxSortAffixes)
            return false;
        Sort.Add(affix);
        return true;
    }
}

public static class Picking
{
    public static List<PickerItem> Apply(IEnumerable<PickerItem> items, PickerQuery query)
    {
        IEnumerable<PickerItem> kept = items.Where(item =>
            (!query.OnlyFree || !item.Equipped)
            && (query.Sets.Count == 0 || query.Sets.Contains(item.Set))
            && (query.Secondary.Count == 0 || query.Secondary.Contains(item.Secondary)));
        if (query.Sort.Count == 0)
            return [.. kept.OrderByDescending(item => item.Level).ThenByDescending(item => item.Rarity)];
        // The most chosen affixes first, then the highest value of each in the order they were chosen.
        IOrderedEnumerable<PickerItem> sorted = kept.OrderByDescending(item => query.Sort.Count(item.Affixes.ContainsKey));
        foreach (string affix in query.Sort)
            sorted = sorted.ThenByDescending(item => item.Affixes.GetValueOrDefault(affix));
        return [.. sorted.ThenByDescending(item => item.Level).ThenByDescending(item => item.Rarity)];
    }
}
