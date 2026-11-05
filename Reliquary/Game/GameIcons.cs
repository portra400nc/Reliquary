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
    private readonly Dictionary<string, Sprite> sprites = [];

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
