using UnityEngine;

namespace Reliquary.Ui;

// The approved mockup's look, in one table so the design can change in one place. Colours are CameraTools'. Sizes are
// canvas units, which equal mockup pixels on the 1280x800 reference canvas; positions are measured from the top left of
// their parent, as in the mockup's CSS.
internal static class Style
{
    public static readonly Color Text = Hex(0xece5d8);
    public static readonly Color Dim = Hex(0xece5d8, 0.62f);
    public static readonly Color Cream = Hex(0xd3bc8e);
    public static readonly Color Track = Hex(0x343e50);
    public static readonly Color Band = Hex(0xece5d8, 0.10f);
    public static readonly Color PanelTop = Hex(0x222835, 0.92f);
    public static readonly Color PanelBottom = Hex(0x2c3342, 0.92f);
    public static readonly Color PanelEdge = Hex(0xd3bc8e, 0.18f);
    public static readonly Color Divider = Hex(0xd3bc8e, 0.12f);
    public static readonly Color PanelShadow = Hex(0x000000, 0.25f);
    public static readonly Color TabBar = Hex(0x12151c, 0.55f);
    public static readonly Color ToastFill = Hex(0x181c25, 0.78f);
    public static readonly Color ToastRing = Hex(0xd3bc8e, 0.35f);
    public static readonly Color TextShadow = Hex(0x000000, 0.35f);
    public static readonly Color GlyphFill = Hex(0x1c2029);
    public static readonly Color GlyphRing = Hex(0xffffff, 0.18f);
    public static readonly Color Light = Hex(0xeae6de);
    public static readonly Color BackInk = Hex(0x2a2f3a);
    public static readonly Color KeyFill = Hex(0x1c202a, 0.55f);
    public static readonly Color BumperInk = Hex(0x22262e);
    public static readonly Color XboxA = Hex(0x6cbf3f);
    public static readonly Color XboxB = Hex(0xe0463c);
    public static readonly Color XboxX = Hex(0x3c8ee0);
    public static readonly Color XboxY = Hex(0xe7b52e);
    public static readonly Color White = Hex(0xffffff);
    public static readonly Color DpadRest = Hex(0xffffff, 0.35f);
    public static readonly Color SwitchRingOff = Hex(0xece5d8, 0.35f);
    public static readonly Color SwitchRingOn = Hex(0xffffff, 0.40f);
    public static readonly Color Thumb = Hex(0xd3bc8e, 0.55f);
    public static readonly Color Clear = Hex(0xffffff, 0f);

    public static readonly Color ChipOff = Hex(0xece5d8, 0.10f);

    // A card's art gradient by rarity, 1 to 5, from its top left to its bottom right (--r1a to --r5b).
    public static readonly Color[] ArtFrom = [Hex(0x6b6b70), Hex(0x6b6b70), Hex(0x3f6b5c), Hex(0x46628a), Hex(0x5b4b8c), Hex(0xa35d2c)];
    public static readonly Color[] ArtTo = [Hex(0x8e8e94), Hex(0x8e8e94), Hex(0x5c977c), Hex(0x5d8bb8), Hex(0x8c6fbd), Hex(0xc88a42)];
    public static readonly Color Star = Hex(0xffcc32);
    public static readonly Color StarOutline = Hex(0x6b4a10);
    public static readonly Color Strip = Hex(0xe9e5dc);
    public static readonly Color StripInk = Hex(0x3b3f4a);
    public static readonly Color StripInkBlocked = Hex(0x3b3f4a, 0.6f);
    public static readonly Color BadgeDark = Hex(0x3c3a54);
    public static readonly Color BadgeShadow = Hex(0x000000, 0.35f);
    public static readonly Color CardShadow = Hex(0x000000, 0.28f);
    public static readonly Color Glow = Hex(0xffffff, 0.55f);
    public static readonly Color IconShadow = Hex(0x000000, 0.35f);
    public static readonly Color BlockedFill = Hex(0x14161e, 0.7f);
    // CSS's filter: grayscale(.85) brightness(.75) on a blocked card's art. The icon can only be tinted darker.
    public const float BlockedGrey = 0.85f;
    public const float BlockedBrightness = 0.75f;
    public static readonly Color BlockedIcon = Hex(0xbfbfbf);

