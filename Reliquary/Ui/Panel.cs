using System.Text;
using Il2CppInterop.Runtime;
using MelonLoader.Utils;
using MoleMole;
using UnityEngine;
using UnityEngine.UI;
using static Reliquary.Ui.Widgets;
using Object = UnityEngine.Object;

namespace Reliquary.Ui;

// The approved full-screen UI. Its objects are rebuilt whenever its state changes, because nothing else on this canvas
// moves and the game cannot run custom MonoBehaviours.
internal sealed class Panel
{
    private enum Tab { Characters, Teams }
    private enum Layer { Main, Picker, Confirm, Menu, Rename }

    private static readonly Slot[] SlotOrder = [Slot.Weapon, Slot.Flower, Slot.Plume, Slot.Sands, Slot.Goblet, Slot.Circlet];
    private static readonly Color Tint = Style.Hex(0x0b1326, 0.62f);
    private static readonly Color Bar = Style.Hex(0x080e1c, 0.45f);
    private static readonly Color CardFill = Style.Hex(0x0a1224, 0.62f);
    private static readonly Color Up = Style.Hex(0x8fd3a0);
    private static readonly Color Down = Style.Hex(0xe58f8f);
    private static readonly Color Warn = Style.Hex(0xf0c26e);
    private static readonly Color Paper = Style.Hex(0xeae2d2);
    private static readonly Color PaperInk = Style.Hex(0x3f3b30);
    private static readonly Color[] ElementColors = [Style.Hex(0x6c7a8a), Style.Hex(0xc8583e), Style.Hex(0x3e74c9), Style.Hex(0x5f9e3c), Style.Hex(0x8a5ec2), Style.Hex(0x4f9fc7), Style.Hex(0x4f9fc7), Style.Hex(0x3fa58e), Style.Hex(0xb58a3e)];

    private readonly Action<string> log, warning;
    private readonly Steps steps;
    private readonly Gear gear;
    private readonly GameIcons icons;
    private readonly MenuTile tile;
    private readonly Applier applier;
    private readonly Dictionary<PadButtons, float> repeats = [];
    private readonly List<(RectTransform Rect, Action Click)> hits = [];

    private Library library = new();
    private string file = "";
    private GameObject root, content, promptRoot;
    private Widgets ui;
    private Font font;
    private float nextFontLook;
    private bool open, dirty, padLayout = true;
    private Tab tab;
    private Layer layer;
    private int avatar, template, slotIndex, team, member, menuIndex;
    private bool inSlots;
    private Slot pickerSlot;
    private int pickerCursor;
    private bool pickerFree;
    private List<ulong> pickerItems = [];
    private Plan plan;
    private string planTitle = "";
    private string rename = "";
    private string toast = "";
    private string toastDetail = "";
    private float toastUntil;
    private IReadOnlyList<Move> undo = [];
    private float nextWarning;

    public Panel(Action<string> log, Action<string> warning)
    {
        this.log = log;
        this.warning = warning;
        steps = new Steps(log);
        gear = new Gear(steps, log);
        icons = new GameIcons(steps, log);
        tile = new MenuTile(steps, log);
        applier = new Applier(gear, Finished);
    }

    public void Update(float now, float deltaTime)
    {
        try
        {
            Pad.Poll(now, log);
            if (Pad.Held != PadButtons.None || Pad.Stick != PadButtons.None)
                padLayout = true;
            else if (Input.anyKeyDown || Input.GetMouseButtonDown(0))
                padLayout = false;
            tile.Update(now, Shapes.Get(Shapes.Sigil));
            applier.Update();
            if (!open)
            {
                DrawPrompt();
                if (tile.Open && (Pad.Pressed(PadButtons.Y) || tile.Clicked()))
                    Open();
                return;
            }
            Input_(now);
            if (toastUntil != 0f && now >= toastUntil && !applier.Busy)
            {
                toastUntil = 0f;
                undo = [];
                dirty = true;
            }
            if (dirty && Ensure(now))
                Build();
        }
        catch (Exception error)
        {
            if (now >= nextWarning)
            {
                nextWarning = now + 10f;
                warning($"Reliquary failed: {error}");
            }
        }
    }

    private void Open()
    {
        gear.Refresh();
        if (gear.Characters.Count == 0)
            return;
        LoadLibrary(gear.Characters[0].Guid);
        tile.Leave();
        DestroyPrompt();
        ulong active = gear.Active();
        avatar = Math.Max(0, gear.Characters.ToList().FindIndex(character => character.Guid == active));
        template = slotIndex = member = 0;
        inSlots = false;
        tab = Tab.Characters;
        layer = Layer.Main;
        open = dirty = true;
        Pad.Settle();
        steps.Run("switch player input", () => ActorUtils.EnablePlayerInput(false, false));
    }

    private void Close()
    {
        open = false;
        layer = Layer.Main;
        if (root)
            Object.Destroy(root);
        root = content = null;
        Pad.Settle();
        steps.Run("switch player input", () => ActorUtils.EnablePlayerInput(true, false));
    }

    private void LoadLibrary(ulong anyGuid)
    {
        string folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Reliquary");
        Directory.CreateDirectory(folder);
        string next = Path.Combine(folder, $"{anyGuid >> 32}.json");
        if (next == file)
            return;
        file = next;
        library = File.Exists(file) ? Library.Load(File.ReadAllText(file)) : new Library();
        log($"loaded {library.Characters.Count} character and {library.Teams.Count} team templates");
    }

    private void Save() => File.WriteAllText(file, library.Save());

    private Character Current => gear.Characters[avatar];

    private List<CharacterTemplate> Templates => [.. library.Of(Current.Guid)];

    private CharacterTemplate Selected => Templates is var list && template < list.Count ? list[template] : null;

    // Input

    private void Input_(float now)
    {
        if (applier.Busy)
            return;
        ClickHits();
        switch (layer)
        {
            case Layer.Main:
                if (tab == Tab.Characters) CharactersInput(now); else TeamsInput(now);
                if (Pressed(PadButtons.LT, KeyCode.Q) || Pressed(PadButtons.RT, KeyCode.E))
                    SwitchTab(tab == Tab.Characters ? Tab.Teams : Tab.Characters);
                if (Pressed(PadButtons.B, KeyCode.Escape))
                    Close();
                break;
            case Layer.Picker: PickerInput(now); break;
            case Layer.Confirm:
                if (Pressed(PadButtons.A, KeyCode.Return))
                    Confirm();
                else if (Pressed(PadButtons.B, KeyCode.Escape))
                    Back();
                break;
            case Layer.Menu: MenuInput(now); break;
            case Layer.Rename: RenameInput(); break;
        }
    }

