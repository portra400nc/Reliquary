using UnityEngine;
using UnityEngine.Rendering;

namespace Reliquary;

// A blurred copy of the game's frame for behind the panel. A command buffer at the end of the main camera's frame copies
// the finished picture at half size and halves it a few more times with bilinear blits, which averages it, then grows it
// back up a step at a time, so the panel shows a soft picture of the live scene. The game draws its own UI and the
// panel after the camera, so neither is in the copy. The game's own blur (MonoUIBlurCapture) only works through a
// callback its UI registers with a capture manager, which a mod cannot hand it.
internal sealed class Backdrop(Steps steps, Action<string> log)
{
    private const string MainCameraPath = "/EntityRoot/MainCamera(Clone)";
    private const CameraEvent When = CameraEvent.AfterEverything;
    private const int Halvings = 4, Grown = 2;
    private const float LookInterval = 1f;

    private Camera camera;
    private CommandBuffer buffer;
    private RenderTexture[] down = [];
    private RenderTexture[] up = [];
    private RenderTexture encoded, shown;
    private int width, height, attachedFrame;
    private float nextLook;
    private bool reported;

    // The blurred picture, once the camera has drawn a frame into it.
    public Texture Picture => buffer != null && shown && Time.frameCount > attachedFrame + 1 ? shown : null;

    public void Update(float now)
    {
        if (camera && buffer != null && Screen.width == width && Screen.height == height)
            return;
        if (buffer != null)
            Release();
        if (now < nextLook)
            return;
        nextLook = now + LookInterval;
        steps.Run("blur the game behind the panel", Attach);
    }

    public bool Warmed { get; private set; }

    // Runs the frame copy's calls once, attaching and removing the command buffer, as soon as the world has a camera.
    // Their first calls cost about 1.5 s on the Deck, which then falls in the loading screen instead of on the first
    // opening of the panel.
    public void Warm(float now)
    {
        if (Warmed || buffer != null || now < nextLook)
            return;
        nextLook = now + LookInterval;
        if (!GameObject.Find(MainCameraPath))
            return;
        steps.Run("warm up the backdrop", Attach);
        Release();
        Warmed = true;
    }

    public void Release()
    {
        if (buffer != null)
        {
            if (camera)
                steps.Run("remove the backdrop's command buffer", () => camera.RemoveCommandBuffer(When, buffer));
            buffer.Release();
        }
        buffer = null;
        camera = null;
        foreach (RenderTexture texture in down.Concat(up).Append(encoded).Append(shown))
            if (texture)
                texture.Release();
        encoded = shown = null;
        down = [];
        up = [];
    }

    private void Attach()
    {
        GameObject found = GameObject.Find(MainCameraPath);
        camera = found ? found.GetComponent<Camera>() : null;
        if (!camera)
        {
            if (!reported)
                log("backdrop: no main camera yet");
            reported = true;
            return;
        }
        width = Screen.width;
        height = Screen.height;
        int w = width, h = height;
        down = new RenderTexture[Halvings];
        for (int i = 0; i < Halvings; i++)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            down[i] = Make(w, h);
        }
        up = new RenderTexture[Grown - 1];
        for (int i = 0; i < up.Length; i++)
        {
            w *= 2;
            h *= 2;
            up[i] = Make(w, h);
        }
        w *= 2;
        h *= 2;
        // The frame is linear light, but the UI draws texture values to the screen as they are. Writing the last step into
        // an sRGB texture gamma-encodes it, and a raw copy into a linear texture of the same format keeps those values.
        encoded = Make(w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        shown = Make(w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        // Six blits in all: on the Deck an odd count turned the picture upside down.
        buffer = new CommandBuffer { name = "Reliquary backdrop" };
        buffer.Blit(BuiltinRenderTextureType.CameraTarget, down[0]);
        for (int i = 1; i < down.Length; i++)
            buffer.Blit(down[i - 1], down[i]);
        Texture from = down[^1];
        foreach (RenderTexture step in up)
        {
            buffer.Blit(from, step);
            from = step;
        }
        buffer.Blit(from, encoded);
        buffer.CopyTexture(encoded, 0, 0, shown, 0, 0);
        camera.AddCommandBuffer(When, buffer);
        attachedFrame = Time.frameCount;
        if (Warmed)
            log($"backdrop: blurring the main camera's frame, {width}x{height}");
    }

    // The blur steps have no alpha channel, so the encoded picture comes out opaque.
    private static RenderTexture Make(int width, int height)
        => Make(width, height, SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

    private static RenderTexture Make(int width, int height, RenderTextureFormat format, RenderTextureReadWrite readWrite)
    {
        var texture = new RenderTexture(width, height, 0, format, readWrite)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        texture.Create();
        return texture;
    }
}
