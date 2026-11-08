using UnityEngine;
using UnityEngine.UI;
using static Reliquary.Ui.Widgets;
using Object = UnityEngine.Object;

namespace Reliquary.Ui;

// The panel's animations, as in the approved motion mockup (.work/reliquary-ui/motion). Each build draws its parts into
// named groups; afterwards the build is compared with the one before, and the groups that changed get a short tween.
// Highlights such as focus frames glide from where the previous build left them, and a layer that closes stays on
// screen as a ghost while it fades out.
internal sealed partial class Panel
{
    // What a build showed, for telling what changed since the build before.
    private sealed record Shot(Layer Layer, Tab Tab, int Avatar, int Template, bool InSlots, int Slot, string SlotItems,
        int Team, bool InMembers, int Member, int RosterFirst, string RosterOrder, int PickerFirst,
        string PickerOrder, int PickerCursor, string Marks, int SetFirst, int PanelCursor, string Toast, ToastKind Kind,
        int StripStart, bool Blurred);

    private const float RosterStep = CardHeight + CardGapY, PickerStep = 154f, SetStep = 45f;
    // The highlights that move with a page change, slowed on 2026-11-08 at the user's request.
    private const float TabMs = 320f, CharacterMs = 260f, ListMs = 260f;

    private readonly Motion motion = new();
    private readonly Dictionary<string, RectTransform> groups = [];
    private readonly Dictionary<string, RectTransform> glideRects = [];
    private readonly Dictionary<string, RectTransform> parts = [];
    private Dictionary<string, Vector4> glidesBefore = [];
    private Dictionary<string, Vector4> glides = [];
    private RectTransform stage;
    private Shot shot, was;
    private bool closing;

    // A layer of the build, drawn over the ones made before it.
    private RectTransform G(string name)
    {
        if (groups.TryGetValue(name, out RectTransform group) && group)
            return group;
        group = Node(name, stage);
        Fill(group);
        groups[name] = group;
        return group;
    }

    private RectTransform Sub(RectTransform parent, string name)
    {
        RectTransform group = Node(name, parent);
        Fill(group);
        groups[name] = group;
        return group;
    }

    // A window at (x, y, width, height) that hides what lies outside it, whose parts are still placed in stage units.
    private RectTransform Clip(RectTransform parent, string name, float x, float y, float width, float height)
    {
        RectTransform window = Node(name + "Window", parent);
        TopLeft(window, x, y, width, height);
        window.gameObject.AddComponent<RectMask2D>();
        RectTransform inner = Node(name, window);
        TopLeft(inner, -x, -y, Width, Style.ReferenceHeight);
        groups[name] = inner;
        return inner;
    }

    // A part the animations look up by name after the build.
    private T Part<T>(string name, T part) where T : Component
    {
        parts[name] = part.GetComponent<RectTransform>();
        return part;
    }

    // A highlight that glides from where the previous build drew the same key.
    private void Glide(string key, RectTransform rect, float x, float y, float width, float height, float ms)
    {
        var to = new Vector4 { x = x, y = y, z = width, w = height };
        glides[key] = to;
        glideRects[key] = rect;
        if (was != null && glidesBefore.TryGetValue(key, out Vector4 from) && !Same(from, to))
            motion.Glide(rect, from, to, ms);
    }

    // The focus frame: a white ring 3 units outside the marked part, on its rounding, with a warm glow that breathes.
    // It glides between the parts it marks.
    private RectTransform FocusFrame(string key, Transform parent, float x, float y, float width, float height, float radius = 0f)
    {
        float r = radius + 3f;
        RectTransform frame = Node("Focus", parent);
        TopLeft(frame, x - 3f, y - 3f, width + 6f, height + 6f);
        Image glow = Picture(frame, "Glow", Shapes.FocusGlow(r), Style.White);
        Place(glow.rectTransform, V(0f, 0f), V(1f, 1f), V(0.5f, 0.5f), V(0f, 0f), V(2f * Shapes.FocusGlowMargin, 2f * Shapes.FocusGlowMargin));
        focusGlows.Add(glow);
        Fill(Picture(frame, "Ring", Shapes.FocusRing(r), Style.White).rectTransform);
        Glide(key, frame, x - 3f, y - 3f, width + 6f, height + 6f, 120f);
        parts[key] = frame;
        return frame;
    }

