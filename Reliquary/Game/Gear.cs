using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using MoleMole;

namespace Reliquary;

internal sealed record Character(ulong Guid, string Name, string Initials, int Element, string Icon);

internal sealed record ItemStat(string Label, float Value, bool Percent);

internal sealed record ItemView(ulong Guid, Slot Slot, uint Id, string Name, string Icon, int Rarity, int Level, int Refinement, uint Set, ItemStat? Main, IReadOnlyList<ItemStat> Subs, bool Locked);

// The game's characters and gear, read live. Items are cached only while the panel is open, so a new or leveled item
// shows the next time it opens.
internal sealed unsafe class Gear(Steps steps, Action<string> log) : IGear
{
    private const string AllAvatars = "IMHPACAOFCE";
    private const string PartyAvatars = "HLGBGJGBGGP";
    private const int ArrayData = 0x20;
    private const int IndexBound = 5_000;
    private static readonly Slot[] Slots = [Slot.Flower, Slot.Plume, Slot.Sands, Slot.Goblet, Slot.Circlet, Slot.Weapon];

    private readonly Dictionary<ulong, ulong> holders = [];
    private readonly Dictionary<ulong, ItemView> views = [];
    private readonly Dictionary<ulong, string> names = [];
    private readonly Dictionary<Slot, List<ulong>> bag = [];

    public IReadOnlyList<Character> Characters { get; private set; } = [];
    public IReadOnlyList<ulong> Party { get; private set; } = [];

    // The characters are the party's first, then the rest in the game's order.
    public void Refresh()
    {
        holders.Clear();
        views.Clear();
        bag.Clear();
        NGCHJLOGLKJ manager = Singleton<NGCHJLOGLKJ>._instance ?? throw new InvalidOperationException("no avatar manager");
        Party = UlongList(manager, PartyAvatars);
        var characters = new List<Character>();
        foreach (ulong guid in Party.Concat(UlongList(manager, AllAvatars)).Distinct())
        {
            LOPOKBINJOP actor = Actor(guid);
            if (actor == null)
                continue;
            foreach (Slot slot in Slots)
                if (actor.CCGCAHDBDEJ((KGLHOFEHMGC)slot) is var item and not 0)
                    holders[item] = guid;
            string name = actor.MFGJHJKKBKF() ?? "Character";
            names[guid] = name;
            characters.Add(new Character(guid, name, name.Length >= 2 ? name[..2] : name, (int)actor.IKILENBHAIF(), AvatarIcons.Of(actor.IBCHLEGMHHL)));
        }
        Characters = characters;
    }

    public string NameOf(ulong avatar) => names.GetValueOrDefault(avatar, "Character");

    public ulong Active()
    {
        uint id = ActorUtils.GetPlayerEntity()?.BFFBGCHHEOF() ?? 0;
        return Singleton<PIDAHDPFKBG>._instance?.DGCCNKPDKOK(id)?.TryCast<LOPOKBINJOP>()?.INIOMMGOEMI() ?? 0;
    }

    public ulong Worn(ulong avatar, Slot slot) => Actor(avatar)?.CCGCAHDBDEJ((KGLHOFEHMGC)slot) ?? 0;

    public bool Owned(ulong item) => Bag().PPIGBKFJBOC(item) != null;

    public ulong? Holder(ulong item) => holders.TryGetValue(item, out ulong avatar) ? avatar : null;

    public Dictionary<Slot, ulong> WornItems(ulong avatar)
        => Slots.Select(slot => (slot, Worn(avatar, slot))).Where(pair => pair.Item2 != 0).ToDictionary(pair => pair.slot, pair => pair.Item2);

    // The bag's items for one slot, newest first as the game lists them last.
    public IReadOnlyList<ulong> Items(Slot slot)
    {
        if (bag.Count == 0)
            FillBag();
        return bag.TryGetValue(slot, out List<ulong> items) ? items : [];
    }

    public ItemView View(ulong guid)
    {
        if (views.TryGetValue(guid, out ItemView view))
            return view;
        NMHKDGOMKIM item = Bag().PPIGBKFJBOC(guid);
        if (item == null)
            return null;
        view = item.TryCast<CJJDJJNDMEP>() is { } artifact ? ArtifactView(guid, artifact) : item.TryCast<GPBLKEHGFPG>() is { } weapon ? WeaponView(guid, weapon) : null;
        if (view != null)
            views[guid] = view;
        return view;
    }

    public int WeaponType(ulong guid) => Bag().PPIGBKFJBOC(guid)?.TryCast<GPBLKEHGFPG>()?.KNOHDKCNNMK()?.FCKOGGBOKIG() is { } type ? (int)type : -1;

