using UnityEngine;
using UnityEngine.UI;
using static Reliquary.Ui.Widgets;

namespace Reliquary.Ui;

// The prompts over the screen and the messages under it, in the filter panels' look: a dark panel with a cream edge, the
// title inside it over a hairline, and its own buttons. The screen's button hints hide while a prompt is open.
internal sealed partial class Panel
{
    private enum ToastKind { Info, Busy, Done, Warn }

    private static readonly Color Scrim = Style.Hex(0x060a14, 0.62f);
    private static readonly Color RowBand = Style.Hex(0xece5d8, 0.045f);
    private static readonly Color Hair = Style.Hex(0xd3bc8e, 0.22f);
    private static readonly Color OptionFill = Style.Hex(0x0a101c, 0.45f);
    private static readonly Color FieldFill = Style.Hex(0x050912, 0.55f);
    private static readonly Color FieldEdge = Style.Hex(0xd3bc8e, 0.3f);
    private static readonly Color Danger = Style.Hex(0xc4564a);
    private const int PromptLines = 8;
    private const float RowHeight = 58f, ItemIcon = 42f, ButtonHeight = 40f, FootHeight = 78f, ToastBottom = 718f;

    private ToastKind toastKind;
    private int deleteFocus;
    private int caretPhase = -1;
    private int shownDone = -1;
    private RectTransform spinner;

    private bool Prompting => layer is Layer.Confirm or Layer.Menu or Layer.Rename or Layer.Delete;

    private readonly record struct PromptBox(float X, float Y, float Width, float Height, float Top);

    private readonly record struct PromptButton(string Text, string Glyph, bool Primary, Action Click, bool Focus = false, Color? Fill = null);

    // Redraws what moves on its own: the caret, the applying count and the spinner.
    private void AnimatePrompts(float now)
    {
        if (layer == Layer.Rename && (int)(now * 2f) % 2 != caretPhase)
        {
            caretPhase = (int)(now * 2f) % 2;
            dirty = true;
        }
        if (applier.Busy && applier.Done != shownDone)
        {
            shownDone = applier.Done;
            dirty = true;
        }
        if (spinner)
            spinner.localEulerAngles = new Vector3 { x = 0f, y = 0f, z = -now * 360f };
    }

    // Dims the screen, takes every click outside the panel (outside runs on one), and draws the panel and its title.
    private PromptBox Prompt(RectTransform parent, float width, float height, string title, string sub, Action outside = null)
    {
        hits.Clear();
        Cover(parent, Scrim);
        Hit(parent, 0f, -extraY, Width, Style.ReferenceHeight + 2f * extraY, outside ?? (() => { }));
        float x = (Width - width) / 2f, y = (Style.ReferenceHeight - height) / 2f;
        Hit(parent, x, y, width, height, () => { });
        Box(parent, x, y, width, height, Style.White, Shapes.Dialog);
        Fit(parent, title, x + 28f, y + 20f, width - 56f, 28f, 20, Style.Text);
        float hair = y + 60f;
        if (sub.Length > 0)
        {
            Print(parent, sub, x + 28f, y + 50f, width - 56f, 18f, 13, Style.Dim, TextAnchor.MiddleLeft);
            hair = y + 78f;
        }
        Box(parent, x + 28f, hair, width - 56f, 1f, Hair, null);
        return new PromptBox(x, y, width, height, hair + 1f);
    }

    private static float PromptHeight(bool sub, float body) => (sub ? 79f : 61f) + body + FootHeight;