    private readonly List<Image> focusGlows = [];
    private const float BreathSeconds = 1.8f;

    // The glow breathes between 35% and full strength, slowly, like the mockup's ease-in-out keyframes.
    private void Breathe(float now)
    {
        focusGlows.RemoveAll(glow => !glow);
        float strength = 0.35f + 0.65f * (0.5f - 0.5f * MathF.Cos(now * 2f * MathF.PI / BreathSeconds));
        foreach (Image glow in focusGlows)
            glow.color = Style.WithAlpha(Style.White, strength);
    }

    private static bool Same(Vector4 a, Vector4 b) => Math.Abs(a.x - b.x) < 0.01f && Math.Abs(a.y - b.y) < 0.01f && Math.Abs(a.z - b.z) < 0.01f && Math.Abs(a.w - b.w) < 0.01f;

    // Where each highlight is right now, which is mid-glide when a held button moves it again.
    private void RememberGlides()
    {
        glidesBefore = [];
        foreach (var (key, target) in glides)
        {
            RectTransform rect = glideRects.GetValueOrDefault(key);
            glidesBefore[key] = rect ? new Vector4 { x = rect.anchoredPosition.x, y = -rect.anchoredPosition.y, z = rect.sizeDelta.x, w = rect.sizeDelta.y } : target;
        }
        glides = [];
        glideRects.Clear();
    }

    // Moves a group of the build being replaced out of it, and lets it leave on its own before it is destroyed.
    // A layer the new one covers fades out under it (below); a layer that closes fades out on top.
    private void Ghost(string name, Action<RectTransform> leave, bool below = false)
    {
        if (!groups.TryGetValue(name, out RectTransform group) || !group)
            return;
        group.SetParent(root.transform, true);
        groups.Remove(name);
        (below ? ghostsBelow : ghosts).Add(group);
        leave(group);
    }

    private readonly List<RectTransform> ghosts = [];
    private readonly List<RectTransform> ghostsBelow = [];

    // After the build: ghosts that close go over everything, and covered ones go just under the new stage.
    private void PlaceGhosts()
    {
        ghosts.RemoveAll(ghost => !ghost);
        foreach (RectTransform ghost in ghosts)
            ghost.SetAsLastSibling();
        foreach (RectTransform ghost in ghostsBelow.Where(ghost => ghost))
        {
            ghost.SetParent(stage.parent, true);
            ghost.SetSiblingIndex(stage.GetSiblingIndex());
        }
        ghostsBelow.Clear();
    }

    private void FadeAway(RectTransform group, float ms, float dx = 0f, float dy = 0f)
    {
        if (dx != 0f || dy != 0f)
            motion.Shift(group, dx, dy, ms, away: true, easeIn: true);
        motion.Fade(group, 1f, 0f, ms, easeIn: true, done: () =>
        {
            if (group)
                Object.Destroy(group.gameObject);
        });
    }

    private Shot Take()
    {
        string marks = layer switch
        {
            Layer.WeaponFilter => string.Join(",", weaponQuery.Secondary.OrderBy(key => key)),
            Layer.ArtifactSort => string.Join(",", SortList()),
            Layer.ChooserFilter => string.Join(",", rosterQuery.Elements.OrderBy(e => e).Select(e => $"e{e}")) + "|" + string.Join(",", rosterQuery.Weapons.OrderBy(w => w).Select(w => $"w{w}")),
            Layer.SetFilter => string.Join(",", artifactQuery.Sets.OrderBy(id => id)),
            _ => "",
        };
        return new Shot(layer, tab, avatar, template, inSlots, slotIndex,
            Selected == null ? "" : string.Join(",", SlotOrder.Select(slot => Selected.Items.GetValueOrDefault(slot))),
            team, inMembers, member, RosterFirstRow(), string.Join(",", roster.Take(40).Select(entry => entry.Guid)),
            PickerFirst(), string.Join(",", pickerItems.Take(40)), pickerCursor, marks, SetFirstRow(), panelCursor,
            toastUntil != 0f ? toast : "", toastKind, StripStart(), backdrop.Picture != null);
    }

    private static Layer Base(Layer layer) => layer switch
    {
        Layer.WeaponFilter or Layer.ArtifactSort => Layer.Picker,
        Layer.ChooserFilter => Layer.Chooser,
        Layer.Confirm or Layer.Menu or Layer.Rename or Layer.Delete => Layer.Main,
        _ => layer,
    };

