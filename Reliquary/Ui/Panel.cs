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
internal sealed partial class Panel
{
    private enum Tab { Characters, Teams }
    private enum Layer { Main, Picker, WeaponFilter, ArtifactSort, SetFilter, Confirm, Menu, Rename, Delete, Chooser, ChooserFilter }

    private static readonly KeyCode[] UiKeys = [KeyCode.Escape, KeyCode.Return, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Q, KeyCode.E, KeyCode.F, KeyCode.N, KeyCode.G, KeyCode.R, KeyCode.PageUp, KeyCode.PageDown, KeyCode.C];
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
    private readonly Backdrop backdrop;
    private bool backdropShown;
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
    private bool open, dirty, padLayout = true, cursorFreed;
    private Tab tab;
    private Layer layer;
    private int avatar, template, slotIndex, team, member, menuIndex;
    private bool inMembers;
    private bool inSlots;
    private Slot pickerSlot;
    private int pickerCursor;
    private readonly PickerQuery weaponQuery = new();
    private readonly PickerQuery artifactQuery = new();
    private readonly Dictionary<Slot, List<string>> sortBySlot = [];
    private List<string> savedChoice = [];
    private int panelCursor;
    private List<ulong> pickerItems = [];
    private List<PickerItem> pickerSource = [];
    private List<(uint Id, string Name, string Icon, string Bonus, int Count)> setRows = [];
    private Plan plan;
    private string planTitle = "";
    private string rename = "";
    private string toast = "";
    private string toastDetail = "";
    private float toastUntil;
    private IReadOnlyList<Move> undo = [];
    private float nextWarning;
    private float wide, extraY;
    private int screenWidth, screenHeight;
    private RectTransform promptBox;

    public Panel(Action<string> log, Action<string> warning)
    {
        this.log = log;
        this.warning = warning;
        steps = new Steps(log);
        gear = new Gear(steps, log);
        icons = new GameIcons(steps, log);
        tile = new MenuTile(steps, log);
        applier = new Applier(gear, Finished);
        backdrop = new Backdrop(steps, log);
    }