    // Buttons at the bottom right, with an optional note at the left.
    private void PromptButtons(RectTransform parent, PromptBox box, string note, Color noteInk, params PromptButton[] buttons)
    {
        float y = box.Y + box.Height - 22f - ButtonHeight, x = box.X + box.Width - 28f;
        foreach (PromptButton button in buttons.Reverse())
        {
            float width = Math.Max(168f, button.Text.Length * 9f + 70f);
            x -= width;
            if (button.Focus)
                Box(parent, x - 3f, y - 3f, width + 6f, ButtonHeight + 6f, Style.White, Shapes.PillRing);
            Button_(parent, button.Text, button.Glyph, x, y, width, ButtonHeight, button.Primary, button.Click, fill: button.Fill);
            x -= 12f;
        }
        if (note.Length > 0)
            Fit(parent, note, box.X + 28f, y + 11f, x - box.X - 36f, 18f, 12, noteInk);
    }

    private void DrawConfirm(RectTransform parent)
    {
        bool team = tab == Tab.Teams;
        int count = plan.Moves.Count, lines = Math.Min(count, PromptLines);
        int members = plan.Moves.Select(move => move.Avatar).Distinct().Count();
        string items = $"{count} item{(count == 1 ? "" : "s")} change{(count == 1 ? "s" : "")}";
        string sub = team ? $"{members} member{(members == 1 ? "" : "s")}, {items}" : items;
        float width = team ? 760f : 720f;
        PromptBox box = Prompt(parent, width, PromptHeight(true, 16f + lines * RowHeight), planTitle, sub);
        float left = box.X + 28f, label = team ? 96f : 78f;
        float column = (width - 56f - label - 22f - 30f) / 2f;
        float from = left + label + 10f, arrow = from + column + 10f, to = arrow + 32f;
        for (int i = 0; i < lines; i++)
        {
            Move move = plan.Moves[i];
            float y = box.Top + 8f + i * RowHeight;
            if (i % 2 == 0)
                Box(parent, box.X + 18f, y, width - 36f, RowHeight, RowBand, null);
            if (team)
            {
                if (gear.Characters.FirstOrDefault(c => c.Guid == move.Avatar) is { } who)
                    Face(parent, who, left, y + 14f, 30f, false);
                Fit(parent, gear.NameOf(move.Avatar), left + 38f, y + 12f, label - 38f, 18f, 13, Style.Text);
                Print(parent, move.Slot.ToString(), left + 38f, y + 30f, label - 38f, 16f, 11, Style.Dim, TextAnchor.MiddleLeft);
            }
            else
                Print(parent, move.Slot.ToString(), left, y, label, RowHeight, 13, Style.Dim, TextAnchor.MiddleLeft);
            string fromName = gear.View(move.From)?.Name ?? "Empty";
            PromptItem(parent, move.From, from, y, column, true, null);
            Print(parent, "›", arrow, y, 22f, RowHeight, 18, Style.Cream, TextAnchor.MiddleCenter);
            PromptItem(parent, move.Item, to, y, column, false, move.Holder is { } holder ? $"{gear.NameOf(holder)} gets {fromName}" : null);
        }
        var notes = new List<string>();
        if (plan.Missing.Count > 0)
            notes.Add($"{plan.Missing.Count} item{(plan.Missing.Count == 1 ? " is" : "s are")} no longer owned and {(plan.Missing.Count == 1 ? "is" : "are")} skipped");
        if (count > lines)
            notes.Add($"And {count - lines} more");
        Action back = Back, confirm = Confirm;
        PromptButtons(parent, box, string.Join(". ", notes), plan.Missing.Count > 0 ? Warn : Style.Dim,
            new PromptButton("Cancel", "B", false, back),
            new PromptButton(team ? "Apply team" : "Apply template", "A", true, confirm));
    }

    // An item's icon and name, two lines at most, with a gold note under it when there is one.
    private void PromptItem(RectTransform parent, ulong guid, float x, float y, float width, bool old, string note)
    {
        ItemView view = guid == 0 ? null : gear.View(guid);
        if (view != null)
            Icon_(parent, view, x, y + 8f, ItemIcon, old ? 0.7f : 1f);
        else
            Box(parent, x, y + 8f, ItemIcon, ItemIcon, Style.Band, Shapes.Card);
        Color ink = old ? Style.Dim : Style.Text;
        string name = view?.Name ?? "Empty";
        if (note == null)
        {
            Fit(parent, name, x + 52f, y + 12f, width - 52f, 34f, 13, ink);
            return;
        }
        Fit(parent, name, x + 52f, y + 5f, width - 52f, 32f, 13, ink);
        Fit(parent, note, x + 52f, y + 38f, width - 52f, 15f, 11, Warn);
    }