    private static bool IsPanel(Layer layer) => layer is Layer.WeaponFilter or Layer.ArtifactSort or Layer.ChooserFilter;

    private static bool IsPrompt(Layer layer) => layer is Layer.Confirm or Layer.Menu or Layer.Rename or Layer.Delete;

    // Before the old build goes: the parts that leave keep playing as ghosts.
    private void LeaveMotion(Shot now)
    {
        if (was == null)
            return;
        if (IsPrompt(was.Layer) && was.Layer != now.Layer)
            Ghost("Prompt", group =>
            {
                if (groups.TryGetValue("Dialog", out RectTransform dialog) && dialog)
                    motion.Grow(dialog, Width / 2f, Style.ReferenceHeight / 2f, 1f, 0.98f, 120f, easeIn: true);
                FadeAway(group, 120f);
            });
        if (IsPanel(was.Layer) && !IsPanel(now.Layer))
        {
            Ghost("PanelDim", group => FadeAway(group, 160f));
            Ghost("Panel", group => FadeAway(group, 160f, -24f));
        }
        Layer from = Base(was.Layer), to = Base(now.Layer);
        if (from == Layer.Chooser && to != Layer.Chooser)
        {
            Ghost("ChooserDim", group => FadeAway(group, 140f));
            Ghost("Chooser", group => FadeAway(group, 140f, -24f));
        }
        if (from == Layer.Picker && to != Layer.Picker)
            Ghost("Picker", group => FadeAway(group, to == Layer.SetFilter ? 180f : 140f), below: to == Layer.SetFilter);
        if (from == Layer.SetFilter && to != Layer.SetFilter)
            Ghost("Sets", group => FadeAway(group, 140f));
        if (from == Layer.Main && to == Layer.Picker)
            foreach (string name in new[] { "Top", "Side", "Main", "Foot" })
                Ghost(name, group => FadeAway(group, 120f), below: true);
        if (was.Toast.Length > 0 && now.Toast.Length == 0)
            Ghost("Toast", group => FadeAway(group, 160f, 0f, 8f));
    }

    // After the new build is drawn: what arrives or changed moves in.
    private void ArriveMotion(Shot now)
    {
        Shot before = was;
        if (before == null)
        {
            OpenMotion();
            return;
        }
        if (!before.Blurred && now.Blurred && groups.TryGetValue("Back", out RectTransform back))
            motion.Fade(back, 0f, 1f, 220f);
        Layer from = Base(before.Layer), to = Base(now.Layer);
        if (IsPrompt(now.Layer) && before.Layer != now.Layer)
        {
            motion.Fade(G("Prompt"), 0f, 1f, 150f);
            if (groups.TryGetValue("Dialog", out RectTransform dialog))
                motion.Grow(dialog, Width / 2f, Style.ReferenceHeight / 2f, 0.96f, 1f, 180f);
        }
        if (IsPanel(now.Layer) && !IsPanel(before.Layer))
        {
            if (groups.TryGetValue("PanelDim", out RectTransform panelDim))
                motion.Fade(panelDim, 0f, 1f, 200f);
            motion.Fade(G("Panel"), 0f, 1f, 200f);
            motion.Shift(G("Panel"), -24f, 0f, 200f);
        }
        if (to == Layer.Chooser && from != Layer.Chooser)
        {
            if (groups.TryGetValue("ChooserDim", out RectTransform chooserDim))
                motion.Fade(chooserDim, 0f, 1f, 200f);
            motion.Fade(G("Chooser"), 0f, 1f, 200f);
            motion.Shift(G("Chooser"), -24f, 0f, 200f);
            for (int row = 0; row < RosterRows + 1; row++)
                if (groups.TryGetValue($"RosterRow{row}", out RectTransform cards))
                    motion.Fade(cards, 0f, 1f, 160f, 60f + row * 20f);
            if (parts.TryGetValue("frame:roster", out RectTransform frame))
                motion.Fade(frame, 0f, 1f, 160f, 80f);
        }
        if (to == Layer.Picker && from != Layer.Picker && from != Layer.SetFilter)
            PickerIn();
        if (to == Layer.Picker && from == Layer.SetFilter && before.PickerOrder != now.PickerOrder)
            ItemsIn();
        if (to == Layer.SetFilter && from != Layer.SetFilter)
        {
            motion.Fade(G("Sets"), 0f, 1f, 180f);
            foreach (string name in new[] { "SetRows", "SetRowsBack" })
                if (groups.TryGetValue(name, out RectTransform rows))
                    motion.Shift(rows, 0f, 8f, 180f);
            if (groups.TryGetValue("SetCard", out RectTransform card))
                motion.Shift(card, 0f, 8f, 180f, 30f);
        }
        if (to == Layer.Main && from == Layer.Picker)
        {
            foreach (string name in new[] { "Top", "Side", "Main", "Foot" })
                motion.Fade(G(name), 0f, 1f, 160f, 60f);
            Glow(before, now);
        }
        if (IsPanel(before.Layer) && !IsPanel(now.Layer) && before.PickerOrder != now.PickerOrder)
            ItemsIn();
        if (IsPanel(before.Layer) && !IsPanel(now.Layer) && now.Layer == Layer.Chooser && before.RosterOrder != now.RosterOrder && groups.TryGetValue("Roster", out RectTransform rosterGrid))
            motion.Fade(rosterGrid, 0f, 1f, 140f);
        if (from == to && from is Layer.Main or Layer.Chooser)
            PageMotion(before, now);
        if (from == to && from == Layer.Chooser)
            ListMotion("Roster", before.RosterFirst, now.RosterFirst, RosterStep, before.RosterOrder != now.RosterOrder);
        if (from == to && from == Layer.Picker)
        {
            // The item details change at once: the user had their cross-fade removed on 2026-11-08.
            ListMotion("Grid", before.PickerFirst, now.PickerFirst, PickerStep, false);
            ListMotion("GridHolders", before.PickerFirst, now.PickerFirst, PickerStep, false);
        }
        if (from == to && from == Layer.SetFilter)
        {
            ListMotion("SetRows", before.SetFirst, now.SetFirst, SetStep, false);
            ListMotion("SetRowsBack", before.SetFirst, now.SetFirst, SetStep, false);
        }
        if (before.Layer == now.Layer && before.Marks != now.Marks)
            PopMarks(before.Marks, now.Marks);
        ToastMotion(before, now);
    }

