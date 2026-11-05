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
    private const int SmallSize = 64;
    private readonly Dictionary<string, Sprite> sprites = [];
    private readonly Dictionary<string, RenderTexture> smalls = [];

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
    public Texture Small(string name)
    {
        if (smalls.TryGetValue(name, out RenderTexture small))
            return small;
        Sprite sprite = Get(name);
        if (sprite == null)
            return null;
        Texture source = sprite.texture;
        int size = source.width;
        var steps_ = new List<RenderTexture>();
        while (size > SmallSize)
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
        smalls[name] = small;
        return small;
    }

    public void ReleaseSmall()
    {
        foreach (RenderTexture small in smalls.Values)
            if (small != null)
                small.Release();
        smalls.Clear();
    }

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
