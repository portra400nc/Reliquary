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

Console.WriteLine($"Reliquary passed {checks} checks");

sealed class Fake(Dictionary<(ulong, Slot), ulong> worn, HashSet<ulong> owned) : IGear
{
    public ulong Worn(ulong avatar, Slot slot) => worn.GetValueOrDefault((avatar, slot));
    public bool Owned(ulong item) => owned.Contains(item);
    public ulong? Holder(ulong item) => worn.Where(pair => pair.Value == item).Select(pair => (ulong?)pair.Key.Item1).FirstOrDefault();
}