    private void OpenMotion()
    {
        motion.Fade(G("Tint"), 0f, 1f, 220f);
        if (groups.TryGetValue("Back", out RectTransform back))
            motion.Fade(back, 0f, 1f, 220f);
        motion.Fade(G("Top"), 0f, 1f, 180f);
        motion.Shift(G("Top"), 0f, -8f, 180f);
        motion.Fade(G("Side"), 0f, 1f, 220f, 40f);
        motion.Shift(G("Side"), -16f, 0f, 220f, 40f);
        motion.Fade(G("Main"), 0f, 1f, 220f, 80f);
        motion.Shift(G("Main"), 0f, 12f, 220f, 80f);
        for (int i = 0; i < 6; i++)
            if (groups.TryGetValue($"Card{i}", out RectTransform card))
                motion.Fade(card, 0f, 1f, 220f, 80f + i * 30f);
        motion.Fade(G("Foot"), 0f, 1f, 180f, 120f);
    }

    // The main screen: tabs, the character strip, the lists in the sidebar and the cards beside them.
    // Only the highlights move here: on 2026-11-08 the user had the page fades for tabs, characters and templates removed.
    private void PageMotion(Shot before, Shot now)
    {
        if (before.Tab != now.Tab)
            return;
        if (before.Avatar != now.Avatar)
        {
            if (before.StripStart != now.StripStart && Math.Abs(before.StripStart - now.StripStart) == 1 && groups.TryGetValue("Faces", out RectTransform faces))
                motion.Shift(faces, (now.StripStart - before.StripStart) * FaceStep, 0f, CharacterMs);
            return;
        }
        if (now.Tab == Tab.Characters && !before.InSlots && now.InSlots && parts.TryGetValue("frame:slot", out RectTransform slotFrame))
            motion.Fade(slotFrame, 0f, 1f, 120f);
        if (now.Tab == Tab.Teams && !before.InMembers && now.InMembers && parts.TryGetValue("frame:member", out RectTransform memberFrame))
            motion.Fade(memberFrame, 0f, 1f, 120f);
    }