    private void DrawMenu(RectTransform parent)
    {
        string title = tab == Tab.Characters ? Selected?.Name ?? "" : CurrentTeam?.Name ?? "";
        string sub = tab == Tab.Characters ? Current.Name : "Team template";
        Action back = Back;
        PromptBox box = Prompt(parent, 380f, (sub.Length > 0 ? 79f : 61f) + 12f + 44f + 8f + 44f + 20f, title, sub, back);
        Action rename_ = StartRename, delete = AskDelete;
        MenuRow(parent, box, "Rename", 0, Style.Text, rename_);
        MenuRow(parent, box, "Delete", 1, Down, delete);
    }

    private void MenuRow(RectTransform parent, PromptBox box, string label, int index, Color ink, Action click)
    {
        float x = box.X + 18f, y = box.Top + 12f + index * 52f, width = box.Width - 36f;
        Box(parent, x, y, width, 44f, OptionFill, null);
        Print(parent, label, x + 16f, y, width - 32f, 44f, 15, ink, TextAnchor.MiddleLeft);
        if (index == menuIndex)
            Frame(parent, x, y, width, 44f);
        Hit(parent, x, y, width, 44f, () => { menuIndex = index; click(); });
    }

    private void AskDelete()
    {
        deleteFocus = 0;
        layer = Layer.Delete;
        dirty = true;
    }