    // One wear request, the same message and send path as the game's equip dialog.
    public void Send(ulong avatar, ulong item)
    {
        DHABIHPMOAH net = Singleton<DHABIHPMOAH>._instance ?? throw new InvalidOperationException("no network manager");
        net.DEDCMIKCBCJ(new MNFGEEHOHOH { OKMEHFHNFCG = avatar, GMLLANJKDMA = item }, false);
    }

    private void FillBag()
    {
        HDAGEENEGPJ source = Bag();
        foreach (MBAMMBNKDGF type in new[] { MBAMMBNKDGF.ITEM_WEAPON, MBAMMBNKDGF.ITEM_RELIQUARY })
        {
            int count = source.KPHFFOKOHJG(type), found = 0;
            for (int i = 0; i < IndexBound && found < count; i++)
            {
                if (source.IPBCBBGEKPB(type, i)?.KCJMDNBKFJE() is not { } item)
                    continue;
                found++;
                Slot slot = item.TryCast<CJJDJJNDMEP>() is { } artifact ? (Slot)artifact.AEIHPGEAMFL() : Slot.Weapon;
                if (!bag.TryGetValue(slot, out List<ulong> list))
                    bag[slot] = list = [];
                list.Add(item.INIOMMGOEMI());
            }
        }
    }

    private ItemView ArtifactView(ulong guid, CJJDJJNDMEP artifact)
    {
        DFHAIDJGOEG row = artifact.MPIHKBMLFFM();
        var subs = new List<ItemStat>();
        MAMOCEFGLOL main = artifact.MKDMDLIIOAN();
        foreach (MAMOCEFGLOL prop in Enum.GetValues<MAMOCEFGLOL>())
        {
            float value = HDAGEENEGPJ.JIHPDCIJEDK(artifact, prop);
            if (value > 0f && prop != main && Stats.Label(prop) is { } label)
                subs.Add(new ItemStat(label, value, Stats.IsPercent(prop)));
        }
        return new ItemView(guid, (Slot)artifact.AEIHPGEAMFL(), artifact.NJIHDGGMLJH(), row.HDBLPDLEKOA() ?? "", row.IOCFNIBFHHN() ?? "", (int)artifact.JODIFHIOBNF(),
            (int)artifact.HDHGOLGLCEB() - 1, 0, artifact.GDOIKLIAKIL(), new ItemStat(Stats.Label(main) ?? main.ToString(), artifact.EPGFHKBNBGA(), Stats.IsPercent(main)), subs, Locked(artifact));
    }

    private static ItemView WeaponView(ulong guid, GPBLKEHGFPG weapon)
    {
        EENHNKOEDCM row = weapon.KNOHDKCNNMK();
        var subs = new List<ItemStat>();
        ItemStat main = null;
        foreach (MAMOCEFGLOL prop in Enum.GetValues<MAMOCEFGLOL>())
        {
            float value = weapon.NANMNCGAGHI(prop);
            if (value <= 0f)
                continue;
            if (prop == MAMOCEFGLOL.FIGHT_PROP_BASE_ATTACK)
                main = new ItemStat("Base ATK", value, false);
            else if (Stats.Label(prop) is { } label)
                subs.Add(new ItemStat(label, value, Stats.IsPercent(prop)));
        }
        return new ItemView(guid, Slot.Weapon, weapon.NJIHDGGMLJH(), row.HDBLPDLEKOA() ?? "", row.IOCFNIBFHHN() ?? "", (int)row.JODIFHIOBNF(), (int)weapon.HDHGOLGLCEB(), (int)weapon.ALEMBDDBKCA() + 1, 0, main, subs, Locked(weapon));
    }

    private static bool Locked(NMHKDGOMKIM item) => item.EAGHGHNLGKD();

    // Which of the item's flags is the lock and which is the New badge, read against the game's own screens.
    public void LogFlags()
    {
        HDAGEENEGPJ source = Bag();
        foreach (MBAMMBNKDGF type in new[] { MBAMMBNKDGF.ITEM_WEAPON, MBAMMBNKDGF.ITEM_RELIQUARY })
        {
            int[] counts = new int[5];
            int total = 0;
            string sample = "";
            int count = source.KPHFFOKOHJG(type);
            for (int i = 0; i < IndexBound && total < count; i++)
            {
                if (source.IPBCBBGEKPB(type, i)?.KCJMDNBKFJE() is not { } item)
                    continue;
                total++;
                bool[] flags = [item.PNBOFDKDEKA(), item.EAGHGHNLGKD(), item.LOLNPMELBFI(), item.KFLGFINKJHP, item.PMOMDIHCFKO];
                for (int f = 0; f < flags.Length; f++)
                    if (flags[f])
                        counts[f]++;
                if (i < 6)
                    sample += $" {item.NJIHDGGMLJH()}:{string.Join("", flags.Select(flag => flag ? 1 : 0))}";
            }
            log($"{type} {total}: PNBOFDKDEKA {counts[0]} EAGHGHNLGKD {counts[1]} LOLNPMELBFI {counts[2]} KFLGFINKJHP {counts[3]} PMOMDIHCFKO {counts[4]}.{sample}");
        }
    }