    public const float ReferenceWidth = 1280f;
    public const float ReferenceHeight = 800f;
    // Under CameraTools' canvas (30000), over CooldownTracker's and Cartographer's (29000).
    public const int SortingOrder = 29500;
    public const string GameFont = "Default_SC";

    public const float PanelWidth = 560f;
    public const float PanelSlide = 16f;
    public const float PanelShadowWidth = 56f;

    public const float TabBarHeight = 64f;
    public const float TabBarPadding = 16f;
    public const float BackSize = 34f;
    public const float TabX = TabBarPadding + BackSize + 4f;
    public const int TabPadX = 10;
    public const int TabSize = 17;
    public const float TabTextHeight = 22f;
    public const float LineInset = 10f;
    public const float LineBottom = 2f;
    public const float LineHeight = 3f;

    public const float WhoX = 24f;
    public const float WhoTop = 16f;
    public const int NameSize = 20;
    public const float NameHeight = 26f;
    public const int SubSize = 12;
    public const float SubHeight = 17f;
    public const float WhoGap = 4f;
    public const float WhoBottom = 12f;
    public const float WhoHeight = WhoTop + NameHeight + WhoGap + SubHeight + WhoGap + SubHeight + WhoBottom;

    public const float LookTop = TabBarHeight + WhoHeight;
    public const float RowLeft = 28f;
    public const float RowRight = 24f;
    public const float RowWidth = PanelWidth - RowLeft - RowRight;
    public const int RowSize = 14;
    public const float RowTextY = 9f;
    public const float RowTextHeight = 20f;
    public const float ArrowX = 10f;
    public const float ArrowY = 22f;
    public const float NoteY = 30f;
    public const float NoteHeight = 16f;
    public const int NoteSize = 11;
    public const float ToggleRowHeight = RowTextY + RowTextHeight + 17f + 8f;
    public const float SwitchWidth = 52f;
    public const float SwitchHeight = 26f;
    public const float KnobInset = 3f;
    public const float KnobSize = 20f;

    public const float TabsTop = LookTop + ToggleRowHeight;
    public const float TabsX = 24f;
    public const float TabsPadTop = 8f;
    public const float TabsPadBottom = 10f;
    public const float ChipHeight = 24f;
    public const int ChipPadX = 10;
    public const float ChipGap = 6f;
    public const int ChipSize = 12;
    public const int CountSize = 12;
    public const float TabsHeight = TabsPadTop + ChipHeight + TabsPadBottom;

    public const float GridTop = TabsTop + TabsHeight;
    public const float GridPadX = 24f;
    public const float GridPadTop = 14f;
    public const float GridPadBottom = 16f;
    public const float ColumnGap = 12f;
    public const float RowGap = 14f;
    public const float CardWidth = (PanelWidth - 2f * GridPadX - (4 - 1) * ColumnGap) / 4;
    public const float ArtSize = CardWidth;
    public const float CardRadius = 8f;
    public const float ArtCorner = 22f;
    public const float ArtAngle = 160f;
    // The light plate starts under the art, low enough that its rounded top corners stay hidden, so its anti-aliased
    // edge does not show as a light line around the art's.
    public const float PlateTop = ArtSize - ArtCorner - CardRadius;
    // Room for two lines of the name: 5 above, two 15-unit lines, 6 below.
    public const float StripHeight = 41f;
    public const float StripPadTop = 5f;
    public const float StripPadBottom = 6f;
    public const float StripPadX = 6f;
    public const float CardHeight = ArtSize + StripHeight;
    public const int CardNameSize = 12;
    public const int StarsSize = 15;
    public const float StarsBottom = 2f;
    public const float StarsHeight = 18f;
    public const float BadgeInset = 6f;
    public const float BadgeSize = 26f;
    public const float BadgeRadius = 6f;
    public const float BadgeGlyph = 16f;
    // The icon's box inside the art, as fractions of its side: 4% left, right and top, 10% bottom.
    public const float IconTop = 0.04f;
    public const float IconBottom = 0.10f;
    public const float IconShadowY = 3f;
    public const float BlockedInset = 6f;
    public const float BlockedTop = 0.42f;
    public const float BlockedPadX = 2f;
    public const float BlockedPadY = 4f;
    public const float BlockedRadius = 5f;
    public const int BlockedSize = 11;
    // CSS's box-shadow blur radius is twice the Gaussian's standard deviation.
    public const float CardShadowY = 2f;
    public const float CardShadowSigma = 3f;
    public const float GlowRing = 3f;
    public const float GlowSigma = 7f;
    public const float Lift = 2f;