    private void DeleteInput(float now)
    {
        if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now) || Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
        {
            deleteFocus = 1 - deleteFocus;
            dirty = true;
        }
        else if (Pressed(PadButtons.A, KeyCode.Return))
        {
            if (deleteFocus == 1) DeleteSelected(); else Back();
        }
        else if (Pressed(PadButtons.B, KeyCode.Escape))
            Back();
    }

    // Cancel starts focused and A presses the focused button, so a second A by accident deletes nothing.
    private void DrawDelete(RectTransform parent)
    {
        string name, body;
        if (tab == Tab.Characters && Selected is { } doomed)
        {
            string who = Current.Name;
            name = doomed.Name;
            body = $"{who}'s gear stays as it is.";
            if (library.Teams.Any(team => team.Members.Any(member => member.Template == doomed.Id)))
                body += $" Teams that use this template keep {who}'s equipped gear instead.";
        }
        else
        {
            name = CurrentTeam?.Name ?? "";
            body = "The party's gear stays as it is.";
        }
        PromptBox box = Prompt(parent, 500f, PromptHeight(false, 62f), $"Delete {name}?", "");
        Wrap(parent, body, box.X + 28f, box.Top + 14f, box.Width - 56f, 44f, 14, Style.Dim, TextAnchor.UpperLeft);
        Action back = Back, delete = DeleteSelected;
        PromptButtons(parent, box, "", Style.Dim,
            new PromptButton("Cancel", deleteFocus == 0 ? "A" : "B", false, back, deleteFocus == 0),
            new PromptButton("Delete", deleteFocus == 1 ? "A" : "", true, delete, deleteFocus == 1, Danger));
    }

    private void DrawRename(RectTransform parent)
    {
        PromptBox box = Prompt(parent, 520f, PromptHeight(false, 74f), tab == Tab.Characters ? "Rename template" : "Rename team", "");
        float x = box.X + 28f, y = box.Top + 18f, width = box.Width - 56f;
        Box(parent, x, y, width, 50f, FieldFill, null);
        Box(parent, x, y, width, 1f, FieldEdge, null);
        Box(parent, x, y, 1f, 50f, FieldEdge, null);
        Box(parent, x + width - 1f, y, 1f, 50f, FieldEdge, null);
        Box(parent, x, y + 48f, width, 2f, Style.Cream, null);
        Text field = Print(parent, rename + (caretPhase == 0 ? "<color=#d3bc8e>|</color>" : "<color=#d3bc8e00>|</color>"), x + 16f, y, width - 32f, 50f, 19, Style.Text, TextAnchor.MiddleLeft);
        field.supportRichText = true;
        Action back = Back, save = SaveRename;
        PromptButtons(parent, box, "", Style.Dim, new PromptButton("Cancel", "B", false, back), new PromptButton("Save", "A", true, save));
    }

    private void SaveRename()
    {
        if (rename.Trim().Length == 0)
            return;
        if (tab == Tab.Characters) Selected!.Name = rename.Trim(); else CurrentTeam!.Name = rename.Trim();
        Save();
        Back();
    }

    private void DrawToast(RectTransform parent)
    {
        bool busy = toastKind == ToastKind.Busy;
        string detail = busy && applier.Busy ? $"{applier.Done} of {applier.Total}" : toastDetail;
        bool offerUndo = undo.Count > 0 && !applier.Busy;
        bool icon = toastKind != ToastKind.Info;
        float textWidth = Math.Max(toast.Length * 8.5f, detail.Length * 7f);
        if (busy)
            textWidth = Math.Max(textWidth, 300f);
        float height = busy ? 64f : 58f;
        float width = Math.Max(420f, 16f + (icon ? 42f : 0f) + textWidth + (offerUndo ? 118f : 0f) + 16f);
        float x = (Width - width) / 2f, y = ToastBottom - height;
        Box(parent, x, y, width, height, Style.White, Shapes.Dialog);
        float textX = x + 16f + (icon ? 42f : 0f);
        float iconY = y + (height - 28f) / 2f;
        switch (toastKind)
        {
            case ToastKind.Busy:
                Image ring = Picture(parent, "Spinner", Shapes.Spinner, Style.Cream);
                spinner = ring.rectTransform;
                Place(spinner, V(0f, 1f), V(0f, 1f), V(0.5f, 0.5f), V(x + 16f + 14f, -(iconY + 14f)), V(22f, 22f));
                break;
            case ToastKind.Done:
                Box(parent, x + 16f, iconY, 28f, 28f, Style.WithAlpha(Up, 0.18f), Shapes.Knob);
                Image check = Picture(parent, "Check", Shapes.Check, Up);
                TopLeft(check.rectTransform, x + 22f, iconY + 6f, 16f, 16f);
                break;
            case ToastKind.Warn:
                Box(parent, x + 16f, iconY, 28f, 28f, Style.WithAlpha(Warn, 0.18f), Shapes.Knob);
                Print(parent, "!", x + 16f, iconY, 28f, 28f, 15, Warn, TextAnchor.MiddleCenter);
                break;
        }
        float right = x + width - 16f - (offerUndo ? 118f : 0f);
        float top = y + (busy ? 10f : detail.Length > 0 ? 11f : 18f);
        Fit(parent, toast, textX, top, right - textX, 22f, 15, Style.Text);
        if (detail.Length > 0)
            Fit(parent, detail, textX, top + 21f, right - textX, 16f, 12, Style.Dim);
        if (busy && applier.Total > 0)
        {
            Box(parent, textX, y + 50f, right - textX, 3f, Style.Band, null);
            Box(parent, textX, y + 50f, (right - textX) * applier.Done / applier.Total, 3f, Style.Cream, null);
        }
        if (offerUndo)
        {
            Action undo_ = Undo;
            Button_(parent, "Undo", "Y", x + width - 16f - 104f, y + (height - 34f) / 2f, 104f, 34f, false, undo_);
        }
    }
}
