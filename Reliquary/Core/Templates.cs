using System.Text.Json;

namespace Reliquary;

public enum Slot { Flower = 1, Plume = 2, Sands = 3, Goblet = 4, Circlet = 5, Weapon = 6 }

// An empty slot is one the template leaves as it is when applied.
public sealed class CharacterTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ulong Avatar { get; set; }
    public string Name { get; set; } = "New template";
    public Dictionary<Slot, ulong> Items { get; set; } = [];
}

// A member without a template keeps the gear they wear.
public sealed class TeamMember
{
    public ulong Avatar { get; set; }
    public string Template { get; set; } = "";
}

// A team template names one character template per member, so a change to that template reaches every team using it.
public sealed class TeamTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New team";
    public List<TeamMember> Members { get; set; } = [];
}

public sealed class Library
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<CharacterTemplate> Characters { get; set; } = [];
    public List<TeamTemplate> Teams { get; set; } = [];

    public CharacterTemplate? Character(string id) => Characters.Find(template => template.Id == id);

    public IEnumerable<CharacterTemplate> Of(ulong avatar) => Characters.Where(template => template.Avatar == avatar);

    // A team keeps the member whose template goes away, and that member's gear is then left as it is.
    public void Delete(CharacterTemplate template)
    {
        Characters.Remove(template);
        foreach (TeamTemplate team in Teams)
            foreach (TeamMember member in team.Members.Where(member => member.Template == template.Id))
                member.Template = "";
    }

    public IEnumerable<CharacterTemplate> Templates(TeamTemplate team)
        => team.Members.Select(member => Character(member.Template)).OfType<CharacterTemplate>();

    public string Save() => JsonSerializer.Serialize(this, Options);

    public static Library Load(string json) => JsonSerializer.Deserialize<Library>(json, Options) ?? new Library();
}

// What the bag and the characters hold right now.
public interface IGear
{
    ulong Worn(ulong avatar, Slot slot);
    bool Owned(ulong item);
    ulong? Holder(ulong item);
}

// One wear request. Holder is who wears Item now, and that character receives From.
public readonly record struct Move(ulong Avatar, Slot Slot, ulong From, ulong Item, ulong? Holder);

public readonly record struct Clash(ulong Item, ulong First, ulong Second);

public readonly record struct Missing(ulong Avatar, Slot Slot, ulong Item);

public sealed record Plan(IReadOnlyList<Move> Moves, IReadOnlyList<Missing> Missing, IReadOnlyList<Clash> Clashes)
{
    public bool CanApply => Clashes.Count == 0 && Moves.Count > 0;
}

public static class Planner
{
    // Later moves see the swaps of earlier ones, so each Move's From and Holder are what the server will find.
    public static Plan Plan(IEnumerable<CharacterTemplate> templates, IGear gear)
    {
        var moves = new List<Move>();
        var missing = new List<Missing>();
        var clashes = new List<Clash>();
        var wanted = new Dictionary<ulong, ulong>();
        var worn = new Dictionary<(ulong, Slot), ulong>();
        var holder = new Dictionary<ulong, ulong?>();
        ulong WornNow(ulong avatar, Slot slot) => worn.TryGetValue((avatar, slot), out ulong item) ? item : gear.Worn(avatar, slot);
        ulong? HolderNow(ulong item) => holder.TryGetValue(item, out ulong? who) ? who : gear.Holder(item);

        foreach (CharacterTemplate template in templates)
            foreach (var (slot, item) in template.Items.OrderBy(pair => pair.Key))
            {
                if (item == 0)
                    continue;
                if (!gear.Owned(item))
                {
                    missing.Add(new Missing(template.Avatar, slot, item));
                    continue;
                }
                if (wanted.TryGetValue(item, out ulong first) && first != template.Avatar)
                {
                    clashes.Add(new Clash(item, first, template.Avatar));
                    continue;
                }
                wanted[item] = template.Avatar;
                ulong from = WornNow(template.Avatar, slot);
                if (from == item)
                    continue;
                ulong? other = HolderNow(item);
                moves.Add(new Move(template.Avatar, slot, from, item, other));
                worn[(template.Avatar, slot)] = item;
                holder[item] = template.Avatar;
                if (other is { } swapped)
                {
                    worn[(swapped, slot)] = from;
                    if (from != 0)
                        holder[from] = swapped;
                }
                else if (from != 0)
                    holder[from] = null;
            }
        return new Plan(moves, missing, clashes);
    }

    // Wearing each previous item in reverse order undoes the swaps. A slot that was empty stays changed, because the game
    // has no wear request for an empty slot.
    public static IReadOnlyList<Move> Undo(IEnumerable<Move> applied)
        => [.. applied.Reverse().Where(move => move.From != 0)
            .Select(move => new Move(move.Avatar, move.Slot, move.Item, move.From, null))];
}