    public const float ThumbWidth = 4f;
    public const float ThumbRight = 4f;
    public const float ThumbMin = 24f;

    // Six hints do not fit one line in the panel, so they sit on two, as the mockup's footer wraps them.
    public const float FooterPadTop = 14f;
    public const float FooterRowGap = 8f;
    public const float FooterBottom = 22f;
    public const float FooterGap = 16f;
    public const float HintGap = 6f;
    public const float HintHeight = 24f;
    public const int HintSize = 13;
    public const float FooterHeight = FooterPadTop + 2f * HintHeight + FooterRowGap + FooterBottom;
    public const float KeyHeight = 22f;
    public const float KeyMinWidth = 24f;
    public const int KeyPadding = 7;
    public const int KeySize = 11;
    public const int FaceSize = 12;
    public const float BumperHeight = 18f;
    public const float BumperMinWidth = 28f;
    public const int BumperPadding = 4;
    public const int BumperSize = 10;

    // The toast is centred over the game, right of the panel.
    public const float ToastX = PanelWidth / 2f;
    public const float ToastY = 68f;
    public const float ToastHeight = 40f;
    public const int ToastPadding = 26;
    public const int ToastSize = 15;
    public const float ToastTime = 1.6f;

    public const float PanelFade = 0.2f;
    public const float ToastFade = 0.25f;
    public const float SwitchSlide = 0.15f;
    public const float RepeatDelay = 0.4f;
    public const float RepeatInterval = 0.08f;

    // Unity's Color constructor is an IL2CPP call; filling the fields is not.
    public static Color Hex(uint rgb, float alpha = 1f)
        => new() { r = (rgb >> 16 & 0xff) / 255f, g = (rgb >> 8 & 0xff) / 255f, b = (rgb & 0xff) / 255f, a = alpha };

    public static Color Lerp(Color a, Color b, float t)
        => new() { r = a.r + (b.r - a.r) * t, g = a.g + (b.g - a.g) * t, b = a.b + (b.b - a.b) * t, a = a.a + (b.a - a.a) * t };

    public static Color WithAlpha(Color color, float alpha) => new() { r = color.r, g = color.g, b = color.b, a = alpha };

    // For a <color> tag in rich text.
    public static string Html(Color color) => $"#{Channel(color.r):x2}{Channel(color.g):x2}{Channel(color.b):x2}";

    private static int Channel(float value) => (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

    // CSS's default transition timing, cubic-bezier(0.25, 0.1, 0.25, 1), solved for x by bisection.
    public static float Ease(float t)
    {
        float low = 0f, high = 1f;
        for (int i = 0; i < 20; i++)
        {
            float mid = (low + high) / 2f;
            if (Bezier(mid, 0.25f, 0.25f) < t)
                low = mid;
            else
                high = mid;
        }
        return Bezier((low + high) / 2f, 0.1f, 1f);
    }

    private static float Bezier(float s, float p1, float p2)
    {
        float u = 1f - s;
        return 3f * u * u * s * p1 + 3f * u * s * s * p2 + s * s * s;
    }
}
