using System.Reflection;
using Il2CppInterop.Runtime;
using MoleMole;
using UnityEngine;

namespace Reliquary;

// The game's own inventory icons, loaded through the same call as CommonMiscs.LoadExternalSprite.
internal sealed unsafe class GameIcons(Steps steps, Action<string> log)
{
    private const string Folder = "ART/UI/Atlas/ItemIcon/";
    private const string LoadSpriteField = "NativeMethodInfoPtr_LoadExternalSprite_Public_Static_Sprite_String_UInt32_0";
    private const string ElementIconPrefix = "UI_Buff_Element_";
    public const int SmallSize = 64;
    private readonly Dictionary<string, Sprite> sprites = [];
    private readonly Dictionary<string, RenderTexture> smalls = [];
    private readonly Dictionary<int, Sprite> elements = [];

    // The game's values for each element, and the end of its icon's name.
    private static readonly Dictionary<int, string> ElementNames = new()
    {
        [1] = "Fire", [2] = "Water", [3] = "Grass", [4] = "Elect", [5] = "Frost", [7] = "Wind", [8] = "Roach",
    };

    public Sprite Get(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (sprites.TryGetValue(name, out Sprite sprite))
            return sprite;
        sprite = steps.Try("load an item icon", () => Load(Folder + name), null);
        sprites[name] = sprite;
        return sprite;
    }

    // The icons have no mipmaps, so drawing one far below its size drops pixels. This halves it in steps, once.
    public Texture Small(string name, int target = SmallSize)
    {
        string key = $"{name}@{target}";
        // Kept across openings of the panel; one the GPU dropped is made again.
        if (smalls.TryGetValue(key, out RenderTexture small) && (small == null || small.IsCreated()))
            return small;
        Sprite sprite = Get(name);
        if (sprite == null)
            return null;
        Texture source = sprite.texture;
        int size = source.width;
        var steps_ = new List<RenderTexture>();
        while (size > target)
        {
            size /= 2;
            var step = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear };
            Graphics.Blit(source, step);
            steps_.Add(step);
            source = step;
        }
        small = steps_.Count > 0 ? steps_[^1] : null;
        foreach (RenderTexture step in steps_.Where(step => step != small))
            step.Release();
        smalls[key] = small;
        return small;
    }

    public bool HasSmall(string name, int target) => smalls.ContainsKey($"{name}@{target}");

    // The element icons have no load path the mod knows, but the game's world HUD keeps them loaded, so they are found by name.
    public Sprite Element(int element)
    {
        if (elements.TryGetValue(element, out Sprite sprite))
            return sprite;
        steps.Run("find the element icons", () =>
        {
            foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Sprite>()))
                foreach ((int value, string suffix) in ElementNames)
                    if (found.name == ElementIconPrefix + suffix)
                        elements[value] = found.Cast<Sprite>();
        });
        foreach (int value in ElementNames.Keys)
            elements.TryAdd(value, null);
        elements.TryAdd(element, null);
        return elements[element];
    }

    // The element icons are the game's own sprites, which a scene change can unload, so they are found again each time.
    public void ForgetElements() => elements.Clear();

    // The wrapper declares the handle by value, but the game method takes `ref uint`.
    private static Sprite Load(string path)
    {
        IntPtr method = (IntPtr)(typeof(CommonMiscs).GetField(LoadSpriteField, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? throw new MissingFieldException(nameof(CommonMiscs), LoadSpriteField));
        uint handle = 0;
        IntPtr* args = stackalloc IntPtr[2];
        args[0] = IL2CPP.ManagedStringToIl2Cpp(path);
        args[1] = (IntPtr)(&handle);
        IntPtr exception = IntPtr.Zero;
        IntPtr result = IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, (void**)args, ref exception);
        if (exception != IntPtr.Zero)
            throw new InvalidOperationException($"LoadExternalSprite raised {IL2CPP.il2cpp_class_get_name_(IL2CPP.il2cpp_object_get_class(exception))}");
        return result == IntPtr.Zero ? null : new Sprite(result);
    }
}
