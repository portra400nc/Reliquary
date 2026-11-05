namespace Reliquary;

public enum RosterSort { Level, Rarity, Element }

public sealed record RosterEntry(ulong Guid, int Level, int Rarity, int Element, int Weapon, bool InParty);

public sealed class RosterQuery
{
    public HashSet<int> Elements { get; } = [];
    public HashSet<int> Weapons { get; } = [];
    public RosterSort Sort { get; set; }
    public bool Ascending { get; set; }

    public bool Filtered => Elements.Count > 0 || Weapons.Count > 0;

    public void NextSort() => Sort = (RosterSort)(((int)Sort + 1) % 3);
}

public static class Roster
{
    // The party stays first and in its own order, as in the game's list. The element sort starts at the lowest element.
    public static List<RosterEntry> Apply(IEnumerable<RosterEntry> entries, RosterQuery query)
    {
        List<RosterEntry> kept = [.. entries.Where(entry =>
            (query.Elements.Count == 0 || query.Elements.Contains(entry.Element))
            && (query.Weapons.Count == 0 || query.Weapons.Contains(entry.Weapon)))];
        Func<RosterEntry, int> key = query.Sort switch
        {
            RosterSort.Rarity => entry => entry.Rarity,
            RosterSort.Element => entry => -entry.Element,
            _ => entry => entry.Level,
        };
        IEnumerable<RosterEntry> others = kept.Where(entry => !entry.InParty);
        IOrderedEnumerable<RosterEntry> sorted = query.Ascending ? others.OrderBy(key) : others.OrderByDescending(key);
        return [.. kept.Where(entry => entry.InParty), .. sorted.ThenByDescending(entry => entry.Level).ThenByDescending(entry => entry.Rarity)];
    }
}
