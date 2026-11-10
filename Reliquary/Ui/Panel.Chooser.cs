using UnityEngine;
using UnityEngine.UI;
using static Reliquary.Ui.Widgets;

namespace Reliquary.Ui;

// The list of all characters: the game's own list on the left, over the normal Characters tab, which follows the focus.
internal sealed partial class Panel
{
    private const int RosterColumns = 5, RosterRows = 7, PortraitSize = 128;
    private const float RosterLeft = 72f, RosterTop = 83f, CardWidth = 70f, CardHeight = 86f, CardGapX = 7f, CardGapY = 6f, RosterWidth = 470f;
    private const float TrackX = 459f, TrackHeight = 638f, ControlsY = 730f;

    private static readonly (int Value, string Label)[] ElementRows = [(1, "Pyro"), (2, "Hydro"), (7, "Anemo"), (4, "Electro"), (3, "Dendro"), (5, "Cryo"), (8, "Geo")];
    private static readonly (int Value, string Label)[] WeaponRows = [(1, "Sword"), (11, "Claymore"), (13, "Polearm"), (12, "Bow"), (10, "Catalyst")];
    private static readonly Color RosterFill = Style.Hex(0x151c2a, 0.97f);
    private static readonly Color RosterDim = Style.Hex(0x080c16, 0.4f);
    private static readonly Color PartyBar = Style.Hex(0xb9e45c);

    private readonly RosterQuery rosterQuery = new();
    private readonly Dictionary<string, RawImage> pendingPortraits = [];
    private List<RosterEntry> roster = [];
    private int rosterCursor;
    private (int Avatar, int Template, int Slot, bool InSlots) chooserReturn;
    private (int[] Elements, int[] Weapons) savedRosterFilter;

    private void OpenChooser()
    {
        chooserReturn = (avatar, template, slotIndex, inSlots);
        FillRoster();
        layer = Layer.Chooser;
        dirty = true;
    }

    // Keeps the focus on the same character when they are still listed, and shows whoever is focused behind the list.
    private void FillRoster()
    {
        ulong keep = Current.Guid;
        roster = Roster.Apply(gear.Characters.Select(who => new RosterEntry(who.Guid, who.Level, who.Rarity, who.Element, who.Weapon, gear.Party.Contains(who.Guid))), rosterQuery);
        rosterCursor = Math.Max(0, roster.FindIndex(entry => entry.Guid == keep));
        rosterTop = null;
        ShowFocused();
    }

    private void ShowFocused()
    {
        dirty = true;
        if (rosterCursor >= roster.Count)
            return;
        ulong guid = roster[rosterCursor].Guid;
        if (guid == Current.Guid)
            return;
        avatar = gear.Characters.ToList().FindIndex(who => who.Guid == guid);
        template = 0;
        inSlots = false;
    }

    private void MoveRoster(int by)
    {
        if (roster.Count == 0)
            return;
        rosterCursor = Math.Clamp(rosterCursor + by, 0, roster.Count - 1);
        rosterTop = null;
        ShowFocused();
    }

    private void ChooseCharacter()
    {
        if (rosterCursor >= roster.Count)
            return;
        if (roster[rosterCursor].Guid == gear.Characters[chooserReturn.Avatar].Guid)
            LeaveChooser();
        else
            CloseChooser();
    }

    private void LeaveChooser()
    {
        (avatar, template, slotIndex, inSlots) = chooserReturn;
        CloseChooser();
    }

    private void CloseChooser()
    {
        pendingPortraits.Clear();
        Back();
    }

    private void NextRosterSort()
    {
        rosterQuery.NextSort();
        FillRoster();
    }

    private void FlipRosterOrder()
    {
        rosterQuery.Ascending = !rosterQuery.Ascending;
        FillRoster();
    }

