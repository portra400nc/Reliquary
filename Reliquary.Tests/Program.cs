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
Same(new Move(Skirk, Slot.Goblet, 100, 200, Furina), held.Moves.Single(), "a equipped item names its holder");

Same(0, Planner.Plan([Of(Skirk, (Slot.Goblet, 100))], gear).Moves.Count, "an item already equipped needs no move");
Same(false, Planner.Plan([Of(Skirk, (Slot.Goblet, 100))], gear).CanApply, "nothing to change cannot apply");

Plan partial = Planner.Plan([Of(Skirk, (Slot.Weapon, 500))], gear);
Same(Slot.Weapon, partial.Moves.Single().Slot, "an empty slot stays out of the plan");

Plan gone = Planner.Plan([Of(Skirk, (Slot.Goblet, 999), (Slot.Weapon, 500))], gear);
Same(new Missing(Skirk, Slot.Goblet, 999), gone.Missing.Single(), "an item no longer owned is reported");
Same(Slot.Weapon, gone.Moves.Single().Slot, "the other items still apply");

Plan clash = Planner.Plan([Of(Furina, (Slot.Goblet, 400)), Of(Kazuha, (Slot.Goblet, 400))], gear);
Same(new Clash(400, Furina, Kazuha), clash.Clashes.Single(), "two members needing one item clash");
Same(false, clash.CanApply, "a clash blocks applying");

// Skirk takes Furina's goblet, so Furina's own template must see Skirk's old goblet as what she has equipped then.
Plan chain = Planner.Plan([Of(Skirk, (Slot.Goblet, 200)), Of(Furina, (Slot.Goblet, 400))], gear);
Same(new Move(Furina, Slot.Goblet, 100, 400, null), chain.Moves[1], "a later move sees the earlier swap");

IReadOnlyList<Move> undo = Planner.Undo(held.Moves);
Same(new Move(Skirk, Slot.Goblet, 200, 100, null), undo.Single(), "undo equips the previous item again");

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

PickerItem Item(ulong guid, int level, uint set = 0, string secondary = "", bool equipped = false, params (string, float)[] affixes)
    => new(guid, level, 5, set, secondary, affixes.ToDictionary(a => a.Item1, a => a.Item2), equipped);
string Order(IEnumerable<PickerItem> items) => string.Join(",", items.Select(item => item.Guid));

var bag = new[]
{
    Item(1, 20, 100, affixes: [("CRIT DMG", 0.20f)]),
    Item(2, 20, 200, equipped: true, affixes: [("CRIT DMG", 0.10f), ("CRIT Rate", 0.05f)]),
    Item(3, 16, 100, affixes: [("CRIT Rate", 0.10f), ("ATK%", 0.04f)]),
    Item(4, 20, 300, affixes: [("HP", 300f)]),
};
Same("1,2,4,3", Order(Picking.Apply(bag, new PickerQuery())), "with nothing chosen items sort by level");

var bySet = new PickerQuery();
bySet.Sets.Add(100);
bySet.Sets.Add(300);
Same("1,4,3", Order(Picking.Apply(bag, bySet)), "several sets can be selected");

var onlyFree = new PickerQuery { OnlyFree = true };
Same("1,4,3", Order(Picking.Apply(bag, onlyFree)), "equipped items can be hidden");

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

var team = new[]
{
    new RosterEntry(1, 20, 4, 1, 12, false),
    new RosterEntry(2, 90, 5, 5, 1, false),
    new RosterEntry(3, 81, 4, 5, 13, true),
    new RosterEntry(4, 90, 4, 8, 11, false),
    new RosterEntry(5, 60, 5, 2, 1, true),
};
string Roster_(RosterQuery query) => string.Join(",", Roster.Apply(team, query).Select(entry => entry.Guid));
Same("3,5,2,4,1", Roster_(new RosterQuery()), "the party comes first in its own order, then level, then rarity");
Same("3,5,1,2,4", Roster_(new RosterQuery { Ascending = true }), "flipping the order keeps the party first and reverses the level order");
Same("3,5,2,4,1", Roster_(new RosterQuery { Sort = RosterSort.Rarity }), "the rarity sort puts five stars ahead");
Same("3,5,1,2,4", Roster_(new RosterQuery { Sort = RosterSort.Element }), "the element sort starts at the lowest element");
var cryo = new RosterQuery();
cryo.Elements.Add(5);
Same("3,2", Roster_(cryo), "the element filter keeps party members and others of that element");
var cryoSword = new RosterQuery();
cryoSword.Elements.Add(5);
cryoSword.Weapons.Add(1);
Same("2", Roster_(cryoSword), "element and weapon filters must both match");
cryoSword.Weapons.Add(13);
Same("3,2", Roster_(cryoSword), "several weapons can be selected");
var cycle = new RosterQuery();
cycle.NextSort();
cycle.NextSort();
cycle.NextSort();
Same("Level", cycle.Sort.ToString(), "the sort cycles back to level");
Same("False", new RosterQuery().Filtered.ToString(), "a new query filters nothing");
Same("True", cryo.Filtered.ToString(), "a chosen element counts as a filter");