    public void Update(float now, float deltaTime)
    {
        try
        {
            Pad.Poll(now, log);
            if (Pad.Held != PadButtons.None || Pad.Stick != PadButtons.None)
                padLayout = true;
            else if (KeyboardUsed())
                padLayout = false;
            tile.Update(now, Shapes.Get(Shapes.Sigil));
            applier.Update();
            if (open)
            {
                motion.Update(now);
                Breathe(now);
            }
            if (closing)
                return;
            if (open)
            {
                LoadOnePortrait();
                UpdateCursor();
                AnimatePrompts(now);
                backdrop.Update(now);
                // The picture appears a frame or two after the camera first draws into it, and goes with a screen resize.
                bool blurred = backdrop.Picture != null;
                if (blurred != backdropShown)
                    dirty = true;
            }
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
            if (screenWidth != Screen.width || screenHeight != Screen.height)
            {
                screenWidth = Screen.width;
                screenHeight = Screen.height;
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

    // Input.anyKeyDown also fires for controller buttons on this game.
    private static bool KeyboardUsed()
    {
        if (Input.GetMouseButtonDown(0))
            return true;
        foreach (KeyCode key in UiKeys)
            if (Input.GetKeyDown(key))
                return true;
        return false;
    }

    private void Open()
    {
        gear.Refresh();
        if (gear.Characters.Count == 0)
            return;
        LoadLibrary(gear.Characters[0].Guid);
        icons.ReleaseSmall();
        tile.Leave();
        DestroyPrompt();
        ulong active = gear.Active();
        avatar = Math.Max(0, gear.Characters.ToList().FindIndex(character => character.Guid == active));
        template = slotIndex = member = 0;
        inSlots = false;
        tab = Tab.Characters;
        layer = Layer.Main;
        open = dirty = true;
        was = null;
        glides = [];
        glidesBefore = [];
        Pad.Settle();
        steps.Run("switch player input", () => ActorUtils.EnablePlayerInput(false, false));
    }

    // The game locks the cursor once its menu closes, so with a mouse the panel frees it, every frame in case the game locks it again.
    private void UpdateCursor()
    {
        if (padLayout)
        {
            LockCursor();
            return;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorFreed = true;
    }

    private void LockCursor()
    {
        if (!cursorFreed)
            return;
        cursorFreed = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Close()
    {
        if (!closing)
            CloseMotion(FinishClose);
    }

    private void FinishClose()
    {
        closing = false;
        foreach (RectTransform ghost in ghosts)
            if (ghost)
                Object.Destroy(ghost.gameObject);
        ghosts.Clear();
        was = null;
        LockCursor();
        open = false;
        layer = Layer.Main;
        if (root)
            Object.Destroy(root);
        root = content = null;
        backdrop.Release();
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
        // Not Input.anyKeyDown: on this game it also reports a controller press, a frame or two after Pad does, which
        // would finish the animation that press just started.
        if ((Pad.Held & ~Pad.Previous) != PadButtons.None || KeyboardUsed())
            motion.FinishAll();
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
                {
                    if (tab == Tab.Teams && inMembers)
                    {
                        inMembers = false;
                        dirty = true;
                    }
                    else
                        Close();
                }
                break;
            case Layer.Picker: PickerInput(now); break;
            case Layer.WeaponFilter: WeaponFilterInput(now); break;
            case Layer.ArtifactSort: ArtifactSortInput(now); break;
            case Layer.SetFilter: SetFilterInput(now); break;
            case Layer.Chooser: ChooserInput(now); break;
            case Layer.ChooserFilter: ChooserFilterInput(now); break;
            case Layer.Confirm:
                if (Pressed(PadButtons.A, KeyCode.Return))
                    Confirm();
                else if (Pressed(PadButtons.B, KeyCode.Escape))
                    Back();
                break;
            case Layer.Menu: MenuInput(now); break;
            case Layer.Rename: RenameInput(); break;
            case Layer.Delete: DeleteInput(now); break;
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
        else if (Pad.Pressed(PadButtons.Y))
            undoOrCreate();
        else if (Input.GetKeyDown(KeyCode.N))
            Create();
        else if (Pressed(PadButtons.View, KeyCode.G))
            SaveEquipped();
        else if (Pressed(PadButtons.LeftStick, KeyCode.C))
            OpenChooser();
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

    // Up and down choose a team; A goes into its members, where left and right choose a member and up and down change
    // that member's template.
    private void TeamsInput(float now)
    {
        if (!inMembers && Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MoveTeam(-1);
        else if (!inMembers && Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MoveTeam(1);
        else if (!inMembers && (Pressed(PadButtons.A, KeyCode.Return) || Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now)))
        {
            if (CurrentTeam != null)
                inMembers = true;
            dirty = true;
        }
        else if (inMembers && Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
        {
            if (member == 0)
                inMembers = false;
            else
                MoveMember(-1);
            dirty = true;
        }
        else if (inMembers && Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
            MoveMember(1);
        else if (inMembers && Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            ChangeMemberTemplate(-1);
        else if (inMembers && Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            ChangeMemberTemplate(1);
        else if (Pressed(PadButtons.X, KeyCode.F))
            AskApply();
        else if (Pad.Pressed(PadButtons.Y))
        {
            if (undo.Count > 0) Undo(); else SaveParty();
        }
        else if (Input.GetKeyDown(KeyCode.N))
            SaveParty();
        else if (Pressed(PadButtons.Menu, KeyCode.R) && CurrentTeam != null)
            OpenMenu();
    }

    private void PickerInput(float now)
    {
        int Columns = PickerColumns;
        if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
            MovePicker(-1);
        else if (Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
            MovePicker(1);
        else if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MovePicker(-Columns);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MovePicker(Columns);
        else if (Pressed(PadButtons.X, KeyCode.F))
            ToggleFree();
        else if (Pressed(PadButtons.Y, KeyCode.N))
            OpenFilter();
        else if (Pressed(PadButtons.View, KeyCode.G))
            OpenSort();
        else if (Pressed(PadButtons.A, KeyCode.Return))
            Pick();
        else if (Pressed(PadButtons.B, KeyCode.Escape))
            Back();
    }

    private void WeaponFilterInput(float now)
    {
        if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MovePanel(-1, WeaponAttributes.Length);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MovePanel(1, WeaponAttributes.Length);
        else if (Pressed(PadButtons.A, KeyCode.Return))
            ToggleSecondary(WeaponAttributes[panelCursor].Key);
        else
            PanelActions(() => weaponQuery.Secondary.Clear(), () => { weaponQuery.Secondary.Clear(); weaponQuery.Secondary.UnionWith(savedChoice); });
    }

    private void ArtifactSortInput(float now)
    {
        if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MovePanel(-1, SortAffixes.Length);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MovePanel(1, SortAffixes.Length);
        else if (Pressed(PadButtons.A, KeyCode.Return))
            ToggleSort(SortAffixes[panelCursor].Key);
        else
            PanelActions(() => SortList().Clear(), () => { SortList().Clear(); SortList().AddRange(savedChoice); });
    }

    private void SetFilterInput(float now)
    {
        if (Repeat(PadButtons.DpadUp, KeyCode.UpArrow, now))
            MovePanel(-2, setRows.Count, false);
        else if (Repeat(PadButtons.DpadDown, KeyCode.DownArrow, now))
            MovePanel(2, setRows.Count, false);
        else if (Repeat(PadButtons.DpadLeft, KeyCode.LeftArrow, now))
            MovePanel(-1, setRows.Count, false);
        else if (Repeat(PadButtons.DpadRight, KeyCode.RightArrow, now))
            MovePanel(1, setRows.Count, false);
        else if (Pressed(PadButtons.A, KeyCode.Return) && panelCursor < setRows.Count)
            ToggleSet(setRows[panelCursor].Id);
        else
            PanelActions(() => artifactQuery.Sets.Clear(), () => { artifactQuery.Sets.Clear(); artifactQuery.Sets.UnionWith(savedChoice.Select(uint.Parse)); });
    }

    // X keeps the choice, B puts back what it was when the panel opened, and L3 clears it.
    private void PanelActions(Action reset, Action restore)
    {
        if (Pressed(PadButtons.X, KeyCode.F))
            ClosePanel();
        else if (Pressed(PadButtons.LeftStick, KeyCode.Backspace))
        {
            reset();
            if (layer == Layer.ChooserFilter) FillRoster(); else FillPicker();
            dirty = true;
        }
        else if (Pressed(PadButtons.B, KeyCode.Escape))
        {
            restore();
            ClosePanel();
        }
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
            if (menuIndex == 0) StartRename(); else AskDelete();
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
        if (Pressed(PadButtons.A, KeyCode.Return))
            SaveRename();
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
            inMembers = false;
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

    private void SaveEquipped()
    {
        library.Characters.Add(new CharacterTemplate { Avatar = Current.Guid, Name = $"Template {Templates.Count + 1}", Items = gear.EquippedItems(Current.Guid) });
        Save();
        template = Templates.Count - 1;
        Toast("Saved equipped gear as a template", "", kind: ToastKind.Done);
        dirty = true;
    }

    private void SaveParty()
    {
        var team_ = new TeamTemplate { Name = $"Team {library.Teams.Count + 1}" };
        foreach (ulong guid in gear.Party)
        {
            var saved = new CharacterTemplate { Avatar = guid, Name = team_.Name, Items = gear.EquippedItems(guid) };
            library.Characters.Add(saved);
            team_.Members.Add(new TeamMember { Avatar = guid, Template = saved.Id });
        }
        library.Teams.Add(team_);
        team = library.Teams.Count - 1;
        Save();
        Toast("Saved the party's equipped gear as a team", "", kind: ToastKind.Done);
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

    private PickerQuery Query => pickerSlot == Slot.Weapon ? weaponQuery : artifactQuery;

    private List<string> SortList()
    {
        if (!sortBySlot.TryGetValue(pickerSlot, out List<string> list))
            sortBySlot[pickerSlot] = list = [];
        return list;
    }

    private void OpenPicker(Slot slot)
    {
        pickerSlot = slot;
        pickerSource = [.. gear.Items(slot).Select(PickerEntry).OfType<PickerItem>()];
        if (slot == Slot.Weapon && gear.WeaponType(gear.Equipped(Current.Guid, Slot.Weapon)) is var kind and not -1)
            pickerSource = [.. pickerSource.Where(item => gear.WeaponType(item.Guid) == kind)];
        FillPicker();
        ulong chosen = Selected?.Items.GetValueOrDefault(slot) ?? 0;
        pickerCursor = Math.Max(0, pickerItems.IndexOf(chosen));
        layer = Layer.Picker;
        dirty = true;
    }

    private PickerItem PickerEntry(ulong guid)
    {
        if (gear.View(guid) is not { } view)
            return null;
        var affixes = new Dictionary<string, float>();
        foreach (ItemStat stat in view.Subs.Append(view.Main).OfType<ItemStat>())
            affixes[stat.Label] = stat.Value;
        return new PickerItem(guid, view.Level, view.Rarity, view.Set, view.Subs.FirstOrDefault()?.Label ?? "", affixes, gear.Holder(guid) != null);
    }

    private void FillPicker()
    {
        PickerQuery query = Query;
        query.Sort.Clear();
        if (pickerSlot != Slot.Weapon)
            query.Sort.AddRange(SortList());
        pickerItems = [.. Picking.Apply(pickerSource, query).Select(item => item.Guid)];
        pickerCursor = Math.Clamp(pickerCursor, 0, Math.Max(0, pickerItems.Count - 1));
    }

    private void ToggleFree()
    {
        Query.OnlyFree = !Query.OnlyFree;
        FillPicker();
        dirty = true;
    }

    private void OpenFilter()
    {
        panelCursor = 0;
        if (pickerSlot == Slot.Weapon)
        {
            savedChoice = [.. weaponQuery.Secondary];
            layer = Layer.WeaponFilter;
        }
        else
        {
            savedChoice = [.. artifactQuery.Sets.Select(set => set.ToString())];
            var counts = pickerSource.GroupBy(item => item.Set).ToDictionary(group => group.Key, group => group.Count());
            setRows = [.. ArtifactSets.All.Where(set => counts.ContainsKey(set.Id)).Select(set => (set.Id, set.Name, set.Icon, set.Bonus, counts[set.Id]))];
            setRows.AddRange(counts.Keys.Where(id => ArtifactSets.All.All(set => set.Id != id)).Select(id => (id, $"Set {id}", "", "", counts[id])));
            layer = Layer.SetFilter;
        }
        dirty = true;
    }

    private void OpenSort()
    {
        if (pickerSlot == Slot.Weapon)
            return;
        panelCursor = 0;
        savedChoice = [.. SortList()];
        layer = Layer.ArtifactSort;
        dirty = true;
    }

    private void MovePanel(int by, int count, bool wrap = true)
    {
        if (count > 0)
            panelCursor = wrap ? (panelCursor + by + count) % count : Math.Clamp(panelCursor + by, 0, count - 1);
        dirty = true;
    }

    private void ClosePanel()
    {
        if (layer == Layer.ChooserFilter)
        {
            FillRoster();
            layer = Layer.Chooser;
        }
        else
        {
            FillPicker();
            layer = Layer.Picker;
        }
        dirty = true;
    }

    private void ToggleSecondary(string key)
    {
        if (!weaponQuery.Secondary.Remove(key))
            weaponQuery.Secondary.Add(key);
        FillPicker();
        dirty = true;
    }

    private void ToggleSet(uint id)
    {
        if (!artifactQuery.Sets.Remove(id))
            artifactQuery.Sets.Add(id);
        FillPicker();
        dirty = true;
    }

    private void ToggleSort(string key)
    {
        var query = new PickerQuery();
        query.Sort.AddRange(SortList());
        if (query.ToggleSort(key))
        {
            SortList().Clear();
            SortList().AddRange(query.Sort);
        }
        else
            Toast($"Choose up to {PickerQuery.MaxSortAffixes} affixes", "");
        FillPicker();
        dirty = true;
    }

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
            Toast("A team member needs an item another member needs", "Choose another template for one of them", kind: ToastKind.Warn);
            dirty = true;
            return;
        }
        if (plan.Moves.Count == 0)
        {
            Toast("Nothing to change", plan.Missing.Count > 0 ? $"{plan.Missing.Count} items are no longer owned" : "This gear is already equipped");
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
        shownDone = -1;
        Toast("Applying", "", 600f, ToastKind.Busy);
        dirty = true;
    }

    private void Undo()
    {
        IReadOnlyList<Move> moves = undo;
        undo = [];
        gear.Refresh();
        applier.Start(moves);
        shownDone = -1;
        Toast("Undoing", "", 600f, ToastKind.Busy);
        dirty = true;
    }

    private void Finished(IReadOnlyList<Move> done, Move? failed)
    {
        gear.Refresh();
        undo = Planner.Undo(done);
        if (failed is { } refused)
            Toast($"{done.Count} items changed. The game refused the next one", $"{gear.View(refused.Item)?.Name ?? "An item"} for {gear.NameOf(refused.Avatar)}", 8f, ToastKind.Warn);
        else
        {
            var swapped = done.Where(move => move.Holder is not null).ToList();
            string detail = swapped.Count > 0 ? $"{gear.NameOf(swapped[0].Holder!.Value)} received the old item" : "";
            bool undoing = toast == "Undoing";
            if (undoing)
                undo = [];
            Toast(undoing ? "Undone" : $"Applied {done.Count} item{(done.Count == 1 ? "" : "s")}", undoing ? "" : detail, undoing ? 2.5f : 6f, ToastKind.Done);
        }
        dirty = true;
    }

    private void Toast(string text, string detail, float seconds = 2.5f, ToastKind kind = ToastKind.Info)
    {
        toastKind = kind;
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
        if (!promptRoot)
        {
            if (!font && !FindFont())
                return;
            promptRoot = NewCanvas("ReliquaryPrompt");
            ui = new Widgets(font);
            promptBox = Node("Prompt", promptRoot.transform);
            Mark(promptBox, "Y", 0f, 4f);
            Print(promptBox, "Reliquary", 30f, 4f, 110f, 24f, 15, Style.BackInk, TextAnchor.MiddleLeft);
        }
        // Left of the game's Quit Game button, wherever the screen's shape puts it.
        float scale = Math.Min(Screen.width / Style.ReferenceWidth, Screen.height / Style.ReferenceHeight);
        if (tile.QuitEdge() is { } edge)
            TopLeft(promptBox, edge.x / scale - PromptWidth, (Screen.height - edge.y) / scale - 16f, PromptWidth, 32f);
        else
            TopLeft(promptBox, 110f, 736f, PromptWidth, 32f);
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
        promptBox = null;
    }

    private void Build()
    {
        dirty = false;
        Shot now = Take();
        LeaveMotion(now);
        RememberGlides();
        hits.Clear();
        if (content)
            Object.Destroy(content);
        groups.Clear();
        parts.Clear();
        slotRects.Clear();
        marks.Clear();
        content = Node("Content", root.transform).gameObject;
        RectTransform screen = content.GetComponent<RectTransform>();
        Fill(screen);
        backdropShown = backdrop.Picture != null;
        if (backdrop.Picture is { } blurred)
        {
            RectTransform back = Node("Back", screen);
            Fill(back);
            groups["Back"] = back;
            var picture = Node("Backdrop", back).gameObject.AddComponent<RawImage>();
            picture.texture = blurred;
            picture.color = Style.White;
            picture.raycastTarget = false;
            Fill(picture.rectTransform);
        }
        RectTransform tint = Node("Tint", screen);
        Fill(tint);
        groups["Tint"] = tint;
        Fill(Picture(tint, "Tint", null, Tint).rectTransform);
        MeasureScreen();
        stage = Node("Stage", screen);
        Place(stage, V(0.5f, 0.5f), V(0.5f, 0.5f), V(0.5f, 0.5f), V(0f, 0f), V(Width, Style.ReferenceHeight));
        RectTransform rect = stage;
        switch (layer)
        {
            case Layer.Picker: DrawPicker(G("Picker")); break;
            case Layer.WeaponFilter:
                DrawPicker(G("Picker"));
                hits.Clear();
                DrawWeaponFilter(G("Panel"));
                break;
            case Layer.ArtifactSort:
                DrawPicker(G("Picker"));
                hits.Clear();
                DrawArtifactSort(G("Panel"));
                break;
            case Layer.SetFilter: DrawSetFilter(G("Sets")); break;
            case Layer.Chooser:
            case Layer.ChooserFilter:
                DrawTop(G("Top"));
                DrawCharacters(rect);
                hits.Clear();
                DrawChooser(G("Chooser"));
                if (layer == Layer.ChooserFilter)
                {
                    hits.Clear();
                    DrawChooserFilter(G("Panel"));
                }
                break;
            default:
                DrawTop(G("Top"));
                if (tab == Tab.Characters) DrawCharacters(rect); else DrawTeams(rect);
                break;
        }
        if (layer == Layer.Confirm) DrawConfirm(G("Prompt"));
        if (layer == Layer.Menu) DrawMenu(G("Prompt"));
        if (layer == Layer.Rename) DrawRename(G("Prompt"));
        if (layer == Layer.Delete) DrawDelete(G("Prompt"));
        if (toastUntil != 0f)
            DrawToast(G("Toast"));
        ArriveMotion(now);
        PlaceGhosts();
        was = now;
    }

    // The stage is 800 units tall and as wide as the screen, never narrower than 1280. On a screen wider than 16:10,
    // wide is the extra width: left groups stay at the left edge, right groups move by wide, and the middle grows.
    // On a narrower screen the stage stays 1280 wide and extraY reaches the screen's top and bottom edges.
    private void MeasureScreen()
    {
        float scale = Math.Min(Screen.width / Style.ReferenceWidth, Screen.height / Style.ReferenceHeight);
        wide = Math.Max(0f, Screen.width / scale - Style.ReferenceWidth);
        extraY = (Screen.height / scale - Style.ReferenceHeight) / 2f;
    }

    private float Width => Style.ReferenceWidth + wide;

    private void Band(Transform parent, float y, float height, Color color)
    {
        float top = y <= 0f ? y - extraY : y;
        float bottom = y + height >= Style.ReferenceHeight ? y + height + extraY : y + height;
        Box(parent, 0f, top, Width, bottom - top, color, null);
    }

    private void LeftBand(Transform parent, float y, float width, float height, Color color)
    {
        float top = y <= 0f ? y - extraY : y;
        float bottom = y + height >= Style.ReferenceHeight ? y + height + extraY : y + height;
        Box(parent, 0f, top, width, bottom - top, color, null);
    }

    private void Cover(Transform parent, Color color) => Band(parent, 0f, Style.ReferenceHeight, color);

    // One row in three zones over the columns below: the title over the sidebar, the tabs from the content's left edge,
    // and on the Characters tab the character strip ending at the stats column's right edge, with All characters as a
    // tile at its end.
    private const float ContentLeft = 296f, FaceStep = 50f, FaceSize = 42f, TileSize = 42f;

    private void DrawTop(RectTransform parent)
    {
        Band(parent, 0f, 74f, Bar);
        Image sigil = Picture(parent, "Sigil", Shapes.Sigil, Style.Cream);
        TopLeft(sigil.rectTransform, 28f, 22f, 30f, 30f);
        Print(parent, "Reliquary", 68f, 22f, 200f, 30f, 21, Style.Text, TextAnchor.MiddleLeft);
        if (padLayout)
            Mark(parent, "LT", ContentLeft, 25f);
        float x = ContentLeft + 38f;
        x += TabLabel(parent, "Characters", x, Tab.Characters) + 28f;
        x += TabLabel(parent, "Teams", x, Tab.Teams) + 14f;
        if (padLayout)
            Mark(parent, "RT", x, 25f);
        if (tab == Tab.Characters)
            DrawStrip(parent);
    }

    private void DrawStrip(RectTransform parent)
    {
        const int Shown = 9;
        float right = Width - 24f, tileX = right - TileSize, divider = tileX - 15f, rb = divider - 14f - 30f;
        Action chooser = OpenChooser;
        Box(parent, tileX, 16f, TileSize, TileSize, Style.Band, Shapes.Card);
        Image grid = Picture(parent, "Grid", Shapes.Grid, Style.Text);
        TopLeft(grid.rectTransform, tileX + 11f, 27f, 20f, 20f);
        if (padLayout)
        {
            Image badge = Picture(parent, "L3", Shapes.Face, Style.White);
            TopLeft(badge.rectTransform, tileX + TileSize - 13f, 16f + TileSize - 13f, 20f, 20f);
            Fill(ui.Label(badge.rectTransform, "Text", "L3", 8, Style.White, TextAnchor.MiddleCenter).rectTransform);
        }
        Hit(parent, tileX, 16f, TileSize, TileSize, chooser);
        Box(parent, divider, 22f, 1f, 30f, Hair, null);
        if (padLayout)
            Mark(parent, "RB", rb, 25f);
        int count = gear.Characters.Count;
        float last = rb - 10f - FaceSize, first = last - (Shown - 1) * FaceStep;
        // The faces sit in a window, so when the strip moves by one the face leaving it slides out of sight.
        RectTransform faces = Clip(parent, "Faces", first - 6f, 8f, last + FaceSize - first + 12f, 66f);
        int start = StripStart(), leaving = was != null && was.Layer == layer && Math.Abs(was.StripStart - start) == 1 ? was.StripStart - start : 0;
        for (int i = leaving < 0 ? -1 : 0; i < Math.Min(Shown, count) + (leaving > 0 ? 1 : 0); i++)
        {
            int index = start >= 0 ? start + i : (avatar + i - Math.Min(Shown / 2, avatar) + count) % count;
            if (index < 0 || index >= count)
                continue;
            Character who = gear.Characters[index];
            float at = first + i * FaceStep;
            Face(faces, who, at, 16f, FaceSize, false);
            if (index == avatar)
            {
                Image line = Box(parent, at + 4f, 16f + FaceSize + 4f, FaceSize - 8f, 3f, Style.Cream, null);
                Glide("faceline", line.rectTransform, at + 4f, 16f + FaceSize + 4f, FaceSize - 8f, 3f, CharacterMs);
            }
            int target = index;
            if (i >= 0 && i < Shown)
                Hit(parent, at, 16f, FaceSize, FaceSize, () => { avatar = target; template = 0; inSlots = false; });
        }
        if (padLayout)
            Mark(parent, "LB", first - 10f - 30f, 25f);
    }

    // The first character in the strip's window of nine, or -1 when everyone fits and the strip wraps around instead.
    private int StripStart()
    {
        const int Shown = 9;
        int count = gear.Characters.Count;
        if (count <= Shown)
            return -1;
        return avatar < Shown / 2 ? 0 : avatar >= count - Shown / 2 ? count - Shown : avatar - Shown / 2;
    }

    // Returns the label's width, which the underline spans.
    private float TabLabel(RectTransform parent, string text, float x, Tab which)
    {
        Text label = Print(parent, text, x, 22f, 160f, 30f, 16, which == tab ? Style.Text : Style.Dim, TextAnchor.MiddleLeft);
        float width = label.preferredWidth > 0f ? label.preferredWidth : text.Length * 11f;
        if (which == tab)
        {
            Image line = Box(parent, x, 56f, width, 3f, Style.Cream, null);
            Glide("underline", line.rectTransform, x, 56f, width, 3f, TabMs);
        }
        Hit(parent, x, 16f, width, 42f, () => SwitchTab(which));
        return width;
    }

    private void DrawCharacters(RectTransform parent)
    {
        RectTransform side = G("Side");
        LeftBand(side, 74f, 276f, 666f, Bar);
        RectTransform rows = Sub(side, "Rows");
        List<CharacterTemplate> list = Templates;
        float y = 80f;
        for (int i = 0; i < list.Count && y < 590f; i++)
        {
            CharacterTemplate item = list[i];
            float height = DrawTemplateRow(rows, item, y, i == template && !inSlots ? Selection.Focus : i == template ? Selection.Current : Selection.None);
            int target = i;
            Hit(rows, 0f, y, 276f, height, () => { template = target; inSlots = false; });
            y += height;
        }
        if (list.Count == 0)
            Print(rows, "No templates yet", 28f, 96f, 230f, 20f, 13, Style.Dim, TextAnchor.MiddleLeft);
        Action create = Create, saveEquipped = SaveEquipped;
        Button_(side, "Create new template", "Y", 14f, 640f, 248f, 40f, true, create);
        Button_(side, "Save equipped gear as template", "View", 14f, 688f, 248f, 40f, false, saveEquipped);

        RectTransform main = G("Main");
        if (Selected is { } current)
        {
            Print(main, current.Name, 296f, 90f, 420f, 28f, 22, Style.Text, TextAnchor.MiddleLeft);
            Print(main, Current.Name, 300f + current.Name.Length * 13f, 96f, 200f, 18f, 13, Style.Dim, TextAnchor.MiddleLeft);
            float slotWidth = 208f + wide / 3f;
            for (int i = 0; i < SlotOrder.Length; i++)
            {
                float x = 296f + (i % 3) * (slotWidth + 10f), top = 128f + (i / 3) * 298f;
                slotRects[i] = new Vector4 { x = x, y = top, z = slotWidth, w = 288f };
                DrawSlot(Sub(main, $"Card{i}"), current, SlotOrder[i], x, top, slotWidth, i);
            }
            DrawCompare(Sub(main, "Compare"), current);
        }
        else
        {
            float left = layer is Layer.Chooser or Layer.ChooserFilter ? 480f : 296f;
            Print(main, $"Create a template for {Current.Name}", left, 340f, 956f + wide - left, 30f, 18, Style.Dim, TextAnchor.MiddleCenter);
        }
        if (layer is not (Layer.Chooser or Layer.ChooserFilter))
            DrawFooter(G("Foot"), inSlots ? "Change item" : "Open template");
    }

    private const int PickerRows = 4;

    // Seven columns of 120 units on a 16:10 screen, and one more for each 120 units a wider screen adds.
    private int PickerColumns => 7 + (int)(wide / 120f);
    private const float PromptWidth = 136f;
    private static readonly (string Key, string Label)[] WeaponAttributes =
    [
        ("CRIT Rate", "CRIT Rate"), ("CRIT DMG", "CRIT DMG"), ("ATK%", "ATK Percentage"), ("DEF%", "DEF Percentage"), ("HP%", "HP Percentage"),
        ("Elemental Mastery", "Elemental Mastery"), ("Energy Recharge", "Energy Recharge"), ("Physical DMG Bonus", "Physical DMG Bonus"),
    ];
    private static readonly (string Key, string Label)[] SortAffixes =
    [
        ("CRIT DMG", "CRIT DMG"), ("CRIT Rate", "CRIT Rate"), ("ATK%", "ATK Percentage"), ("ATK", "ATK"), ("DEF", "DEF"), ("DEF%", "DEF Percentage"),
        ("HP", "HP"), ("HP%", "HP Percentage"), ("Elemental Mastery", "Elemental Mastery"), ("Energy Recharge", "Energy Recharge"),
    ];

    private enum Selection { None, Current, Focus }

    private float DrawTemplateRow(RectTransform parent, CharacterTemplate item, float y, Selection selection)
    {
        var (notes, warn) = TemplateNote(item);
        bool equipped = item.Items.Count > 0 && item.Items.All(pair => gear.Equipped(item.Avatar, pair.Key) == pair.Value);
        float height = 48f + (notes.Length > 0 ? 18f : 0f);
        if (selection != Selection.None)
            Highlight(parent, y, height);
        if (selection == Selection.Focus)
            Pointer(parent, y);
        Fit(parent, item.Name + (equipped ? "   (Equipped)" : ""), 28f, y + 8f, 240f, 20f, 15, Style.Text);
        Fit(parent, SetSummary(item.Items.Values), 28f, y + 28f, 240f, 16f, 12, Style.Dim);
        if (notes.Length > 0)
            Print(parent, notes, 28f, y + 44f, 240f, 16f, 12, warn ? Warn : Down, TextAnchor.MiddleLeft);
        return height;
    }

    // The band behind the chosen row of a sidebar list and the arrow that marks it, both gliding to a new row.
    private void Highlight(RectTransform parent, float y, float height)
    {
        Image band = Box(parent, 0f, y, 276f, height, Style.Band, null);
        Glide($"band:{tab}", band.rectTransform, 0f, y, 276f, height, ListMs);
    }

    private void Pointer(RectTransform parent, float y)
    {
        Text arrow = Print(parent, "▸", 10f, y + 10f, 14f, 20f, 13, Style.Text, TextAnchor.MiddleLeft);
        Glide($"arrow:{tab}", arrow.rectTransform, 10f, y + 10f, 14f, 20f, ListMs);
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
        return held > 0 ? (held == 1 ? $"1 item equipped by {who}" : $"{held} items equipped by others", true) : ("", true);
    }

    private string SetSummary(IEnumerable<ulong> items)
    {
        var sets = items.Select(gear.View).Where(view => view != null && view.Set != 0).GroupBy(view => view.Set).Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count()).Select(group => $"{ArtifactSets.Name(group.Key)} {group.Count()}").ToList();
        return sets.Count > 0 ? string.Join(", ", sets) : items.Any() ? "No set bonus" : "Nothing chosen yet";
    }

    private void DrawSlot(RectTransform parent, CharacterTemplate current, Slot slot, float x, float y, float Width, int index)
    {
        const float Height = 288f;
        bool focus = inSlots && index == slotIndex;
        Box(parent, x, y, Width, Height, CardFill, Shapes.Card);
        if (focus)
            FocusFrame("frame:slot", parent, x, y, Width, Height, Style.CardRadius);
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
            Print(parent, $"Equipped by {gear.NameOf(holder)}", x + 12f, y + Height - 26f, Width - 24f, 16f, 12, Warn, TextAnchor.MiddleLeft);
    }

    // The items' own stats, without set bonuses or the character's base stats.
    private void DrawCompare(RectTransform parent, CharacterTemplate current)
    {
        float X = 960f + wide;
        const float Width = 296f;
        Print(parent, "Gear stats compared with equipped gear", X, 90f, Width, 20f, 14, Style.Cream, TextAnchor.MiddleLeft);
        var after = new Dictionary<string, (float Value, bool Percent)>();
        var before = new Dictionary<string, (float Value, bool Percent)>();
        foreach (Slot slot in SlotOrder)
        {
            ulong equipped = gear.Equipped(current.Avatar, slot);
            ulong chosen = current.Items.GetValueOrDefault(slot, equipped);
            Add(before, gear.View(equipped));
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
        string sets = SetSummary(SlotOrder.Select(slot => current.Items.GetValueOrDefault(slot, gear.Equipped(current.Avatar, slot))).Where(guid => guid != 0));
        Fit(parent, sets, X, y + 10f, Width, 18f, 13, Up);
        Action apply = AskApply;
        Button_(parent, "Apply template", "X", X, y + 40f, Width, 40f, true, apply);
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
        Band(parent, 740f, 60f, Bar);
        if (Prompting)
            return;
        if (!padLayout)
        {
            EscHint(parent, "Close");
            Action menu = OpenMenu;
            Button_(parent, "Rename or delete", "", 14f, 750f, 180f, 36f, false, menu);
            return;
        }
        Mark(parent, "Menu", 28f, 756f);
        Print(parent, "Rename or delete", 60f, 758f, 180f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        Mark(parent, "A", 1010f + wide, 756f);
        Print(parent, primary, 1040f + wide, 758f, 120f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        Mark(parent, "B", 1170f + wide, 756f);
        Print(parent, "Close", 1200f + wide, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
    }

    // The team templates in the sidebar like the character templates, and the chosen team's members beside it.
    private const int TeamRowsShown = 9;
    private const float TeamRowHeight = 64f, MemberTop = 128f, MemberHeight = 516f, MemberGap = 12f;

    private void DrawTeams(RectTransform parent)
    {
        RectTransform side = G("Side");
        // The band first: drawn after the rows, its dark fill covered them.
        LeftBand(side, 74f, 276f, 666f, Bar);
        RectTransform rows = Sub(side, "Rows"), main = G("Main"), foot = G("Foot");
        int count = library.Teams.Count;
        int first = Math.Max(0, Math.Min(team - TeamRowsShown + 1, count - TeamRowsShown));
        for (int i = Math.Max(0, first); i < count && i < first + TeamRowsShown; i++)
            DrawTeamRow(rows, library.Teams[i], i, 80f + (i - first) * TeamRowHeight);
        if (count == 0)
            Print(rows, "No team templates yet", 28f, 96f, 230f, 20f, 13, Style.Dim, TextAnchor.MiddleLeft);
        Action save = SaveParty;
        Button_(side, "Save party as team", "Y", 14f, 688f, 248f, 40f, true, save);
        parent = main;
        TeamTemplate current = CurrentTeam;
        Plan check = current == null ? null : Planner.Plan(library.Templates(current), gear);
        if (current != null)
        {
            Print(parent, current.Name, ContentLeft, 90f, 600f, 28f, 22, Style.Text, TextAnchor.MiddleLeft);
            float width = (Width - 24f - ContentLeft - 3f * MemberGap) / 4f;
            for (int i = 0; i < current.Members.Count; i++)
            {
                float x = ContentLeft + i * (width + MemberGap);
                RectTransform card = Sub(main, $"Card{i}");
                int target = i;
                // Before the card's parts, so its template arrows take their own clicks.
                Hit(card, x, MemberTop, width, MemberHeight, () => { member = target; inMembers = true; });
                DrawMember(card, current.Members[i], x, width, inMembers && i == member, check, i);
            }
            if (check.Clashes.Count > 0)
            {
                Clash clash = check.Clashes[0];
                float boxWidth = Width - 24f - ContentLeft;
                Box(parent, ContentLeft, 666f, boxWidth, 60f, Style.Hex(0xf0c26e, 0.14f), Shapes.Card);
                Print(parent, $"{gear.NameOf(clash.First)} and {gear.NameOf(clash.Second)} both need {gear.View(clash.Item)?.Name ?? "one item"}", ContentLeft + 16f, 676f, boxWidth - 32f, 20f, 14, Warn, TextAnchor.MiddleLeft);
                Print(parent, "Choose another template for one of them. The team cannot be applied until then.", ContentLeft + 16f, 698f, boxWidth - 32f, 18f, 12, Style.Dim, TextAnchor.MiddleLeft);
            }
        }
        else
            Print(parent, "Save your party's equipped gear as a team to start", ContentLeft, 340f, Width - 24f - ContentLeft, 30f, 18, Style.Dim, TextAnchor.MiddleCenter);
        parent = foot;
        Band(parent, 740f, 60f, Bar);
        if (Prompting)
            return;
        Color applyInk = check is { CanApply: true } ? Style.Text : Style.Dim;
        if (padLayout)
        {
            if (current != null)
            {
                Mark(parent, "Menu", 28f, 756f);
                Print(parent, "Rename or delete", 60f, 758f, 180f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
                if (inMembers)
                {
                    Mark(parent, "UpDown", 790f + wide, 756f);
                    Print(parent, "Change template", 820f + wide, 758f, 150f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
                }
                else
                {
                    Mark(parent, "A", 830f + wide, 756f);
                    Print(parent, "Open team", 860f + wide, 758f, 120f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
                }
                Mark(parent, "X", 990f + wide, 756f);
                Print(parent, "Apply team", 1020f + wide, 758f, 120f, 24f, 14, applyInk, TextAnchor.MiddleLeft);
            }
            Mark(parent, "B", 1170f + wide, 756f);
            Print(parent, inMembers ? "Back" : "Close", 1200f + wide, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        }
        else
        {
            if (current != null)
            {
                Action menu = OpenMenu, apply = AskApply;
                Button_(parent, "Rename or delete", "", 14f, 750f, 180f, 36f, false, menu);
                Button_(parent, "Apply team", "", 960f + wide, 750f, 150f, 36f, true, apply);
            }
            EscHint(parent, inMembers ? "Back" : "Close");
        }
    }

    private void DrawTeamRow(RectTransform parent, TeamTemplate item, int index, float y)
    {
        if (index == team)
            Highlight(parent, y, TeamRowHeight);
        if (index == team && !inMembers)
            Pointer(parent, y);
        Fit(parent, item.Name, 28f, y + 8f, 240f, 20f, 15, Style.Text);
        for (int i = 0; i < item.Members.Count; i++)
            if (gear.Characters.FirstOrDefault(c => c.Guid == item.Members[i].Avatar) is { } who)
                Face(parent, who, 28f + i * 26f, y + 32f, 22f, false);
        Hit(parent, 0f, y, 276f, TeamRowHeight, () => { team = index; member = 0; inMembers = false; });
    }

    private void DrawMember(RectTransform parent, TeamMember who, float x, float Width, bool focus, Plan check, int index)
    {
        const float Top = MemberTop, Height = MemberHeight;
        Box(parent, x, Top, Width, Height, CardFill, Shapes.Card);
        if (focus)
            FocusFrame("frame:member", parent, x, Top, Width, Height, Style.CardRadius);
        Character character = gear.Characters.FirstOrDefault(c => c.Guid == who.Avatar);
        if (character != null)
            Face(parent, character, x + 14f, Top + 14f, 42f, false);
        Fit(parent, gear.NameOf(who.Avatar), x + 66f, Top + 24f, Width - 76f, 22f, 16, Style.Text);
        CharacterTemplate chosen = library.Character(who.Template);
        Box(parent, x + 14f, Top + 68f, Width - 28f, 34f, Style.Band, Shapes.Card);
        Fit(parent, chosen?.Name ?? "Keep equipped gear", x + 40f, Top + 74f, Width - 80f, 22f, 14, Style.Text);
        Print(parent, "‹", x + 22f, Top + 74f, 16f, 22f, 15, Style.Dim, TextAnchor.MiddleLeft);
        Print(parent, "›", x + Width - 38f, Top + 74f, 16f, 22f, 15, Style.Dim, TextAnchor.MiddleRight);
        TeamMember member_ = who;
        Hit(parent, x + 14f, Top + 68f, 34f, 34f, () => { member = CurrentTeam!.Members.IndexOf(member_); inMembers = true; ChangeMemberTemplate(-1); });
        Hit(parent, x + Width - 48f, Top + 68f, 34f, 34f, () => { member = CurrentTeam!.Members.IndexOf(member_); inMembers = true; ChangeMemberTemplate(1); });
        float icon = (Width - 28f - 12f) / 3f;
        for (int i = 0; i < SlotOrder.Length; i++)
        {
            ulong guid = chosen?.Items.GetValueOrDefault(SlotOrder[i]) ?? gear.Equipped(who.Avatar, SlotOrder[i]);
            float cx = x + 14f + (i % 3) * (icon + 6f), cy = Top + 114f + (i / 3) * (icon + 6f);
            ItemView view = guid == 0 ? null : gear.View(guid);
            if (view != null)
                Icon_(parent, view, cx, cy, icon);
            else
                Box(parent, cx, cy, icon, icon, Style.Band, Shapes.Card);
            if (guid != 0 && check.Clashes.Any(clash => clash.Item == guid))
                Frame(parent, cx, cy, icon, icon, Warn);
        }
        float y = Top + 114f + 2f * icon + 6f + 16f;
        IEnumerable<ulong> items = SlotOrder.Select(slot => chosen?.Items.GetValueOrDefault(slot, gear.Equipped(who.Avatar, slot)) ?? gear.Equipped(who.Avatar, slot)).Where(guid => guid != 0);
        Fit(parent, SetSummary(items), x + 14f, y, Width - 28f, 18f, 12, Up);
        y += 24f;
        var totals = new Dictionary<string, (float, bool)>();
        foreach (ulong guid in items)
            Add(totals, gear.View(guid));
        foreach (var (label, (value, percent)) in totals.OrderBy(pair => Rank(pair.Key)).Take(7))
        {
            Print(parent, label, x + 14f, y, Width - 90f, 18f, 13, Style.Text, TextAnchor.MiddleLeft);
            Print(parent, Stats.Format(value, percent), x + 14f, y, Width - 28f, 18f, 13, Style.Dim, TextAnchor.MiddleRight);
            y += 22f;
        }
    }

    private void DrawPicker(RectTransform parent)
    {
        int Columns = PickerColumns;
        const int Rows = PickerRows;
        const float CardWidth = 112f, CardHeight = 146f, Gap = 8f;
        Band(parent, 0f, 74f, Bar);
        Print(parent, Selected?.Name ?? "", 28f, 22f, 300f, 30f, 21, Style.Text, TextAnchor.MiddleLeft);
        Print(parent, $"{pickerSlot} for {Current.Name}", 340f, 26f, 400f, 24f, 16, Style.Cream, TextAnchor.MiddleLeft);
        Fit(parent, PickerState(), 28f, 80f, 830f + wide, 20f, 13, Style.Dim);
        if (pickerItems.Count == 0)
            Print(parent, "No items match", 28f, 340f, 830f + wide, 24f, 16, Style.Dim, TextAnchor.MiddleCenter);
        int firstRow = PickerFirst(), first = firstRow * Columns;
        // A row that scrolled out of sight is drawn once more, so it can slide away under the window's edge.
        int leaving = was != null && Base(was.Layer) == Layer.Picker && Math.Abs(was.PickerFirst - firstRow) == 1 ? was.PickerFirst - firstRow : 0;
        RectTransform grid = Clip(parent, "Grid", 0f, 98f, Style.ReferenceWidth + wide, Rows * (CardHeight + Gap) + 2f);
        // Who holds an item goes on a layer above the focus frame, which would otherwise cross their portrait.
        var holderRows = new List<RectTransform>();
        for (int localRow = leaving < 0 ? -1 : 0; localRow < Rows + (leaving > 0 ? 1 : 0); localRow++)
        {
            RectTransform cards = Sub(grid, $"PickerRow{localRow + 1}");
            RectTransform holders = Node($"Holders{localRow + 1}", cards);
            holderRows.Add(holders);
            for (int column = 0; column < Columns; column++)
            {
                int i = first + localRow * Columns + column;
                if (i < 0 || i >= pickerItems.Count || gear.View(pickerItems[i]) is not { } view)
                    continue;
                float x = 28f + column * (CardWidth + Gap), y = 106f + localRow * (CardHeight + Gap);
                ItemCard(cards, view, x, y, CardWidth, CardHeight, false, holders);
                int target = i;
                if (localRow >= 0 && localRow < Rows)
                    Hit(cards, x, y, CardWidth, CardHeight, () => { pickerCursor = target; Pick(); });
            }
        }
        if (pickerCursor >= first && pickerCursor < first + Columns * Rows)
        {
            int local = pickerCursor - first;
            FocusFrame("frame:picker", parent, 28f + (local % Columns) * (CardWidth + Gap), 106f + (local / Columns) * (CardHeight + Gap), CardWidth, CardHeight, Style.CardRadius);
        }
        RectTransform portraits = Clip(parent, "GridHolders", 0f, 98f, Style.ReferenceWidth + wide, Rows * (CardHeight + Gap) + 2f);
        foreach (RectTransform holders in holderRows)
        {
            holders.SetParent(portraits, false);
            Fill(holders);
            groups[holders.name] = holders;
        }
        float Side = 878f + wide;
        const float Width = 378f;
        RectTransform board = parent;
        parent = Sub(parent, "Detail");
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
                Print(parent, $"Equipped by {gear.NameOf(holder)}", Side + 14f, 440f, Width - 28f, 18f, 13, Warn, TextAnchor.MiddleLeft);
                Print(parent, $"Applying gives {gear.NameOf(holder)} {Current.Name}'s equipped {pickerSlot.ToString().ToLowerInvariant()}", Side + 14f, 462f, Width - 28f, 18f, 12, Style.Dim, TextAnchor.MiddleLeft);
            }
            Action pick = Pick;
            Button_(parent, "Use in this template", "A", Side, 504f, Width, 42f, true, pick);
        }
        parent = board;
        Band(parent, 740f, 60f, Bar);
        bool weapon = pickerSlot == Slot.Weapon;
        string freeText = Query.OnlyFree ? "Show all items" : "Unequipped items";
        if (padLayout)
        {
            float x = 28f;
            Mark(parent, "Y", x, 756f);
            Print(parent, "Filter", x + 32f, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            x += 96f;
            if (!weapon)
            {
                Mark(parent, "View", x, 756f);
                Print(parent, "Sort", x + 40f, 758f, 50f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
                x += 92f;
            }
            Mark(parent, "X", x, 756f);
            Print(parent, freeText, x + 32f, 758f, 180f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "A", 1010f + wide, 756f);
            Print(parent, "Use in template", 1040f + wide, 758f, 130f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "B", 1170f + wide, 756f);
            Print(parent, "Back", 1200f + wide, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        }
        else
        {
            Action filter = OpenFilter, sort = OpenSort, free = ToggleFree, pick = Pick;
            float x = 24f;
            Button_(parent, "Filter", "", x, 750f, 90f, 36f, false, filter);
            x += 100f;
            if (!weapon)
            {
                Button_(parent, "Sort", "", x, 750f, 80f, 36f, false, sort);
                x += 90f;
            }
            Button_(parent, freeText, "", x, 750f, 180f, 36f, false, free);
            Button_(parent, "Use in template", "", 990f + wide, 750f, 160f, 36f, true, pick);
            EscHint(parent, "Back");
        }
    }

    // The first row of items the picker shows, which keeps the cursor off the window's top and bottom rows.
    private int PickerFirst()
    {
        int Columns = PickerColumns;
        int row = pickerCursor / Columns;
        return Math.Max(0, Math.Min(row - 1, (pickerItems.Count - 1) / Columns - PickerRows + 1));
    }

    private string PickerState()
    {
        var filters = new List<string>();
        if (pickerSlot == Slot.Weapon)
            filters.AddRange(WeaponAttributes.Where(row => weaponQuery.Secondary.Contains(row.Key)).Select(row => row.Label));
        else
            filters.AddRange(artifactQuery.Sets.Select(ArtifactSets.Name));
        string filter = filters.Count == 0 ? "None" : filters.Count <= 2 ? string.Join(", ", filters) : $"{filters[0]} and {filters.Count - 1} more";
        string sort = pickerSlot == Slot.Weapon || SortList().Count == 0 ? "Level" : string.Join(", ", SortList().Select(key => SortAffixes.First(row => row.Key == key).Label));
        return $"Filter: {filter}    Sort: {sort}    {pickerItems.Count} items{(Query.OnlyFree ? ", all unequipped" : "")}";
    }

    private void DrawWeaponFilter(RectTransform parent)
    {
        DrawPanelFrame(parent, "Weapons filter", "Secondary attributes", "");
        for (int i = 0; i < WeaponAttributes.Length; i++)
        {
            var (key, label) = WeaponAttributes[i];
            int count = pickerSource.Count(item => item.Secondary == key);
            int index = i;
            OptionRow(parent, 16f, 118f + i * 46f, 360f, label, weaponQuery.Secondary.Contains(key) ? "" : null, count.ToString(), count == 0, i == panelCursor, key, () => { panelCursor = index; ToggleSecondary(key); });
        }
        DrawPanelFooter(parent, () => { weaponQuery.Secondary.Clear(); FillPicker(); });
    }

    private void DrawArtifactSort(RectTransform parent)
    {
        DrawPanelFrame(parent, "Sort order", $"Sort by affix, {pickerSlot}", $"Choose up to {PickerQuery.MaxSortAffixes}");
        for (int i = 0; i < SortAffixes.Length; i++)
        {
            var (key, label) = SortAffixes[i];
            int order = SortList().IndexOf(key);
            int index = i;
            OptionRow(parent, 16f, 140f + i * 46f, 360f, label, order >= 0 ? (order + 1).ToString() : null, "", false, i == panelCursor, key, () => { panelCursor = index; ToggleSort(key); });
        }
        DrawPanelFooter(parent, () => { SortList().Clear(); FillPicker(); });
    }

    private void DrawPanelFrame(RectTransform parent, string title, string section, string hint)
    {
        Cover(G("PanelDim"), Style.Hex(0x060a14, 0.5f));
        parent.SetAsLastSibling();
        LeftBand(parent, 0f, 392f, 800f, Style.Hex(0x202938, 0.985f));
        Print(parent, title, 22f, 34f, 340f, 26f, 18, Style.Cream, TextAnchor.MiddleLeft);
        Box(parent, 16f, 70f, 360f, 1f, Style.Hex(0xd3bc8e, 0.3f), null);
        Print(parent, section, 22f, 84f, 340f, 22f, 14, Style.Cream, TextAnchor.MiddleLeft);
        if (hint.Length > 0)
            Print(parent, hint, 22f, 108f, 340f, 20f, 12, Style.Dim, TextAnchor.MiddleLeft);
    }

    private void DrawPanelFooter(RectTransform parent, Action reset)
    {
        LeftBand(parent, 740f, 392f, 60f, Style.Hex(0x181f2c));
        if (padLayout)
        {
            Mark(parent, "L3", 40f, 756f);
            Print(parent, "Reset", 70f, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "X", 150f, 756f);
            Print(parent, "Confirm", 180f, 758f, 70f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "A", 270f, 756f);
            Print(parent, "Select", 300f, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            return;
        }
        Action confirm = ClosePanel;
        Button_(parent, "Reset", "", 80f, 750f, 100f, 36f, false, reset);
        Button_(parent, "Confirm", "", 192f, 750f, 110f, 36f, true, confirm);
    }

    // mark is null when the row is not selected, and the text to show in its circle when it is.
    private void OptionRow(RectTransform parent, float x, float y, float width, string label, string mark, string count, bool dim, bool focus, string markKey, Action click)
    {
        Color ink = dim ? Style.Hex(0xece5d8, 0.35f) : Style.Text;
        Box(parent, x, y, width, 40f, Style.Hex(0x0a101c, 0.3f), null);
        Image radio = Picture(parent, "Radio", Shapes.Knob, mark != null ? Up : Style.Hex(0xece5d8, 0.22f));
        TopLeft(radio.rectTransform, x + 12f, y + 12f, 16f, 16f);
        marks[markKey] = radio.rectTransform;
        if (!string.IsNullOrEmpty(mark))
        {
            Text number = ui.Label(radio.rectTransform, "Number", mark, 11, Style.BackInk, TextAnchor.MiddleCenter);
            Fill(number.rectTransform);
        }
        Print(parent, label, x + 38f, y + 9f, width - 90f, 22f, 14, ink, TextAnchor.MiddleLeft);
        Print(parent, count, x, y + 9f, width - 12f, 22f, 14, ink, TextAnchor.MiddleRight);
        if (focus)
            FocusFrame("frame:option", parent, x, y, width, 40f);
        Hit(parent, x, y, width, 40f, click);
    }

    // The first row of sets in view, which keeps the cursor near the middle of the list.
    private int SetFirstRow()
    {
        const int PerColumn = 14;
        return Math.Max(0, Math.Min(panelCursor / 2 - PerColumn / 2, (setRows.Count + 1) / 2 - PerColumn));
    }

    private void DrawSetFilter(RectTransform parent)
    {
        const int PerColumn = 14;
        const float Left = 64f, Top = 86f, RowHeight = 45f;
        float ColumnWidth = 411f + wide / 2f;
        Cover(parent, Style.Hex(0x151c2a, 0.97f));
        Print(parent, "Artifact set filter", 64f, 24f, 500f, 24f, 17, Style.Cream, TextAnchor.MiddleLeft);
        Print(parent, "Artifacts that meet the requirements will be shown", 64f, 48f, 600f, 18f, 12, Style.Dim, TextAnchor.MiddleLeft);
        Box(parent, 64f, 72f, 1152f + wide, 1f, Style.Hex(0xd3bc8e, 0.3f), null);
        int firstRow = SetFirstRow();
        int leaving = was != null && was.Layer == Layer.SetFilter && Math.Abs(was.SetFirst - firstRow) == 1 ? was.SetFirst - firstRow : 0;
        // Row backgrounds, then the light band on the focused row gliding between them, then the rows' contents.
        RectTransform backs = Clip(parent, "SetRowsBack", Left, Top, 2f * ColumnWidth + 14f, PerColumn * RowHeight);
        int from = (firstRow + (leaving < 0 ? -1 : 0)) * 2, to = (firstRow + PerColumn + (leaving > 0 ? 1 : 0)) * 2;
        for (int i = Math.Max(0, from); i < setRows.Count && i < to; i++)
            Box(backs, Left + (i % 2) * (ColumnWidth + 14f), Top + (i / 2 - firstRow) * RowHeight, ColumnWidth, 38f, Style.Hex(0x283448, 0.6f), null);
        if (panelCursor < setRows.Count)
        {
            float bandX = Left + (panelCursor % 2) * (ColumnWidth + 14f), bandY = Top + (panelCursor / 2 - firstRow) * RowHeight;
            Image band = Box(parent, bandX, bandY, ColumnWidth, 38f, Paper, null);
            Glide("band:sets", band.rectTransform, bandX, bandY, ColumnWidth, 38f, 120f);
        }
        RectTransform rows = Clip(parent, "SetRows", Left, Top, 2f * ColumnWidth + 14f, PerColumn * RowHeight);
        for (int i = Math.Max(0, from); i < setRows.Count && i < to; i++)
        {
            var set = setRows[i];
            float x = Left + (i % 2) * (ColumnWidth + 14f), y = Top + (i / 2 - firstRow) * RowHeight;
            bool focus = i == panelCursor, selected = artifactQuery.Sets.Contains(set.Id);
            Color ink = focus ? PaperInk : Style.Text;
            Image radio = Picture(rows, "Radio", Shapes.Knob, selected ? Up : focus ? Style.Hex(0x3f3b30, 0.25f) : Style.Hex(0xece5d8, 0.22f));
            TopLeft(radio.rectTransform, x + 12f, y + 11f, 16f, 16f);
            marks[set.Id.ToString()] = radio.rectTransform;
            if (icons.Small(set.Icon) is { } small)
            {
                var icon = Node("Icon", rows).gameObject.AddComponent<RawImage>();
                icon.texture = small;
                icon.raycastTarget = false;
                TopLeft(icon.rectTransform, x + 34f, y + 4f, 30f, 30f);
            }
            Fit(rows, set.Name, x + 70f, y + 8f, ColumnWidth - 120f, 22f, 14, ink);
            Print(rows, set.Count.ToString(), x, y + 8f, ColumnWidth - 12f, 22f, 14, ink, TextAnchor.MiddleRight);
            int index = i;
            uint id = set.Id;
            if (i / 2 >= firstRow && i / 2 < firstRow + PerColumn)
                Hit(rows, x, y, ColumnWidth, 38f, () => { panelCursor = index; ToggleSet(id); });
        }
        RectTransform page = parent;
        parent = Sub(parent, "SetCard");
        if (panelCursor < setRows.Count)
        {
            var shown = setRows[panelCursor];
            float CardX = 920f + wide;
            const float CardWidth = 296f;
            Box(parent, CardX, Top, CardWidth, 636f, Paper, null);
            Box(parent, CardX, Top, CardWidth, 38f, Style.Hex(0xa35d2c), null);
            Fit(parent, shown.Name, CardX + 14f, Top + 6f, CardWidth - 28f, 26f, 15, Style.White);
            Box(parent, CardX, Top + 38f, CardWidth, 110f, Style.Hex(0x8a6340), null);
            if (icons.Get(shown.Icon) is { } sprite)
            {
                Image art = Picture(parent, "Art", null, Style.White);
                art.sprite = sprite;
                art.preserveAspect = true;
                TopLeft(art.rectTransform, CardX + CardWidth - 110f, Top + 42f, 100f, 100f);
            }
            float y = Top + 160f;
            Fit(parent, shown.Name, CardX + 14f, y, CardWidth - 28f, 20f, 14, Style.Hex(0x4f9a4c));
            y += 24f;
            foreach (string line in shown.Bonus.Split(" | "))
            {
                float height = (line.Length / 36 + 1) * 18f;
                Wrap(parent, line, CardX + 14f, y, CardWidth - 28f, height, 13, PaperInk, TextAnchor.UpperLeft);
                y += height + 8f;
            }
        }
        parent = page;
        Band(parent, 740f, 60f, Bar);
        if (padLayout)
        {
            Mark(parent, "L3", 28f, 756f);
            Print(parent, "Reset", 58f, 758f, 80f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "X", 890f + wide, 756f);
            Print(parent, "Confirm filter", 920f + wide, 758f, 120f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "A", 1060f + wide, 756f);
            Print(parent, "Select", 1090f + wide, 758f, 70f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
            Mark(parent, "B", 1170f + wide, 756f);
            Print(parent, "Leave", 1200f + wide, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
        }
        else
        {
            Action confirm = ClosePanel;
            Button_(parent, "Reset", "", 24f, 750f, 100f, 36f, false, () => { artifactQuery.Sets.Clear(); FillPicker(); });
            Button_(parent, "Confirm filter", "", 960f + wide, 750f, 150f, 36f, true, confirm);
            EscHint(parent, "Leave");
        }
    }

    // Parts

    private Text Print(Transform parent, string text, float x, float y, float width, float height, int size, Color color, TextAnchor alignment)
    {
        Text label = ui.Label(parent, "Text", text ?? "", size, color, alignment);
        TopLeft(label.rectTransform, x, y, width, height);
        return label;
    }

    // One line that shrinks to stay inside its box, for text as long as a set name.
    private void Fit(Transform parent, string text, float x, float y, float width, float height, int size, Color color)
    {
        Text label = Print(parent, text, x, y, width, height, size, color, TextAnchor.MiddleLeft);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 8;
        label.resizeTextMaxSize = size;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
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

    // A 2-unit outline just outside (x, y, width, height). Its edges stretch with one node, so it can glide and resize.
    private static RectTransform Frame(Transform parent, float x, float y, float width, float height, Color? color = null)
    {
        Color ink = color ?? Style.White;
        RectTransform frame = Node("Frame", parent);
        TopLeft(frame, x - 2f, y - 2f, width + 4f, height + 4f);
        Place(Picture(frame, "Top", null, ink).rectTransform, V(0f, 1f), V(1f, 1f), V(0.5f, 1f), V(0f, 0f), V(0f, 2f));
        Place(Picture(frame, "Bottom", null, ink).rectTransform, V(0f, 0f), V(1f, 0f), V(0.5f, 0f), V(0f, 0f), V(0f, 2f));
        Place(Picture(frame, "Left", null, ink).rectTransform, V(0f, 0f), V(0f, 1f), V(0f, 0.5f), V(0f, 0f), V(2f, 0f));
        Place(Picture(frame, "Right", null, ink).rectTransform, V(1f, 0f), V(1f, 1f), V(1f, 0.5f), V(0f, 0f), V(2f, 0f));
        return frame;
    }

    private void Hit(Transform parent, float x, float y, float width, float height, Action click)
    {
        RectTransform rect = Node("Hit", parent);
        TopLeft(rect, x, y, width, height);
        hits.Add((rect, click));
    }

    // The Esc keycap and what it does, at the right end of the footer.
    private void EscHint(Transform parent, string action)
    {
        Image cap = Picture(parent, "Key", Shapes.Key, Style.White);
        TopLeft(cap.rectTransform, 1170f + wide, 758f, 36f, Style.KeyHeight);
        Text letter = ui.Label(cap.rectTransform, "Text", "Esc", Style.KeySize, Style.BumperInk, TextAnchor.MiddleCenter);
        Fill(letter.rectTransform);
        Print(parent, action, 1212f + wide, 758f, 60f, 24f, 14, Style.Text, TextAnchor.MiddleLeft);
    }

    private sealed record MarkLook(Shape Shape, string Text = null, Color Ink = default, int Size = 0);

    private static readonly Dictionary<string, MarkLook> Marks = new()
    {
        ["A"] = new(Shapes.Face, "A", Style.XboxA, Style.FaceSize),
        ["B"] = new(Shapes.Face, "B", Style.XboxB, Style.FaceSize),
        ["X"] = new(Shapes.Face, "X", Style.XboxX, Style.FaceSize),
        ["Y"] = new(Shapes.Face, "Y", Style.XboxY, Style.FaceSize),
        ["L3"] = new(Shapes.Face, "L3", Style.White, Style.BumperSize),
        ["LB"] = new(Shapes.Bumper, "LB", Style.BumperInk, Style.BumperSize),
        ["RB"] = new(Shapes.Bumper, "RB", Style.BumperInk, Style.BumperSize),
        ["LT"] = new(Shapes.Trigger, "LT", Style.BumperInk, Style.BumperSize),
        ["RT"] = new(Shapes.Trigger, "RT", Style.BumperInk, Style.BumperSize),
        ["Menu"] = new(Shapes.Menu),
        ["View"] = new(Shapes.View),
        ["UpDown"] = new(Shapes.DpadUpDown),
    };

    // A controller glyph in CameraTools' look, centered in a 24 unit tall row whose top is y.
    private void Mark(Transform parent, string name, float x, float y)
    {
        MarkLook look = Marks[name];
        Image image = Picture(parent, name, look.Shape, Style.White);
        TopLeft(image.rectTransform, x, y + (24f - look.Shape.Height) / 2f, look.Shape.Width, look.Shape.Height);
        if (look.Text != null)
            Fill(ui.Label(image.rectTransform, "Text", look.Text, look.Size, look.Ink, TextAnchor.MiddleCenter).rectTransform);
    }

    // The character's portrait clipped to a circle, or their initials when the icon table does not know them.
    private void Face(Transform parent, Character who, float x, float y, float size, bool focus)
    {
        Color fill = ElementColors[Math.Clamp(who.Element, 0, ElementColors.Length - 1)];
        Image backing = Picture(parent, "Backing", Shapes.Knob, new Color { r = fill.r * 0.55f, g = fill.g * 0.55f, b = fill.b * 0.55f, a = 1f });
        TopLeft(backing.rectTransform, x, y, size, size);
        if ((icons.Small(who.Icon + "_Circle") ?? icons.Small(who.Icon)) is { } small)
        {
            Image clip = Picture(parent, "Clip", Shapes.Knob, Style.White);
            TopLeft(clip.rectTransform, x, y, size, size);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = Node("Portrait", clip.rectTransform).gameObject.AddComponent<RawImage>();
            portrait.texture = small;
            portrait.raycastTarget = false;
            Fill(portrait.rectTransform);
        }
        else
        {
            Text label = ui.Label(backing.rectTransform, "Text", who.Initials, (int)(size * 0.36f), Style.White, TextAnchor.MiddleCenter);
            Fill(label.rectTransform);
        }
        if (focus)
            Box(parent, x + 4f, y + size + 4f, size - 8f, 3f, Style.Cream, null);
    }

    // The game's own item card: art on a rarity plate, stars on its lower edge, level on a light strip, the lock and
    // refinement in the top left and who has it equipped in the top right.
    private void ItemCard(Transform parent, ItemView view, float x, float y, float width, float height, bool focus, Transform holderLayer = null)
    {
        Box(parent, x, y, width, height, Style.Strip, Shapes.Card);
        Icon_(parent, view, x, y, width);
        Text stars = Print(parent, new string('\u2605', Math.Clamp(view.Rarity, 0, 5)), x, y + width - 22f, width, 20f, 15, Style.Star, TextAnchor.MiddleCenter);
        stars.gameObject.AddComponent<Outline>().effectColor = Style.StarOutline;
        Print(parent, view.Slot == Slot.Weapon ? $"Lv. {view.Level}" : $"+{view.Level}", x, y + width, width, height - width, 15, Style.StripInk, TextAnchor.MiddleCenter);
        float badge = y + 4f;
        if (view.Slot == Slot.Weapon)
        {
            Box(parent, x + 4f, badge, 18f, 18f, view.Refinement >= 5 ? Style.Hex(0xd98a2f) : Style.Hex(0x3c3f4a, 0.92f), Shapes.Badge);
            Print(parent, view.Refinement.ToString(), x + 4f, badge, 18f, 18f, 12, Style.White, TextAnchor.MiddleCenter);
            badge += 21f;
        }
        if (view.Locked)
        {
            Box(parent, x + 4f, badge, 18f, 18f, Style.Hex(0xe9e5dc), Shapes.Badge);
            Image padlock = Picture(parent, "Lock", Shapes.LockBody, Style.Hex(0xc6463c));
            TopLeft(padlock.rectTransform, x + 7f, badge + 2f, 12f, 14f);
        }
        if (gear.Holder(view.Guid) is { } holder && gear.Characters.FirstOrDefault(c => c.Guid == holder) is { } who)
            Face(holderLayer ?? parent, who, x + width - 30f, y - 6f, 34f, false);
        if (focus)
            Frame(parent, x, y, width, height);
    }

    private void Icon_(Transform parent, ItemView view, float x, float y, float size, float alpha = 1f)
    {
        int rarity = Math.Clamp(view.Rarity, 0, Shapes.Art.Length - 1);
        Image art = Picture(parent, "Art", Shapes.Art[rarity], Style.WithAlpha(Style.White, alpha));
        art.type = Image.Type.Simple;
        TopLeft(art.rectTransform, x, y, size, size);
        if (icons.Get(view.Icon) is { } sprite)
        {
            Image image = Picture(parent, "Icon", null, Style.WithAlpha(Style.White, alpha));
            image.sprite = sprite;
            image.preserveAspect = true;
            TopLeft(image.rectTransform, x, y, size, size);
        }
    }

    private void Button_(Transform parent, string text, string glyph, float x, float y, float width, float height, bool primary, Action click, Color? outline = null, Color? fill = null)
    {
        Box(parent, x, y, width, height, fill ?? (primary ? Style.Light : outline == null ? Style.Band : Style.Hex(0x3f3b30, 0.12f)), Shapes.Pill);
        Color textInk = fill != null ? Style.White : primary ? Style.BackInk : outline ?? Style.Text;
        bool showGlyph = padLayout && glyph.Length > 0;
        if (showGlyph)
            Mark(parent, glyph, x + 14f, y + (height - 24f) / 2f);
        if (showGlyph)
            Fit(parent, text, x + 48f, y + (height - 18f) / 2f, width - 58f, 18f, 13, textInk);
        else
            Print(parent, text, x, y, width, height, 13, textInk, TextAnchor.MiddleCenter);
        Hit(parent, x, y, width, height, click);
    }
}