    // A grid that moved by a row slides into place, and one that changed order fades back in.
    private void ListMotion(string name, int beforeFirst, int nowFirst, float step, bool reordered)
    {
        if (!groups.TryGetValue(name, out RectTransform grid))
            return;
        if (reordered)
            motion.Fade(grid, 0f, 1f, 140f);
        else if (beforeFirst != nowFirst && Math.Abs(beforeFirst - nowFirst) == 1)
            motion.Shift(grid, 0f, (nowFirst - beforeFirst) * step, 140f);
    }

    private void PickerIn()
    {
        motion.Fade(G("Picker"), 0f, 1f, 160f);
        for (int row = 0; row < PickerRows + 2; row++)
            foreach (string name in new[] { $"PickerRow{row}", $"Holders{row}" })
                if (groups.TryGetValue(name, out RectTransform cards))
                {
                    motion.Fade(cards, 0f, 1f, 220f, 60f + row * 30f);
                    motion.Shift(cards, 0f, 10f, 220f, 60f + row * 30f);
                }
        if (parts.TryGetValue("frame:picker", out RectTransform frame))
            motion.Fade(frame, 0f, 1f, 160f, 120f);
        if (groups.TryGetValue("Detail", out RectTransform detail))
        {
            motion.Fade(detail, 0f, 1f, 220f, 60f);
            motion.Shift(detail, 16f, 0f, 220f, 60f);
        }
    }

    private void ItemsIn()
    {
        for (int row = 0; row < PickerRows + 2; row++)
            foreach (string name in new[] { $"PickerRow{row}", $"Holders{row}" })
                if (groups.TryGetValue(name, out RectTransform cards))
                    motion.Fade(cards, 0f, 1f, 140f, 80f + row * 20f);
    }

    // The slot that just got a new item glows cream for a moment.
    private void Glow(Shot before, Shot now)
    {
        if (before.SlotItems == now.SlotItems || now.Tab != Tab.Characters)
            return;
        string[] a = before.SlotItems.Split(','), b = now.SlotItems.Split(',');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i] && slotRects.TryGetValue(i, out Vector4 slot) && groups.TryGetValue($"Card{i}", out RectTransform card))
            {
                RectTransform glow = Frame(card, slot.x, slot.y, slot.z, slot.w, Style.Cream);
                motion.Fade(glow, 1f, 0f, 600f, 160f, done: () =>
                {
                    if (glow)
                        Object.Destroy(glow.gameObject);
                });
            }
    }

    private readonly Dictionary<int, Vector4> slotRects = [];
    private readonly Dictionary<string, RectTransform> marks = [];

    // A row whose tick changed pops its circle.
    private void PopMarks(string before, string now)
    {
        var a = before.Split(',', '|').ToHashSet();
        var b = now.Split(',', '|').ToHashSet();
        foreach (string key in a.Union(b))
            if (key.Length > 0 && (a.Contains(key) != b.Contains(key) || SortPlaceChanged(before, now, key)) && marks.TryGetValue(key, out RectTransform radio) && radio)
                motion.Pop(radio, 160f);
    }

    private static bool SortPlaceChanged(string before, string now, string key) => Array.IndexOf(before.Split(','), key) != Array.IndexOf(now.Split(','), key);

    private void ToastMotion(Shot before, Shot now)
    {
        if (now.Toast.Length == 0 || !groups.TryGetValue("Toast", out RectTransform group))
            return;
        if (before.Toast.Length == 0)
        {
            motion.Fade(group, 0f, 1f, 200f);
            motion.Shift(group, 0f, 16f, 200f);
            return;
        }
        if (before.Kind != now.Kind && now.Kind is ToastKind.Done or ToastKind.Warn && parts.TryGetValue("ToastIcon", out RectTransform icon))
            motion.Pop(icon, 160f, iconAt.X, iconAt.Y);
        if (parts.TryGetValue("Undo", out RectTransform undoButton) && before.Kind == ToastKind.Busy && now.Kind != ToastKind.Busy)
            motion.Fade(undoButton, 0f, 1f, 160f);
    }

    // Closing fades the whole panel out before it goes.
    private void CloseMotion(Action done)
    {
        motion.FinishAll();
        closing = true;
        if (!content)
        {
            done();
            return;
        }
        RectTransform everything = content.GetComponent<RectTransform>();
        motion.Fade(everything, 1f, 0f, 160f, easeIn: true, done: done);
        foreach (RectTransform ghost in ghosts.Where(ghost => ghost))
            motion.Fade(ghost, 1f, 0f, 120f, easeIn: true);
    }
}