    private void CharactersInput(float now)
    {
        if (Pressed(PadButtons.LB, KeyCode.PageUp))
            MoveAvatar(-1);
        else if (Pressed(PadButtons.RB, KeyCode.PageDown))
            MoveAvatar(1);
        else if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MoveCursor(inSlots ? 0 : -1, inSlots ? -1 : 0);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MoveCursor(inSlots ? 0 : 1, inSlots ? 1 : 0);
        else if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
        {
            if (inSlots && slotIndex % 3 == 0)
                inSlots = false;
            else if (inSlots)
                slotIndex--;
            dirty = true;
        }
        else if (Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
        {
            if (!inSlots && Selected != null)
                inSlots = true;
            else if (inSlots && slotIndex % 3 < 2)
                slotIndex++;
            dirty = true;
        }
        else if (Pressed(PadButtons.A, KeyCode.Return))
        {
            if (inSlots && Selected != null)
                OpenPicker(SlotOrder[slotIndex]);
            else if (!inSlots && Selected != null)
            {
                inSlots = true;
                dirty = true;
            }
        }
        else if (Pressed(PadButtons.X, KeyCode.F))
            AskApply();
        else if (Pressed(PadButtons.Y, KeyCode.N))
            undoOrCreate();
        else if (Pressed(PadButtons.View, KeyCode.G))
            SaveWorn();
        else if (Pressed(PadButtons.Menu, KeyCode.R) && Selected != null)
            OpenMenu();
    }

    private void undoOrCreate()
    {
        if (undo.Count > 0)
            Undo();
        else
            Create();
    }

    private void TeamsInput(float now)
    {
        if (Pressed(PadButtons.LB, KeyCode.PageUp))
            MoveTeam(-1);
        else if (Pressed(PadButtons.RB, KeyCode.PageDown))
            MoveTeam(1);
        else if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
            MoveMember(-1);
        else if (Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
            MoveMember(1);
        else if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            ChangeMemberTemplate(-1);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            ChangeMemberTemplate(1);
        else if (Pressed(PadButtons.X, KeyCode.F))
            AskApply();
        else if (Pressed(PadButtons.Y, KeyCode.N))
        {
            if (undo.Count > 0) Undo(); else SaveParty();
        }
        else if (Pressed(PadButtons.Menu, KeyCode.R) && CurrentTeam != null)
            OpenMenu();
    }

    private void PickerInput(float now)
    {
        const int Columns = 6;
        if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
            MovePicker(-1);
        else if (Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
            MovePicker(1);
        else if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MovePicker(-Columns);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MovePicker(Columns);
        else if (Pressed(PadButtons.X, KeyCode.F))
        {
            pickerFree = !pickerFree;
            FillPicker();
            dirty = true;
        }
        else if (Pressed(PadButtons.A, KeyCode.Return))
            Pick();
        else if (Pressed(PadButtons.B, KeyCode.Escape))
            Back();
    }

    private void MenuInput(float now)
    {
        if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now) || Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
        {
            menuIndex = 1 - menuIndex;
            dirty = true;
        }
        else if (Pressed(PadButtons.A, KeyCode.Return))
        {
            if (menuIndex == 0) StartRename(); else DeleteSelected();
        }
        else if (Pressed(PadButtons.B, KeyCode.Escape))
            Back();
    }

    private void RenameInput()
    {
        var text = new StringBuilder(rename);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++)
            if (Input.GetKeyDown(key) && text.Length < 28)
                text.Append(shift ? key.ToString() : key.ToString().ToLowerInvariant());
        for (KeyCode key = KeyCode.Alpha0; key <= KeyCode.Alpha9; key++)
            if (Input.GetKeyDown(key) && text.Length < 28)
                text.Append((char)('0' + (key - KeyCode.Alpha0)));
        if (Input.GetKeyDown(KeyCode.Space) && text.Length < 28)
            text.Append(' ');
        if (Input.GetKeyDown(KeyCode.Backspace) && text.Length > 0)
            text.Length--;
        if (text.ToString() != rename)
        {
            rename = text.ToString();
            dirty = true;
        }
        if (Pressed(PadButtons.A, KeyCode.Return) && rename.Trim().Length > 0)
        {
            if (tab == Tab.Characters) Selected!.Name = rename.Trim(); else CurrentTeam!.Name = rename.Trim();
            Save();
            Back();
        }
        else if (Pressed(PadButtons.B, KeyCode.Escape))
            Back();
    }

    private bool Pressed(PadButtons button, KeyCode key) => Pad.Pressed(button) || Input.GetKeyDown(key);

    private bool Repeat(PadButtons button, KeyCode key, float now)
    {
        bool held = (Pad.Held & button) != 0 || (Pad.Stick & button) != 0 || Input.GetKey(key);
        if (!held)
        {
            repeats.Remove(button);
            return false;
        }
        if (!repeats.TryGetValue(button, out float next))
        {
            repeats[button] = now + 0.35f;
            return true;
        }
        if (now < next)
            return false;
        repeats[button] = now + 0.09f;
        return true;
    }

    private void ClickHits()
    {
        if (!Input.GetMouseButtonDown(0))
            return;
        var point = new Vector2 { x = Input.mousePosition.x, y = Input.mousePosition.y };
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Rect && RectTransformUtility.RectangleContainsScreenPoint(hits[i].Rect, point, null))
            {
                hits[i].Click();
                dirty = true;
                return;
            }
    }

    // Actions

    private void SwitchTab(Tab next)
    {
        tab = next;
        inSlots = false;
        if (tab == Tab.Teams)
        {
            int deployed = library.Teams.FindIndex(candidate => candidate.Members.Select(m => m.Avatar).OrderBy(g => g).SequenceEqual(gear.Party.OrderBy(g => g)));
            team = deployed >= 0 ? deployed : Math.Clamp(team, 0, Math.Max(0, library.Teams.Count - 1));
            member = 0;
        }
        dirty = true;
    }

    private void MoveAvatar(int by)
    {
        avatar = (avatar + by + gear.Characters.Count) % gear.Characters.Count;
        template = 0;
        inSlots = false;
        dirty = true;
    }

    private void MoveCursor(int list, int slots)
    {
        if (inSlots)
            slotIndex = (slotIndex + 3 * slots + 6) % 6;
        else if (Templates.Count > 0)
            template = (template + list + Templates.Count) % Templates.Count;
        dirty = true;
    }

    private void Create()
    {
        var created = new CharacterTemplate { Avatar = Current.Guid, Name = $"Template {Templates.Count + 1}" };
        library.Characters.Add(created);
        Save();
        template = Templates.Count - 1;
        inSlots = true;
        slotIndex = 0;
        dirty = true;
    }

    private void SaveWorn()
    {
        library.Characters.Add(new CharacterTemplate { Avatar = Current.Guid, Name = $"Template {Templates.Count + 1}", Items = gear.WornItems(Current.Guid) });
        Save();
        template = Templates.Count - 1;
        Toast("Saved worn gear as a template", "");
        dirty = true;
    }

    private void SaveParty()
    {
        var team_ = new TeamTemplate { Name = $"Team {library.Teams.Count + 1}" };
        foreach (ulong guid in gear.Party)
        {
            var saved = new CharacterTemplate { Avatar = guid, Name = team_.Name, Items = gear.WornItems(guid) };
            library.Characters.Add(saved);
            team_.Members.Add(new TeamMember { Avatar = guid, Template = saved.Id });
        }
        library.Teams.Add(team_);
        team = library.Teams.Count - 1;
        Save();
        Toast("Saved the party's worn gear as a team", "");
        dirty = true;
    }

