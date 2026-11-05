using Reliquary;

int checks = 0;
void Same<T>(T expected, T actual, string what)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{what}: expected {expected}, got {actual}");
}

const ulong Skirk = 1, Furina = 2, Kazuha = 3;
var gear = new Fake(new() { [(Skirk, Slot.Goblet)] = 100, [(Furina, Slot.Goblet)] = 200, [(Skirk, Slot.Weapon)] = 300 }, [100, 200, 300, 400, 500]);
CharacterTemplate Of(ulong avatar, params (Slot, ulong)[] items) => new() { Avatar = avatar, Items = items.ToDictionary(i => i.Item1, i => i.Item2) };

Plan free = Planner.Plan([Of(Skirk, (Slot.Goblet, 400))], gear);
Same(new Move(Skirk, Slot.Goblet, 100, 400, null), free.Moves.Single(), "a free item moves without a holder");

Plan held = Planner.Plan([Of(Skirk, (Slot.Goblet, 200))], gear);
Same(new Move(Skirk, Slot.Goblet, 100, 200, Furina), held.Moves.Single(), "a worn item names its holder");

Same(0, Planner.Plan([Of(Skirk, (Slot.Goblet, 100))], gear).Moves.Count, "an item already worn needs no move");
Same(false, Planner.Plan([Of(Skirk, (Slot.Goblet, 100))], gear).CanApply, "nothing to change cannot apply");

Plan partial = Planner.Plan([Of(Skirk, (Slot.Weapon, 500))], gear);
Same(Slot.Weapon, partial.Moves.Single().Slot, "an empty slot stays out of the plan");

Plan gone = Planner.Plan([Of(Skirk, (Slot.Goblet, 999), (Slot.Weapon, 500))], gear);
Same(new Missing(Skirk, Slot.Goblet, 999), gone.Missing.Single(), "an item no longer owned is reported");
Same(Slot.Weapon, gone.Moves.Single().Slot, "the other items still apply");

Plan clash = Planner.Plan([Of(Furina, (Slot.Goblet, 400)), Of(Kazuha, (Slot.Goblet, 400))], gear);
Same(new Clash(400, Furina, Kazuha), clash.Clashes.Single(), "two members needing one item clash");
Same(false, clash.CanApply, "a clash blocks applying");

// Skirk takes Furina's goblet, so Furina's own template must see Skirk's old goblet as what she wears then.
Plan chain = Planner.Plan([Of(Skirk, (Slot.Goblet, 200)), Of(Furina, (Slot.Goblet, 400))], gear);
Same(new Move(Furina, Slot.Goblet, 100, 400, null), chain.Moves[1], "a later move sees the earlier swap");

IReadOnlyList<Move> undo = Planner.Undo(held.Moves);
Same(new Move(Skirk, Slot.Goblet, 200, 100, null), undo.Single(), "undo wears the previous item again");

var empty = new Fake(new() { [(Kazuha, Slot.Flower)] = 0 }, [400]);
Same(0, Planner.Undo(Planner.Plan([Of(Kazuha, (Slot.Flower, 400))], empty).Moves).Count, "undo cannot empty a slot");

var library = new Library();
var skirk = Of(Skirk, (Slot.Goblet, 400));
library.Characters.Add(skirk);
library.Teams.Add(new TeamTemplate { Members = [new TeamMember { Avatar = Skirk, Template = skirk.Id }] });
Library loaded = Library.Load(library.Save());
Same(400ul, loaded.Characters.Single().Items[Slot.Goblet], "a saved template keeps its items");
Same(skirk.Id, loaded.Teams.Single().Members.Single().Template, "a saved team keeps its members");
Same(400ul, loaded.Templates(loaded.Teams.Single()).Single().Items[Slot.Goblet], "a team finds its members' templates");
loaded.Delete(loaded.Characters.Single());
Same("", loaded.Teams.Single().Members.Single().Template, "deleting a template clears it from teams");
Same(Skirk, loaded.Teams.Single().Members.Single().Avatar, "the team keeps that member");

PickerItem Item(ulong guid, int level, uint set = 0, string secondary = "", bool worn = false, params (string, float)[] affixes)
    => new(guid, level, 5, set, secondary, affixes.ToDictionary(a => a.Item1, a => a.Item2), worn);
string Order(IEnumerable<PickerItem> items) => string.Join(",", items.Select(item => item.Guid));

var bag = new[]
{
    Item(1, 20, 100, affixes: [("CRIT DMG", 0.20f)]),
    Item(2, 20, 200, worn: true, affixes: [("CRIT DMG", 0.10f), ("CRIT Rate", 0.05f)]),
    Item(3, 16, 100, affixes: [("CRIT Rate", 0.10f), ("ATK%", 0.04f)]),
    Item(4, 20, 300, affixes: [("HP", 300f)]),
};
Same("1,2,4,3", Order(Picking.Apply(bag, new PickerQuery())), "with nothing chosen items sort by level");

var bySet = new PickerQuery();
bySet.Sets.Add(100);
bySet.Sets.Add(300);
Same("1,4,3", Order(Picking.Apply(bag, bySet)), "several sets can be selected");

var onlyFree = new PickerQuery { OnlyFree = true };
Same("1,4,3", Order(Picking.Apply(bag, onlyFree)), "worn items can be hidden");

var critDmg = new PickerQuery();
critDmg.ToggleSort("CRIT DMG");
Same("1,2,4,3", Order(Picking.Apply(bag, critDmg)), "the chosen affix sorts by value, and items without it follow by level");

var both = new PickerQuery();
both.ToggleSort("CRIT DMG");
both.ToggleSort("CRIT Rate");
Same("2,1,3,4", Order(Picking.Apply(bag, both)), "items with the most chosen affixes come first");

var limit = new PickerQuery();
Same(true, limit.ToggleSort("A") && limit.ToggleSort("B") && limit.ToggleSort("C"), "three affixes can be chosen");
Same(false, limit.ToggleSort("D"), "a fourth affix is refused");
Same(true, limit.ToggleSort("B"), "choosing an affix again removes it");
Same("A,C", string.Join(",", limit.Sort), "the others keep their order");

var weapons = new[] { Item(5, 90, secondary: "CRIT DMG"), Item(6, 90, secondary: "ATK%"), Item(7, 1, secondary: "CRIT DMG") };
var bySecondary = new PickerQuery();
bySecondary.Secondary.Add("CRIT DMG");
Same("5,7", Order(Picking.Apply(weapons, bySecondary)), "weapons filter by secondary attribute");

Console.WriteLine($"Reliquary passed {checks} checks");

sealed class Fake(Dictionary<(ulong, Slot), ulong> worn, HashSet<ulong> owned) : IGear
{
    public ulong Worn(ulong avatar, Slot slot) => worn.GetValueOrDefault((avatar, slot));
    public bool Owned(ulong item) => owned.Contains(item);
    public ulong? Holder(ulong item) => worn.Where(pair => pair.Value == item).Select(pair => (ulong?)pair.Key.Item1).FirstOrDefault();
}