// Scrolling: a 400-unit window over 1000 units of content.
string Near(float value) => MathF.Round(value).ToString();
ScrollState Fresh() { var state = new ScrollState(); state.Measure(400f, 1000f); return state; }
void Run(ScrollState state, float from, float seconds) { for (float t = from; t <= from + seconds; t += 1f / 60f) state.Step(t); }

var follow = Fresh();
follow.BeginDrag(500f, 0f);
follow.DragTo(380f, 0.1f);
Same("120", Near(follow.Offset), "the content follows the drag exactly");
follow.DragTo(500f, 0.2f);
follow.DragTo(450f, 0.3f);
follow.EndDrag(0.5f);
Run(follow, 0.5f, 1f);
Same("50", Near(follow.Offset), "a drag held still before release does not fling");

var flick = Fresh();
flick.BeginDrag(500f, 0f);
flick.DragTo(480f, 0.01f);
flick.DragTo(440f, 0.03f);
flick.EndDrag(0.035f);
Run(flick, 0.035f, 0.3f);
float coasting = flick.Offset;
Same("True", (coasting > 120f).ToString(), "a flick keeps going after release");
Run(flick, 0.4f, 4f);
Same("False", flick.Moving.ToString(), "a flick slows down and stops");
Same("True", (flick.Offset > coasting && flick.Offset <= flick.Max).ToString(), "it stops further on and inside the content");

var band = Fresh();
band.BeginDrag(500f, 0f);
band.DragTo(1500f, 0.1f);
Same("True", (band.Offset < 0f && band.Offset > -400f).ToString(), "pulling past the start stretches less than the pull");
band.EndDrag(0.4f);
Run(band, 0.4f, 1.5f);
Same("0", Near(band.Offset), "the stretch springs back to the start");

// The Deck can take 50 ms a frame: a fast release past the start must still settle at the start, not fly on.
var slow = Fresh();
slow.BeginDrag(500f, 0f);
slow.DragTo(560f, 0.05f);
slow.DragTo(700f, 0.1f);
slow.EndDrag(0.1f);
for (float t = 0.15f; t < 3f; t += 0.05f)
    slow.Step(t);
Same("0", Near(slow.Offset), "a fast pull past the start springs back at 20 frames a second");

var wheel = Fresh();
wheel.Wheel(100f, 0f);
wheel.Wheel(100f, 0.05f);
Run(wheel, 0.05f, 0.5f);
Same("200", Near(wheel.Offset), "wheel notches add up and ease there");
wheel.Wheel(5000f, 1f);
Run(wheel, 1f, 0.5f);
Same("600", Near(wheel.Offset), "the wheel stops at the end of the content");

var reveal = Fresh();
reveal.Reveal(700f, 760f, 50f, 0f, ease: false);
Same("410", Near(reveal.Offset), "a row below the window comes into view with its padding");
reveal.Reveal(500f, 560f, 50f, 0f, ease: false);
Same("410", Near(reveal.Offset), "a row already in view stays where it is");

Console.WriteLine($"Reliquary passed {checks} checks");

sealed class Fake(Dictionary<(ulong, Slot), ulong> equipped, HashSet<ulong> owned) : IGear
{
    public ulong Equipped(ulong avatar, Slot slot) => equipped.GetValueOrDefault((avatar, slot));
    public bool Owned(ulong item) => owned.Contains(item);
    public ulong? Holder(ulong item) => equipped.Where(pair => pair.Value == item).Select(pair => (ulong?)pair.Key.Item1).FirstOrDefault();
}