    private TeamTemplate CurrentTeam => team < library.Teams.Count ? library.Teams[team] : null;

    private void MoveTeam(int by)
    {
        if (library.Teams.Count > 0)
            team = (team + by + library.Teams.Count) % library.Teams.Count;
        member = 0;
        dirty = true;
    }

    private void MoveMember(int by)
    {
        if (CurrentTeam is { } current && current.Members.Count > 0)
            member = (member + by + current.Members.Count) % current.Members.Count;
        dirty = true;
    }

    private void ChangeMemberTemplate(int by)
    {
        if (CurrentTeam is not { } current || member >= current.Members.Count)
            return;
        TeamMember who = current.Members[member];
        var choices = library.Of(who.Avatar).Select(t => t.Id).Prepend("").ToList();
        int at = Math.Max(0, choices.IndexOf(who.Template));
        who.Template = choices[(at + by + choices.Count) % choices.Count];
        Save();
        dirty = true;
    }

    private void OpenPicker(Slot slot)
    {
        pickerSlot = slot;
        pickerFree = false;
        FillPicker();
        ulong chosen = Selected?.Items.GetValueOrDefault(slot) ?? 0;
        pickerCursor = Math.Max(0, pickerItems.IndexOf(chosen));
        layer = Layer.Picker;
        dirty = true;
    }

    private void FillPicker()
    {
        pickerItems = [.. gear.Items(pickerSlot).Where(item => !pickerFree || gear.Holder(item) == null)
            .OrderByDescending(item => gear.View(item)?.Level ?? 0).ThenByDescending(item => gear.View(item)?.Rarity ?? 0)];
        if (pickerSlot == Slot.Weapon)
        {
            ItemView worn = gear.View(gear.Worn(Current.Guid, Slot.Weapon));
            if (worn != null)
                pickerItems = [.. pickerItems.Where(item => WeaponKind(item) == WeaponKind(worn.Guid))];
        }
        pickerCursor = Math.Clamp(pickerCursor, 0, Math.Max(0, pickerItems.Count - 1));
    }

    private int WeaponKind(ulong item) => gear.WeaponType(item);

    private void MovePicker(int by)
    {
        if (pickerItems.Count == 0)
            return;
        pickerCursor = Math.Clamp(pickerCursor + by, 0, pickerItems.Count - 1);
        dirty = true;
    }

    private void Pick()
    {
        if (Selected == null || pickerCursor >= pickerItems.Count)
            return;
        Selected.Items[pickerSlot] = pickerItems[pickerCursor];
        Save();
        Back();
    }

    private void OpenMenu()
    {
        menuIndex = 0;
        layer = Layer.Menu;
        dirty = true;
    }

    private void StartRename()
    {
        rename = tab == Tab.Characters ? Selected?.Name ?? "" : CurrentTeam?.Name ?? "";
        layer = Layer.Rename;
        dirty = true;
    }

    private void DeleteSelected()
    {
        if (tab == Tab.Characters && Selected is { } doomed)
        {
            library.Delete(doomed);
            template = Math.Max(0, template - 1);
        }
        else if (tab == Tab.Teams && CurrentTeam is { } gone)
        {
            library.Teams.Remove(gone);
            team = Math.Max(0, team - 1);
        }
        inSlots = false;
        Save();
        Back();
    }

    private void Back()
    {
        layer = Layer.Main;
        dirty = true;
    }

    private void AskApply()
    {
        IEnumerable<CharacterTemplate> templates;
        if (tab == Tab.Characters)
        {
            if (Selected is not { } one)
                return;
            templates = [one];
            planTitle = $"Apply {one.Name} to {Current.Name}?";
        }
        else
        {
            if (CurrentTeam is not { } current)
                return;
            templates = library.Templates(current);
            planTitle = $"Apply {current.Name}?";
        }
        gear.Refresh();
        plan = Planner.Plan(templates, gear);
        if (plan.Clashes.Count > 0)
        {
            Toast("A team member needs an item another member needs", "Choose another template for one of them");
            dirty = true;
            return;
        }
        if (plan.Moves.Count == 0)
        {
            Toast("Nothing to change", plan.Missing.Count > 0 ? $"{plan.Missing.Count} items are no longer owned" : "This gear is already worn");
            dirty = true;
            return;
        }
        layer = Layer.Confirm;
        dirty = true;
    }

    private void Confirm()
    {
        layer = Layer.Main;
        applier.Start(plan.Moves);
        Toast("Applying", $"0 of {plan.Moves.Count}", 600f);
        dirty = true;
    }

    private void Undo()
    {
        IReadOnlyList<Move> moves = undo;
        undo = [];
        gear.Refresh();
        applier.Start(moves);
        Toast("Undoing", "", 600f);
        dirty = true;
    }

    private void Finished(IReadOnlyList<Move> done, Move? failed)
    {
        gear.Refresh();
        undo = Planner.Undo(done);
        if (failed is { } refused)
            Toast($"{done.Count} items changed. The game refused the next one", $"{gear.View(refused.Item)?.Name ?? "An item"} for {gear.NameOf(refused.Avatar)}. Y to undo", 8f);
        else
        {
            var swapped = done.Where(move => move.Holder is not null).ToList();
            string detail = swapped.Count > 0 ? $"{gear.NameOf(swapped[0].Holder!.Value)} received the old item" : "";
            Toast($"Applied {done.Count} items", detail + (undo.Count > 0 ? (detail.Length > 0 ? ". " : "") + "Y to undo" : ""), 6f);
        }
        dirty = true;
    }

    private void Toast(string text, string detail, float seconds = 2.5f)
    {
        toast = text;
        toastDetail = detail;
        toastUntil = Time.unscaledTime + seconds;
    }

    // Drawing