    private static HDAGEENEGPJ Bag() => Singleton<HDAGEENEGPJ>._instance ?? throw new InvalidOperationException("no bag");

    private static LOPOKBINJOP Actor(ulong guid) => Singleton<PIDAHDPFKBG>._instance?.GMAFCHCGMLK(guid)?.TryCast<LOPOKBINJOP>();

    // These return a List<ulong>, a generic instance the wrappers lack.
    private static ulong[] UlongList(NGCHJLOGLKJ manager, string name)
    {
        IntPtr iter = IntPtr.Zero, method;
        while ((method = IL2CPP.il2cpp_class_get_methods(Il2CppClassPointerStore<NGCHJLOGLKJ>.NativeClassPtr, ref iter)) != IntPtr.Zero)
            if (IL2CPP.il2cpp_method_get_name_(method) == name && IL2CPP.il2cpp_method_get_param_count(method) == 0 && !IL2CPP.il2cpp_method_is_generic(method))
                break;
        if (method == IntPtr.Zero)
            throw new MissingMethodException(name);
        IntPtr exception = IntPtr.Zero;
        IntPtr list = IL2CPP.il2cpp_runtime_invoke(method, manager.Pointer, null, ref exception);
        if (exception != IntPtr.Zero || list == IntPtr.Zero)
            throw new InvalidOperationException($"{name} failed");
        nint handle = IL2CPP.il2cpp_gchandle_new(list, true);
        try
        {
            IntPtr target = IL2CPP.il2cpp_gchandle_get_target(handle);
            IntPtr type = IL2CPP.il2cpp_object_get_class(target);
            IntPtr array = Marshal.ReadIntPtr(target, (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.il2cpp_class_get_field_from_name(type, "_items")));
            int size = Marshal.ReadInt32(target, (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.il2cpp_class_get_field_from_name(type, "_size")));
            var guids = new ulong[size];
            for (int i = 0; i < size; i++)
                guids[i] = (ulong)Marshal.ReadInt64(array, ArrayData + sizeof(ulong) * i);
            return guids;
        }
        finally
        {
            IL2CPP.il2cpp_gchandle_free(handle);
        }
    }
}

internal static class Stats
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["FIGHT_PROP_HP"] = "HP", ["FIGHT_PROP_HP_PERCENT"] = "HP%", ["FIGHT_PROP_ATTACK"] = "ATK", ["FIGHT_PROP_ATTACK_PERCENT"] = "ATK%",
        ["FIGHT_PROP_DEFENSE"] = "DEF", ["FIGHT_PROP_DEFENSE_PERCENT"] = "DEF%", ["FIGHT_PROP_ELEMENT_MASTERY"] = "Elemental Mastery",
        ["FIGHT_PROP_CRITICAL"] = "CRIT Rate", ["FIGHT_PROP_CRITICAL_HURT"] = "CRIT DMG", ["FIGHT_PROP_CHARGE_EFFICIENCY"] = "Energy Recharge",
        ["FIGHT_PROP_HEAL_ADD"] = "Healing Bonus", ["FIGHT_PROP_PHYSICAL_ADD_HURT"] = "Physical DMG Bonus", ["FIGHT_PROP_FIRE_ADD_HURT"] = "Pyro DMG Bonus",
        ["FIGHT_PROP_ELEC_ADD_HURT"] = "Electro DMG Bonus", ["FIGHT_PROP_WATER_ADD_HURT"] = "Hydro DMG Bonus", ["FIGHT_PROP_GRASS_ADD_HURT"] = "Dendro DMG Bonus",
        ["FIGHT_PROP_WIND_ADD_HURT"] = "Anemo DMG Bonus", ["FIGHT_PROP_ROCK_ADD_HURT"] = "Geo DMG Bonus", ["FIGHT_PROP_ICE_ADD_HURT"] = "Cryo DMG Bonus",
    };

    private static readonly HashSet<string> Flat = ["HP", "ATK", "DEF", "Elemental Mastery"];

    public static string Label(MAMOCEFGLOL prop) => Labels.GetValueOrDefault(prop.ToString());

    public static bool IsPercent(MAMOCEFGLOL prop) => Label(prop) is { } label && !Flat.Contains(label);

    public static string Format(ItemStat stat) => Format(stat.Value, stat.Percent);

    public static string Format(float value, bool percent) => percent ? $"{value * 100f:0.0}%" : $"{value:#,0}";
}