    private void OpenRosterFilter()
    {
        savedRosterFilter = ([.. rosterQuery.Elements], [.. rosterQuery.Weapons]);
        panelCursor = 0;
        layer = Layer.ChooserFilter;
        dirty = true;
    }

    private void ToggleRosterFilter(int row)
    {
        HashSet<int> chosen = row < ElementRows.Length ? rosterQuery.Elements : rosterQuery.Weapons;
        int value = row < ElementRows.Length ? ElementRows[row].Value : WeaponRows[row - ElementRows.Length].Value;
        if (!chosen.Remove(value))
            chosen.Add(value);
        FillRoster();
    }

    private void ResetRosterFilter()
    {
        rosterQuery.Elements.Clear();
        rosterQuery.Weapons.Clear();
    }

    private void ChooserInput(float now)
    {
        if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
            MoveRoster(-1);
        else if (Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
            MoveRoster(1);
        else if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MoveRoster(-RosterColumns);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MoveRoster(RosterColumns);
        else if (Pressed(PadButtons.Y, KeyCode.N))
            OpenRosterFilter();
        else if (Pressed(PadButtons.X, KeyCode.F))
            NextRosterSort();
        else if (Pressed(PadButtons.Menu, KeyCode.R))
            FlipRosterOrder();
        else if (Pressed(PadButtons.A, KeyCode.Return))
            ChooseCharacter();
        else if (Pressed(PadButtons.B, KeyCode.Escape))
            LeaveChooser();
    }

    private void ChooserFilterInput(float now)
    {
        int rows = ElementRows.Length + WeaponRows.Length;
        if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MovePanel(-1, rows);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MovePanel(1, rows);
        else if (Pressed(PadButtons.A, KeyCode.Return))
            ToggleRosterFilter(panelCursor);
        else
            PanelActions(ResetRosterFilter, () =>
            {
                ResetRosterFilter();
                rosterQuery.Elements.UnionWith(savedRosterFilter.Elements);
                rosterQuery.Weapons.UnionWith(savedRosterFilter.Weapons);
            });
    }

    // One portrait a frame, set straight on its card, so the first open does not stall on loading them all.
    private void LoadOnePortrait()
    {
        if (pendingPortraits.Count == 0)
            return;
        (string icon, RawImage portrait) = pendingPortraits.First();
        pendingPortraits.Remove(icon);
        if (!portrait)
            return;
        portrait.texture = icons.Small(icon, PortraitSize);
        portrait.enabled = portrait.texture;
    }

    private void DrawChooser(RectTransform parent)
    {
        pendingPortraits.Clear();
        Cover(G("ChooserDim"), RosterDim);
        parent.SetAsLastSibling();
        LeftBand(parent, 0f, RosterWidth, Style.ReferenceHeight, RosterFill);
        Print(parent, $"Characters / {Current.Name}", RosterLeft, 30f, 380f, 24f, 14, Style.Cream, TextAnchor.MiddleLeft);
        if (roster.Count == 0)
            Print(parent, "No characters match", RosterLeft, 340f, 385f, 24f, 16, Style.Dim, TextAnchor.MiddleCenter);
        int lastRow = (Math.Max(roster.Count, 1) - 1) / RosterColumns;
        int firstRow = RosterFirstRow();
        int first = firstRow * RosterColumns;
        // A row that scrolled out of sight is drawn once more, so it can slide away under the window's edge.
        int leaving = was != null && Base(was.Layer) == Layer.Chooser && Math.Abs(was.RosterFirst - firstRow) == 1 ? was.RosterFirst - firstRow : 0;
        RectTransform grid = Clip(parent, "Roster", 0f, RosterTop - 2f, RosterWidth - 14f, RosterRows * (CardHeight + CardGapY) + 2f);
        Scroll(parent, 0f, RosterTop - 2f, RosterWidth - 14f, RosterRows * (CardHeight + CardGapY) + 2f, CardHeight + CardGapY, ScrollRoster);
        for (int localRow = leaving < 0 ? -1 : 0; localRow < RosterRows + (leaving > 0 ? 1 : 0); localRow++)
        {
            RectTransform cards = Sub(grid, $"RosterRow{localRow + 1}");
            for (int column = 0; column < RosterColumns; column++)
            {
                int i = first + localRow * RosterColumns + column;
                if (i < 0 || i >= roster.Count)
                    continue;
                float x = RosterLeft + column * (CardWidth + CardGapX), y = RosterTop + localRow * (CardHeight + CardGapY);
                RosterCard(cards, roster[i], x, y, false);
                int target = i;
                if (localRow >= 0 && localRow < RosterRows)
                    Hit(cards, x, y, CardWidth, CardHeight, () =>
                    {
                        rosterCursor = target;
                        ShowFocused();
                        ChooseCharacter();
                    });
            }
        }
        if (rosterCursor >= first && rosterCursor < first + RosterColumns * RosterRows)
        {
            int local = rosterCursor - first;
            FocusFrame("frame:roster", parent, RosterLeft + (local % RosterColumns) * (CardWidth + CardGapX), RosterTop + (local / RosterColumns) * (CardHeight + CardGapY), CardWidth, CardHeight, 3f);
        }
        if (lastRow >= RosterRows)
        {
            float rows = lastRow + 1;
            Box(parent, TrackX, RosterTop, 4f, TrackHeight, Style.Hex(0xece5d8, 0.14f), Shapes.Bar);
            Image thumb = Box(parent, TrackX, RosterTop + firstRow / rows * TrackHeight, 4f, RosterRows / rows * TrackHeight, Style.Hex(0xece5d8, 0.6f), Shapes.Bar);
            Glide("thumb", thumb.rectTransform, TrackX, RosterTop + firstRow / rows * TrackHeight, 4f, RosterRows / rows * TrackHeight, 140f);
        }
        DrawRosterControls(parent);
        if (padLayout)
        {
            Mark(parent, "A", 1010f + wide, 756f);
            Print(parent, "Open", 1040f + wide, 758f, 120f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "B", 1170f + wide, 756f);
            Print(parent, "Leave", 1200f + wide, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        }
        else
            EscHint(parent, "Leave");
    }

    // The first row of the list in view, which keeps the cursor off the window's top row.
    private int RosterFirstRow()
    {
        int last = Math.Max(0, (Math.Max(roster.Count, 1) - 1) / RosterColumns - RosterRows + 1);
        return rosterTop is { } top ? Math.Clamp(top, 0, last) : Math.Max(0, Math.Min(rosterCursor / RosterColumns - 1, last));
    }

    // Art on a rarity plate, with the element in the top left, the level on a light strip and lime edges on a party member.
    private void RosterCard(Transform parent, RosterEntry entry, float x, float y, bool focus)
    {
        Character who = gear.Characters.First(character => character.Guid == entry.Guid);
        Box(parent, x, y, CardWidth, CardHeight, Style.Strip, Shapes.RosterPlate);
        Image art = Picture(parent, "Art", Shapes.RosterArt[Math.Clamp(entry.Rarity, 0, Shapes.RosterArt.Length - 1)], Style.White);
        art.type = Image.Type.Simple;
        TopLeft(art.rectTransform, x, y, CardWidth, CardWidth);
        var portrait = Node("Portrait", parent).gameObject.AddComponent<RawImage>();
        portrait.raycastTarget = false;
        TopLeft(portrait.rectTransform, x, y, CardWidth, CardWidth);
        if (icons.HasSmall(who.Icon, PortraitSize))
        {
            portrait.texture = icons.Small(who.Icon, PortraitSize);
            portrait.enabled = portrait.texture;
        }
        else
        {
            portrait.enabled = false;
            pendingPortraits[who.Icon] = portrait;
        }
        if (icons.Element(entry.Element) is { } element)
        {
            Image icon = Picture(parent, "Element", null, Style.White);
            icon.sprite = element;
            icon.preserveAspect = true;
            TopLeft(icon.rectTransform, x + 2f, y + 2f, 18f, 18f);
        }
        Print(parent, $"Lv. {entry.Level}", x, y + CardWidth, CardWidth, CardHeight - CardWidth, 10, Style.StripInk, TextAnchor.MiddleCenter);
        if (entry.InParty)
        {
            Box(parent, x, y - 1f, CardWidth, 2f, PartyBar, null);
            Box(parent, x, y + CardHeight - 1f, CardWidth, 2f, PartyBar, null);
        }
        if (focus)
            FocusFrame("frame:roster", parent, x, y, CardWidth, CardHeight, 3f);
    }

    private void DrawRosterControls(RectTransform parent)
    {
        const float End = RosterLeft + 385f;
        float x = RosterLeft;
        if (padLayout)
        {
            Mark(parent, "Y", x, ControlsY + 3f);
            x += 28f;
        }
        Image funnel = Picture(parent, "Funnel", Shapes.Funnel, rosterQuery.Filtered ? Style.Cream : Style.Text);
        TopLeft(funnel.rectTransform, x, ControlsY + 7f, 16f, 16f);
        Action filter = OpenRosterFilter, sort = NextRosterSort, flip = FlipRosterOrder;
        Hit(parent, RosterLeft, ControlsY - 2f, x + 16f - RosterLeft, 34f, filter);
        x += 28f;
        float orderX = End - 16f - (padLayout ? 32f : 4f);
        float pillWidth = orderX - 12f - x;
        Box(parent, x, ControlsY + 1f, pillWidth, 28f, Paper, Shapes.Pill);
        Print(parent, rosterQuery.Sort.ToString(), x + 14f, ControlsY + 1f, pillWidth - 40f, 28f, 14, PaperInk, TextAnchor.MiddleLeft);
        if (padLayout)
            Mark(parent, "X", x + pillWidth - 27f, ControlsY + 3f);
        Hit(parent, x, ControlsY - 2f, pillWidth, 34f, sort);
        Image order = Picture(parent, "Order", Shapes.SortOrder, rosterQuery.Ascending ? Style.Cream : Style.Text);
        TopLeft(order.rectTransform, orderX, ControlsY + 7f, 16f, 16f);
        if (padLayout)
            Mark(parent, "Menu", orderX + 22f, ControlsY + 3f);
        Hit(parent, orderX, ControlsY - 2f, End - orderX, 34f, flip);
    }

    private void DrawChooserFilter(RectTransform parent)
    {
        DrawPanelFrame(parent, "Characters filter", "Element", "");
        float y = 110f;
        for (int i = 0; i < ElementRows.Length; i++)
        {
            FilterRow(parent, ElementRows[i].Label, rosterQuery.Elements.Contains(ElementRows[i].Value), i, y);
            y += 44f;
        }
        Print(parent, "Weapon", 22f, y + 2f, 340f, 22f, 14, Style.Cream, TextAnchor.MiddleLeft);
        y += 28f;
        for (int i = 0; i < WeaponRows.Length; i++)
        {
            FilterRow(parent, WeaponRows[i].Label, rosterQuery.Weapons.Contains(WeaponRows[i].Value), ElementRows.Length + i, y);
            y += 44f;
        }
        DrawPanelFooter(parent, () =>
        {
            ResetRosterFilter();
            FillRoster();
        });
    }

    private void FilterRow(RectTransform parent, string label, bool chosen, int row, float y)
        => OptionRow(parent, 16f, y, 360f, label, chosen ? "" : null, "", false, row == panelCursor, row < ElementRows.Length ? $"e{ElementRows[row].Value}" : $"w{WeaponRows[row - ElementRows.Length].Value}", () =>
        {
            panelCursor = row;
            ToggleRosterFilter(row);
        });
}