    private bool Ensure(float now)
    {
        if (root)
            return true;
        if (!font)
        {
            if (now < nextFontLook)
                return false;
            nextFontLook = now + 1f;
            foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Font>()))
                if (item.name == Style.GameFont)
                    font = item.TryCast<Font>();
            if (!font)
                return false;
        }
        root = NewCanvas("Reliquary");
        ui = new Widgets(font);
        return true;
    }

    private static GameObject NewCanvas(string name)
    {
        var made = new GameObject(name);
        Object.DontDestroyOnLoad(made);
        var canvas = made.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Style.SortingOrder;
        var scaler = made.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = V(Style.ReferenceWidth, Style.ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referencePixelsPerUnit = 100f;
        return made;
    }

    private void DrawPrompt()
    {
        if (!tile.Open || !padLayout)
        {
            DestroyPrompt();
            return;
        }
        if (promptRoot || !font && !FindFont())
            return;
        promptRoot = NewCanvas("ReliquaryPrompt");
        ui = new Widgets(font);
        RectTransform box = Node("Prompt", promptRoot.transform);
        TopLeft(box, 110f, 736f, 150f, 32f);
        Glyph(box, "Y", Style.XboxY, 0f, 4f);
        Print(box, "Reliquary", 30f, 4f, 110f, 24f, 15, Style.BackInk, TextAnchor.MiddleLeft);
    }

    private bool FindFont()
    {
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Font>()))
            if (item.name == Style.GameFont)
                font = item.TryCast<Font>();
        return font;
    }

    private void DestroyPrompt()
    {
        if (promptRoot)
            Object.Destroy(promptRoot);
        promptRoot = null;
    }

    private void Build()
    {
        dirty = false;
        hits.Clear();
        if (content)
            Object.Destroy(content);
        content = Node("Content", root.transform).gameObject;
        RectTransform rect = content.GetComponent<RectTransform>();
        Fill(rect);
        Fill(Picture(rect, "Tint", null, Tint).rectTransform);
        switch (layer)
        {
            case Layer.Picker: DrawPicker(rect); break;
            default:
                DrawTop(rect);
                if (tab == Tab.Characters) DrawCharacters(rect); else DrawTeams(rect);
                break;
        }
        if (layer == Layer.Confirm) DrawConfirm(rect);
        if (layer == Layer.Menu) DrawMenu(rect);
        if (layer == Layer.Rename) DrawRename(rect);
        if (toastUntil != 0f)
            DrawToast(rect);
    }

    private void DrawTop(RectTransform parent)
    {
        Image bar = Box(parent, 0f, 0f, 1280f, 74f, Bar, null);
        Image sigil = Picture(parent, "Sigil", Shapes.Sigil, Style.Cream);
        TopLeft(sigil.rectTransform, 28f, 22f, 30f, 30f);
        Print(parent, "Reliquary", 68f, 22f, 200f, 30f, 21, Style.Text, TextAnchor.MiddleLeft);
        float x = 290f;
        if (padLayout)
            Pill(parent, "LT", x, 27f);
        TabLabel(parent, "Characters", x + 38f, Tab.Characters);
        TabLabel(parent, "Teams", x + 150f, Tab.Teams);
        if (padLayout)
            Pill(parent, "RT", x + 228f, 27f);
        if (tab != Tab.Characters)
            return;
        const int Shown = 9;
        int count = gear.Characters.Count;
        float faces = 1280f - 28f - (Shown * 50f) - (padLayout ? 40f : 0f);
        if (padLayout)
            Pill(parent, "LB", faces - 40f, 27f);
        for (int i = 0; i < Math.Min(Shown, count); i++)
        {
            int index = (avatar + i - Math.Min(Shown / 2, avatar) + count) % count;
            if (avatar >= count - Shown / 2 && count > Shown)
                index = count - Shown + i;
            Character who = gear.Characters[index];
            float at = faces + i * 50f;
            Face(parent, who, at, 16f, 42f, index == avatar);
            int target = index;
            Hit(parent, at, 16f, 42f, 42f, () => { avatar = target; template = 0; inSlots = false; });
        }
        if (padLayout)
            Pill(parent, "RB", 1280f - 58f, 27f);
    }

    private void TabLabel(RectTransform parent, string text, float x, Tab which)
    {
        Print(parent, text, x, 22f, 100f, 30f, 16, which == tab ? Style.Text : Style.Dim, TextAnchor.MiddleLeft);
        if (which == tab)
            Box(parent, x, 56f, text.Length * 8.5f, 3f, Style.Cream, null);
        Hit(parent, x, 16f, 100f, 42f, () => SwitchTab(which));
    }

    private void DrawCharacters(RectTransform parent)
    {
        Box(parent, 0f, 74f, 276f, 666f, Bar, null);
        List<CharacterTemplate> list = Templates;
        float y = 80f;
        for (int i = 0; i < list.Count && y < 590f; i++)
        {
            CharacterTemplate item = list[i];
            float height = DrawTemplateRow(parent, item, y, i == template && !inSlots ? Selection.Focus : i == template ? Selection.Current : Selection.None);
            int target = i;
            Hit(parent, 0f, y, 276f, height, () => { template = target; inSlots = false; });
            y += height;
        }
        if (list.Count == 0)
            Print(parent, "No templates yet", 28f, 96f, 230f, 20f, 13, Style.Dim, TextAnchor.MiddleLeft);
        Action create = Create, saveWorn = SaveWorn;
        Button_(parent, "Create new template", "Y", Style.XboxY, 14f, 640f, 248f, 40f, true, create);
        Button_(parent, "Save worn gear as template", "View", Style.Text, 14f, 688f, 248f, 40f, false, saveWorn);

        if (Selected is { } current)
        {
            Print(parent, current.Name, 296f, 90f, 420f, 28f, 22, Style.Text, TextAnchor.MiddleLeft);
            Print(parent, Current.Name, 300f + current.Name.Length * 13f, 96f, 200f, 18f, 13, Style.Dim, TextAnchor.MiddleLeft);
            for (int i = 0; i < SlotOrder.Length; i++)
                DrawSlot(parent, current, SlotOrder[i], 296f + (i % 3) * 218f, 128f + (i / 3) * 298f, i);
            DrawCompare(parent, current);
        }
        else
            Print(parent, $"Create a template for {Current.Name}", 296f, 340f, 660f, 30f, 18, Style.Dim, TextAnchor.MiddleCenter);
        DrawFooter(parent, inSlots ? "Change item" : "Open template");
    }

    private enum Selection { None, Current, Focus }

    private float DrawTemplateRow(RectTransform parent, CharacterTemplate item, float y, Selection selection)
    {
        var (notes, warn) = TemplateNote(item);
        bool worn = item.Items.Count > 0 && item.Items.All(pair => gear.Worn(item.Avatar, pair.Key) == pair.Value);
        float height = 48f + (notes.Length > 0 ? 18f : 0f);
        if (selection != Selection.None)
            Box(parent, 0f, y, 276f, height, Style.Band, null);
        if (selection == Selection.Focus)
            Print(parent, "▸", 10f, y + 10f, 14f, 20f, 13, Style.Text, TextAnchor.MiddleLeft);
        Print(parent, item.Name + (worn ? "   (Worn)" : ""), 28f, y + 8f, 240f, 20f, 15, Style.Text, TextAnchor.MiddleLeft);
        Print(parent, SetSummary(item.Items.Values), 28f, y + 28f, 240f, 16f, 12, Style.Dim, TextAnchor.MiddleLeft);
        if (notes.Length > 0)
            Print(parent, notes, 28f, y + 44f, 240f, 16f, 12, warn ? Warn : Down, TextAnchor.MiddleLeft);
        return height;
    }

    private (string, bool) TemplateNote(CharacterTemplate item)
    {
        int gone = 0, held = 0;
        string who = "";
        foreach (ulong guid in item.Items.Values)
        {
            if (!gear.Owned(guid))
                gone++;
            else if (gear.Holder(guid) is { } holder && holder != item.Avatar)
            {
                held++;
                who = gear.NameOf(holder);
            }
        }
        if (gone > 0)
            return ($"{gone} item{(gone > 1 ? "s" : "")} no longer owned", false);
        return held > 0 ? (held == 1 ? $"1 item worn by {who}" : $"{held} items worn by others", true) : ("", true);
    }

    private string SetSummary(IEnumerable<ulong> items)
    {
        var sets = items.Select(gear.View).Where(view => view != null && view.Set != 0).GroupBy(view => view.Set).Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count()).Select(group => $"{gear.SetName(group.Key)} {group.Count()}").ToList();
        return sets.Count > 0 ? string.Join(", ", sets) : items.Any() ? "No set bonus" : "Nothing chosen yet";
    }

    private void DrawSlot(RectTransform parent, CharacterTemplate current, Slot slot, float x, float y, int index)
    {
        const float Width = 208f, Height = 288f;
        bool focus = inSlots && index == slotIndex;
        Box(parent, x, y, Width, Height, CardFill, Shapes.Card);
        if (focus)
            Frame(parent, x, y, Width, Height);
        Print(parent, slot.ToString(), x + 12f, y + 10f, 120f, 16f, 12, Style.Dim, TextAnchor.MiddleLeft);
        Hit(parent, x, y, Width, Height, () => { inSlots = true; slotIndex = index; OpenPicker(slot); });
        ulong guid = current.Items.GetValueOrDefault(slot);
        ItemView view = guid == 0 ? null : gear.View(guid);
        if (guid == 0)
        {
            Print(parent, slot == Slot.Sands ? "Choose sands" : $"Choose a {slot.ToString().ToLowerInvariant()}", x, y + 130f, Width, 20f, 14, focus ? Style.Text : Style.Dim, TextAnchor.MiddleCenter);
            return;
        }
        if (view == null)
        {
            Print(parent, "No longer owned", x, y + 130f, Width, 20f, 14, Down, TextAnchor.MiddleCenter);
            return;
        }
        Icon_(parent, view, x + 12f, y + 30f, 54f);
        Wrap(parent, view.Name, x + 74f, y + 30f, Width - 84f, 54f, 13, Style.Text);
        float line = y + 92f;
        if (view.Main is { } main)
        {
            Print(parent, main.Label, x + 12f, line, 130f, 18f, 13, Style.Cream, TextAnchor.MiddleLeft);
            Print(parent, Stats.Format(main), x + 12f, line, Width - 24f, 18f, 13, Style.Cream, TextAnchor.MiddleRight);
            line += 20f;
        }
        Print(parent, slot == Slot.Weapon ? $"Level {view.Level}, Refinement {view.Refinement}" : $"Level {view.Level}", x + 12f, line, Width - 24f, 16f, 12, Style.Dim, TextAnchor.MiddleLeft);
        line += 24f;
        foreach (ItemStat stat in view.Subs.Take(4))
        {
            Print(parent, stat.Label, x + 12f, line, 130f, 16f, 12, Style.Dim, TextAnchor.MiddleLeft);
            Print(parent, Stats.Format(stat), x + 12f, line, Width - 24f, 16f, 12, Style.Dim, TextAnchor.MiddleRight);
            line += 18f;
        }
        if (gear.Holder(guid) is { } holder && holder != current.Avatar)
            Print(parent, $"Worn by {gear.NameOf(holder)}", x + 12f, y + Height - 26f, Width - 24f, 16f, 12, Warn, TextAnchor.MiddleLeft);
    }

    // The items' own stats, without set bonuses or the character's base stats.
    private void DrawCompare(RectTransform parent, CharacterTemplate current)
    {
        const float X = 960f, Width = 296f;
        Print(parent, "Gear stats compared with worn gear", X, 90f, Width, 20f, 14, Style.Cream, TextAnchor.MiddleLeft);
        var after = new Dictionary<string, (float Value, bool Percent)>();
        var before = new Dictionary<string, (float Value, bool Percent)>();
        foreach (Slot slot in SlotOrder)
        {
            ulong worn = gear.Worn(current.Avatar, slot);
            ulong chosen = current.Items.GetValueOrDefault(slot, worn);
            Add(before, gear.View(worn));
            Add(after, gear.View(chosen));
        }
        float y = 118f;
        foreach (string label in after.Keys.Union(before.Keys).OrderBy(Rank))
        {
            float a = after.GetValueOrDefault(label).Value, b = before.GetValueOrDefault(label).Value;
            bool percent = after.TryGetValue(label, out var stat) ? stat.Percent : before[label].Percent;
            float delta = a - b;
            Print(parent, label, X, y, 150f, 20f, 13, Style.Text, TextAnchor.MiddleLeft);
            Print(parent, Stats.Format(a, percent), X, y, Width - 66f, 20f, 13, Style.Text, TextAnchor.MiddleRight);
            bool same = Math.Abs(delta) < 0.0005f;
            Print(parent, same ? "same" : (delta > 0 ? "+" : "") + Stats.Format(delta, percent), X, y, Width, 20f, 12, same ? Style.Dim : delta > 0 ? Up : Down, TextAnchor.MiddleRight);
            y += 24f;
            if (y > 560f)
                break;
        }
        string sets = SetSummary(SlotOrder.Select(slot => current.Items.GetValueOrDefault(slot, gear.Worn(current.Avatar, slot))).Where(guid => guid != 0));
        Print(parent, sets, X, y + 10f, Width, 18f, 13, Up, TextAnchor.MiddleLeft);
        Action apply = AskApply;
        Button_(parent, "Apply template", "X", Style.XboxX, X, y + 40f, Width, 40f, true, apply);
    }

    private static void Add(Dictionary<string, (float, bool)> totals, ItemView view)
    {
        if (view == null)
            return;
        foreach (ItemStat stat in view.Subs.Append(view.Main).OfType<ItemStat>())
            totals[stat.Label] = (totals.GetValueOrDefault(stat.Label).Item1 + stat.Value, stat.Percent);
    }

    private static int Rank(string label)
    {
        int at = Array.IndexOf(new[] { "HP", "HP%", "Base ATK", "ATK", "ATK%", "DEF", "DEF%", "Elemental Mastery", "CRIT Rate", "CRIT DMG", "Energy Recharge" }, label);
        return at >= 0 ? at : 99;
    }

    private void DrawFooter(RectTransform parent, string primary)
    {
        Box(parent, 0f, 740f, 1280f, 60f, Bar, null);
        if (!padLayout)
        {
            Print(parent, "Esc  Close", 1100f, 758f, 160f, 24f, 14, Style.Text, TextAnchor.MiddleRight);
            Action menu = OpenMenu;
            Button_(parent, "Rename or delete", "", Style.Text, 24f, 750f, 180f, 36f, false, menu);
            return;
        }
        Glyph(parent, "☰", Style.Text, 28f, 756f);
        Print(parent, "Rename or delete", 60f, 758f, 180f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        Glyph(parent, "A", Style.XboxA, 1010f, 756f);
        Print(parent, primary, 1040f, 758f, 120f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        Glyph(parent, "B", Style.XboxB, 1170f, 756f);
        Print(parent, "Close", 1200f, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
    }

    private void DrawTeams(RectTransform parent)
    {
        TeamTemplate current = CurrentTeam;
        if (current == null)
        {
            Print(parent, "No team templates yet", 0f, 320f, 1280f, 30f, 18, Style.Dim, TextAnchor.MiddleCenter);
            Action save = SaveParty;
            Button_(parent, "Save party's worn gear as team", "Y", Style.XboxY, 470f, 370f, 340f, 42f, true, save);
            DrawFooter(parent, "");
            return;
        }
        if (padLayout)
            Pill(parent, "LB", 28f, 96f);
        float at = padLayout ? 68f : 28f;
        Print(parent, current.Name, at, 90f, 300f, 30f, 21, Style.Text, TextAnchor.MiddleLeft);
        if (padLayout)
            Pill(parent, "RB", at + current.Name.Length * 12f + 10f, 96f);
        Print(parent, $"Team template {team + 1} of {library.Teams.Count}", at + current.Name.Length * 12f + 50f, 94f, 300f, 22f, 13, Style.Dim, TextAnchor.MiddleLeft);
        Plan check = Planner.Plan(library.Templates(current), gear);
        for (int i = 0; i < current.Members.Count; i++)
            DrawMember(parent, current.Members[i], 28f + i * 310f, i == member, check);
        if (check.Clashes.Count > 0)
        {
            Clash clash = check.Clashes[0];
            Box(parent, 28f, 666f, 1224f, 60f, Style.Hex(0xf0c26e, 0.14f), Shapes.Card);
            Print(parent, $"{gear.NameOf(clash.First)} and {gear.NameOf(clash.Second)} both need {gear.View(clash.Item)?.Name ?? "one item"}", 44f, 676f, 1190f, 20f, 14, Warn, TextAnchor.MiddleLeft);
            Print(parent, "Choose another template for one of them. The team cannot be applied until then.", 44f, 698f, 1190f, 18f, 12, Style.Dim, TextAnchor.MiddleLeft);
        }
        Box(parent, 0f, 740f, 1280f, 60f, Bar, null);
        Color applyInk = check.CanApply ? Style.Text : Style.Dim;
        if (padLayout)
        {
            Glyph(parent, "Y", Style.XboxY, 28f, 756f);
            Print(parent, "Save party's worn gear as team", 60f, 758f, 280f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Glyph(parent, "☰", Style.Text, 340f, 756f);
            Print(parent, "Rename or delete", 372f, 758f, 170f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Print(parent, "D-pad  Change template", 760f, 758f, 220f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Glyph(parent, "X", Style.XboxX, 990f, 756f);
            Print(parent, "Apply team", 1020f, 758f, 120f, 24f, 14, applyInk, TextAnchor.MiddleLeft);
            Glyph(parent, "B", Style.XboxB, 1170f, 756f);
            Print(parent, "Close", 1200f, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        }
        else
        {
            Action save = SaveParty, menu = OpenMenu, apply = AskApply;
            Button_(parent, "Save party's worn gear as team", "", Style.Text, 24f, 750f, 290f, 36f, false, save);
            Button_(parent, "Rename or delete", "", Style.Text, 324f, 750f, 180f, 36f, false, menu);
            Button_(parent, "Apply team", "", Style.Text, 960f, 750f, 150f, 36f, true, apply);
            Print(parent, "Esc  Close", 1120f, 758f, 140f, 24f, 14, Style.Text, TextAnchor.MiddleRight);
        }
    }

    private void DrawMember(RectTransform parent, TeamMember who, float x, bool focus, Plan check)
    {
        const float Width = 298f, Height = 516f, Top = 136f;
        Box(parent, x, Top, Width, Height, CardFill, Shapes.Card);
        if (focus)
            Frame(parent, x, Top, Width, Height);
        Character character = gear.Characters.FirstOrDefault(c => c.Guid == who.Avatar);
        if (character != null)
            Face(parent, character, x + 14f, Top + 14f, 42f, false);
        Print(parent, gear.NameOf(who.Avatar), x + 66f, Top + 24f, Width - 76f, 22f, 16, Style.Text, TextAnchor.MiddleLeft);
        CharacterTemplate chosen = library.Character(who.Template);
        Box(parent, x + 14f, Top + 68f, Width - 28f, 34f, Style.Band, Shapes.Card);
        Print(parent, chosen?.Name ?? "Keep worn gear", x + 14f, Top + 74f, Width - 28f, 22f, 14, Style.Text, TextAnchor.MiddleCenter);
        Print(parent, "‹", x + 22f, Top + 74f, 16f, 22f, 15, Style.Dim, TextAnchor.MiddleLeft);
        Print(parent, "›", x + Width - 38f, Top + 74f, 16f, 22f, 15, Style.Dim, TextAnchor.MiddleRight);
        for (int i = 0; i < SlotOrder.Length; i++)
        {
            ulong guid = chosen?.Items.GetValueOrDefault(SlotOrder[i]) ?? gear.Worn(who.Avatar, SlotOrder[i]);
            float cx = x + 14f + (i % 3) * 92f, cy = Top + 114f + (i / 3) * 92f;
            ItemView view = guid == 0 ? null : gear.View(guid);
            if (view != null)
                Icon_(parent, view, cx, cy, 86f);
            else
                Box(parent, cx, cy, 86f, 86f, Style.Band, Shapes.Card);
            if (guid != 0 && check.Clashes.Any(clash => clash.Item == guid))
                Frame(parent, cx, cy, 86f, 86f, Warn);
        }
        float y = Top + 310f;
        IEnumerable<ulong> items = SlotOrder.Select(slot => chosen?.Items.GetValueOrDefault(slot, gear.Worn(who.Avatar, slot)) ?? gear.Worn(who.Avatar, slot)).Where(guid => guid != 0);
        Print(parent, SetSummary(items), x + 14f, y, Width - 28f, 18f, 12, Up, TextAnchor.MiddleLeft);
        y += 24f;
        var totals = new Dictionary<string, (float, bool)>();
        foreach (ulong guid in items)
            Add(totals, gear.View(guid));
        foreach (var (label, (value, percent)) in totals.OrderBy(pair => Rank(pair.Key)).Take(7))
        {
            Print(parent, label, x + 14f, y, 180f, 18f, 13, Style.Text, TextAnchor.MiddleLeft);
            Print(parent, Stats.Format(value, percent), x + 14f, y, Width - 28f, 18f, 13, Style.Dim, TextAnchor.MiddleRight);
            y += 22f;
        }
    }

    private void DrawPicker(RectTransform parent)
    {
        const int Columns = 6, Rows = 5;
        const float CardWidth = 132f, CardHeight = 112f, Gap = 8f;
        Box(parent, 0f, 0f, 1280f, 74f, Bar, null);
        Print(parent, Selected?.Name ?? "", 28f, 22f, 300f, 30f, 21, Style.Text, TextAnchor.MiddleLeft);
        Print(parent, $"{pickerSlot} for {Current.Name}", 340f, 26f, 400f, 24f, 16, Style.Cream, TextAnchor.MiddleLeft);
        Print(parent, pickerFree ? "Showing items nobody wears" : "Showing all items", 28f, 84f, 400f, 20f, 13, Style.Dim, TextAnchor.MiddleLeft);
        int row = pickerCursor / Columns;
        int first = Math.Max(0, Math.Min(row - 2, (pickerItems.Count - 1) / Columns - Rows + 1)) * Columns;
        for (int i = first; i < pickerItems.Count && i < first + Columns * Rows; i++)
        {
            int local = i - first;
            float x = 28f + (local % Columns) * (CardWidth + Gap), y = 110f + (local / Columns) * (CardHeight + Gap);
            ItemView view = gear.View(pickerItems[i]);
            if (view == null)
                continue;
            Box(parent, x, y, CardWidth, CardHeight, Paper, Shapes.Card);
            Icon_(parent, view, x + (CardWidth - 76f) / 2f, y + 2f, 76f);
            Print(parent, $"+{view.Level}", x + 6f, y + 4f, 40f, 16f, 11, Style.White, TextAnchor.MiddleLeft);
            Wrap(parent, view.Name, x + 4f, y + 78f, CardWidth - 8f, 32f, 11, PaperInk, TextAnchor.MiddleCenter);
            if (gear.Holder(view.Guid) is { } holder && gear.Characters.FirstOrDefault(c => c.Guid == holder) is { } who)
                Face(parent, who, x + CardWidth - 30f, y + 4f, 26f, false);
            if (i == pickerCursor)
                Frame(parent, x, y, CardWidth, CardHeight);
            int target = i;
            Hit(parent, x, y, CardWidth, CardHeight, () => { pickerCursor = target; Pick(); });
        }
        const float Side = 878f, Width = 378f;
        if (pickerCursor < pickerItems.Count && gear.View(pickerItems[pickerCursor]) is { } shown)
        {
            Box(parent, Side, 90f, Width, 400f, CardFill, Shapes.Card);
            Icon_(parent, shown, Side + 14f, 104f, 64f);
            Wrap(parent, shown.Name, Side + 88f, 104f, Width - 100f, 64f, 15, Style.Text);
            float y = 180f;
            if (shown.Main is { } main)
            {
                Print(parent, main.Label, Side + 14f, y, 220f, 20f, 14, Style.Cream, TextAnchor.MiddleLeft);
                Print(parent, Stats.Format(main), Side + 14f, y, Width - 28f, 20f, 14, Style.Cream, TextAnchor.MiddleRight);
                y += 24f;
            }
            Print(parent, pickerSlot == Slot.Weapon ? $"Level {shown.Level}, Refinement {shown.Refinement}" : $"Level {shown.Level}", Side + 14f, y, Width - 28f, 18f, 12, Style.Dim, TextAnchor.MiddleLeft);
            y += 28f;
            foreach (ItemStat stat in shown.Subs)
            {
                Print(parent, stat.Label, Side + 14f, y, 220f, 18f, 13, Style.Dim, TextAnchor.MiddleLeft);
                Print(parent, Stats.Format(stat), Side + 14f, y, Width - 28f, 18f, 13, Style.Dim, TextAnchor.MiddleRight);
                y += 22f;
            }
            if (gear.Holder(shown.Guid) is { } holder && holder != Current.Guid)
            {
                Print(parent, $"Worn by {gear.NameOf(holder)}", Side + 14f, 440f, Width - 28f, 18f, 13, Warn, TextAnchor.MiddleLeft);
                Print(parent, $"Applying gives {gear.NameOf(holder)} {Current.Name}'s worn {pickerSlot.ToString().ToLowerInvariant()}", Side + 14f, 462f, Width - 28f, 18f, 12, Style.Dim, TextAnchor.MiddleLeft);
            }
            Action pick = Pick;
            Button_(parent, "Use in this template", "A", Style.XboxA, Side, 504f, Width, 42f, true, pick);
        }
        Box(parent, 0f, 740f, 1280f, 60f, Bar, null);
        if (padLayout)
        {
            Glyph(parent, "X", Style.XboxX, 28f, 756f);
            Print(parent, pickerFree ? "Show all items" : "Show items nobody wears", 60f, 758f, 260f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Glyph(parent, "B", Style.XboxB, 1170f, 756f);
            Print(parent, "Back", 1200f, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        }
        else
            Print(parent, "Esc  Back", 1100f, 758f, 160f, 24f, 14, Style.Text, TextAnchor.MiddleRight);
    }

    private void DrawConfirm(RectTransform parent)
    {
        Fill(Picture(parent, "Veil", null, Style.Hex(0x060a14, 0.6f)).rectTransform);
        int lines = Math.Min(plan.Moves.Count, 8);
        float height = 150f + lines * 38f + (plan.Missing.Count > 0 ? 22f : 0f);
        const float X = 290f, Width = 700f;
        float y = (800f - height) / 2f;
        Box(parent, X, y, Width, height, Paper, Shapes.Card);
        Print(parent, planTitle, X + 28f, y + 20f, Width - 56f, 28f, 20, PaperInk, TextAnchor.MiddleLeft);
        string summary = $"{plan.Moves.Count} item{(plan.Moves.Count == 1 ? "" : "s")} change.";
        Print(parent, summary, X + 28f, y + 50f, Width - 56f, 18f, 13, PaperInk, TextAnchor.MiddleLeft);
        float line = y + 76f;
        foreach (Move move in plan.Moves.Take(lines))
        {
            string to = gear.View(move.Item)?.Name ?? "?";
            string from = gear.View(move.From)?.Name ?? "Empty";
            Print(parent, tab == Tab.Teams ? gear.NameOf(move.Avatar) : move.Slot.ToString(), X + 28f, line, 90f, 18f, 13, PaperInk, TextAnchor.MiddleLeft);
            Print(parent, from, X + 120f, line, 240f, 18f, 13, PaperInk, TextAnchor.MiddleLeft);
            Print(parent, "›", X + 362f, line, 16f, 18f, 13, PaperInk, TextAnchor.MiddleLeft);
            Print(parent, to, X + 384f, line, 300f, 18f, 13, PaperInk, TextAnchor.MiddleLeft);
            if (move.Holder is { } holder)
                Print(parent, $"{gear.NameOf(holder)} gets {from}", X + 384f, line + 17f, 300f, 16f, 12, Style.Hex(0xa8732a), TextAnchor.MiddleLeft);
            line += 38f;
        }
        if (plan.Moves.Count > lines)
            Print(parent, $"And {plan.Moves.Count - lines} more", X + 28f, line - 2f, 300f, 16f, 12, PaperInk, TextAnchor.MiddleLeft);
        if (plan.Missing.Count > 0)
            Print(parent, $"{plan.Missing.Count} item{(plan.Missing.Count == 1 ? " is" : "s are")} no longer owned and will be skipped.", X + 28f, y + height - 76f, Width - 56f, 18f, 12, Style.Hex(0xb04b3f), TextAnchor.MiddleLeft);
        Action back = Back, confirm = Confirm;
        Button_(parent, "Cancel", "B", Style.XboxB, X + 150f, y + height - 54f, 190f, 40f, false, back, PaperInk);
        Button_(parent, tab == Tab.Teams ? "Apply team" : "Apply template", "A", Style.XboxA, X + 360f, y + height - 54f, 190f, 40f, true, confirm);
    }

    private void DrawMenu(RectTransform parent)
    {
        Fill(Picture(parent, "Veil", null, Style.Hex(0x060a14, 0.6f)).rectTransform);
        const float X = 490f, Y = 310f;
        Box(parent, X, Y, 300f, 180f, Paper, Shapes.Card);
        Print(parent, tab == Tab.Characters ? Selected?.Name ?? "" : CurrentTeam?.Name ?? "", X + 20f, Y + 16f, 260f, 24f, 16, PaperInk, TextAnchor.MiddleCenter);
        Action rename_ = StartRename, delete = DeleteSelected;
        Button_(parent, "Rename", "", Style.Text, X + 40f, Y + 52f, 220f, 40f, menuIndex == 0, rename_, PaperInk);
        Button_(parent, "Delete", "", Style.Text, X + 40f, Y + 102f, 220f, 40f, menuIndex == 1, delete, PaperInk);
    }

    private void DrawRename(RectTransform parent)
    {
        Fill(Picture(parent, "Veil", null, Style.Hex(0x060a14, 0.6f)).rectTransform);
        const float X = 390f, Y = 330f;
        Box(parent, X, Y, 500f, 140f, Paper, Shapes.Card);
        Print(parent, "Type a name", X + 24f, Y + 16f, 450f, 20f, 15, PaperInk, TextAnchor.MiddleLeft);
        Print(parent, rename + "_", X + 24f, Y + 48f, 450f, 28f, 18, PaperInk, TextAnchor.MiddleLeft);
        Print(parent, padLayout ? "Steam + X opens the Deck's keyboard. A saves, B cancels." : "Enter saves, Esc cancels.", X + 24f, Y + 96f, 450f, 18f, 12, PaperInk, TextAnchor.MiddleLeft);
    }

    private void DrawToast(RectTransform parent)
    {
        float width = Math.Max(toast.Length, toastDetail.Length) * 8f + 60f;
        Box(parent, (1280f - width) / 2f, 668f, width, 58f, Style.Hex(0x141b2a, 0.96f), Shapes.Card);
        Print(parent, toast, 0f, 676f, 1280f, 22f, 14, Style.Text, TextAnchor.MiddleCenter);
        Print(parent, toastDetail, 0f, 698f, 1280f, 18f, 12, Style.Dim, TextAnchor.MiddleCenter);
    }

    // Parts

    private Text Print(Transform parent, string text, float x, float y, float width, float height, int size, Color color, TextAnchor alignment)
    {
        Text label = ui.Label(parent, "Text", text ?? "", size, color, alignment);
        TopLeft(label.rectTransform, x, y, width, height);
        return label;
    }

    private void Wrap(Transform parent, string text, float x, float y, float width, float height, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        Text label = Print(parent, text, x, y, width, height, size, color, alignment);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    private static Image Box(Transform parent, float x, float y, float width, float height, Color color, Shape shape)
    {
        Image box = Picture(parent, "Box", shape, color);
        TopLeft(box.rectTransform, x, y, width, height);
        return box;
    }

    private static void Frame(Transform parent, float x, float y, float width, float height, Color? color = null)
    {
        Color ink = color ?? Style.White;
        Box(parent, x - 2f, y - 2f, width + 4f, 2f, ink, null);
        Box(parent, x - 2f, y + height, width + 4f, 2f, ink, null);
        Box(parent, x - 2f, y, 2f, height, ink, null);
        Box(parent, x + width, y, 2f, height, ink, null);
    }

    private void Hit(Transform parent, float x, float y, float width, float height, Action click)
    {
        RectTransform rect = Node("Hit", parent);
        TopLeft(rect, x, y, width, height);
        hits.Add((rect, click));
    }

    private void Glyph(Transform parent, string letter, Color ink, float x, float y)
    {
        Image face = Picture(parent, "Face", Shapes.Face, Style.GlyphFill);
        TopLeft(face.rectTransform, x, y, 24f, 24f);
        Text label = ui.Label(face.rectTransform, "Letter", letter, Style.FaceSize, ink, TextAnchor.MiddleCenter);
        Fill(label.rectTransform);
    }

    private void Pill(Transform parent, string text, float x, float y)
    {
        Image plate = Picture(parent, "Pill", Shapes.Bumper, Style.White);
        TopLeft(plate.rectTransform, x, y, 32f, 20f);
        Text label = ui.Label(plate.rectTransform, "Text", text, Style.BumperSize, Style.BumperInk, TextAnchor.MiddleCenter);
        Fill(label.rectTransform);
    }

    private void Face(Transform parent, Character who, float x, float y, float size, bool focus)
    {
        Color fill = ElementColors[Math.Clamp(who.Element, 0, ElementColors.Length - 1)];
        if (focus)
        {
            Image ring = Picture(parent, "Ring", Shapes.Knob, Style.Cream);
            TopLeft(ring.rectTransform, x - 3f, y - 3f, size + 6f, size + 6f);
        }
        Image face = Picture(parent, "Face", Shapes.Knob, fill);
        TopLeft(face.rectTransform, x, y, size, size);
        Text label = ui.Label(face.rectTransform, "Text", who.Initials, (int)(size * 0.36f), Style.White, TextAnchor.MiddleCenter);
        Fill(label.rectTransform);
    }

    private void Icon_(Transform parent, ItemView view, float x, float y, float size)
    {
        int rarity = Math.Clamp(view.Rarity, 0, Shapes.Art.Length - 1);
        Image art = Picture(parent, "Art", Shapes.Art[rarity], Style.White);
        art.type = Image.Type.Simple;
        TopLeft(art.rectTransform, x, y, size, size);
        if (icons.Get(view.Icon) is { } sprite)
        {
            Image image = Picture(parent, "Icon", null, Style.White);
            image.sprite = sprite;
            image.preserveAspect = true;
            TopLeft(image.rectTransform, x, y, size, size);
        }
    }

    private void Button_(Transform parent, string text, string glyph, Color ink, float x, float y, float width, float height, bool primary, Action click, Color? outline = null)
    {
        Box(parent, x, y, width, height, primary ? Style.Light : outline == null ? Style.Band : Style.Hex(0x3f3b30, 0.12f), Shapes.Pill);
        Color textInk = primary ? Style.BackInk : outline ?? Style.Text;
        bool showGlyph = padLayout && glyph.Length > 0;
        if (showGlyph && glyph.Length == 1)
            Glyph(parent, glyph, ink, x + 16f, y + (height - 24f) / 2f);
        else if (showGlyph)
            Pill(parent, glyph, x + 10f, y + (height - 20f) / 2f);
        Print(parent, text, x + (showGlyph ? 40f : 0f), y, width - (showGlyph ? 44f : 0f), height, 13, textInk, showGlyph ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
        Hit(parent, x, y, width, height, click);
    }
}
