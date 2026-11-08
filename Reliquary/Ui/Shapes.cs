using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Reliquary.Ui;

// A picture the mod draws itself: Width x Height canvas units, coloured by Pixel at a point in units from the top left.
// A shape with a Border is 9-sliced, so it stretches to any size larger than twice its border.
internal sealed record Shape(string Name, float Width, float Height, float Border, Func<float, float, Color> Pixel);

// The panel's shapes, CameraTools' own, drawn once from signed distance fields at Density texture pixels per canvas unit
// with mipmaps. The textures are DontSave, so the game's unloading of unused assets leaves them alone.
internal static class Shapes
{
    public const int Density = 2;
    private const int ArrayData = 0x20;

    // The sliced shapes are two texture pixels wider and taller than their borders, which is where they stretch.
    private const float Stretch = 2f / Density;
    private const float DialogRadius = 6f, RingRadius = Style.ChipHeight / 2f + 3f;

    public static readonly Shape Face = Glyph("Face", 24f, 24f, Circle(12f, 12f, 12f));
    public static readonly Shape Dpad = Glyph("Dpad", 24f, 24f, Circle(12f, 12f, 12f),
        (Union(Box(10f, 5f, 4f, 14f, 1f), Box(5f, 10f, 14f, 4f, 1f)), Style.DpadRest),
        (Union(Box(10f, 5f, 4f, 5f, 1f), Box(10f, 14f, 4f, 5f, 1f)), Style.White));
    public static readonly Shape Key = Solid("Key", 10f + Stretch, 10f + Stretch, 5f, Box(0f, 0f, 10f + Stretch, 10f + Stretch, 5f), Style.Light);
    public static readonly Shape Toast = Framed("Toast", Style.ToastHeight + Stretch, Style.ToastHeight + Stretch,
        Style.ToastHeight / 2f, Style.ToastHeight / 2f, 1f, Style.ToastFill, Style.ToastRing);
    public static readonly Shape Bar = Solid("Bar", 4f + Stretch, 4f + Stretch, 2f, Box(0f, 0f, 4f + Stretch, 4f + Stretch, 2f), Style.White);
    public static readonly Shape SwitchOff = Framed("SwitchOff", Style.SwitchWidth, Style.SwitchHeight, Style.SwitchHeight / 2f, 0f, 1.5f,
        Style.Track, Style.SwitchRingOff);
    public static readonly Shape SwitchOn = Framed("SwitchOn", Style.SwitchWidth, Style.SwitchHeight, Style.SwitchHeight / 2f, 0f, 1.5f,
        Style.Cream, Style.SwitchRingOn);
    // CameraTools' controller glyphs.
    public static readonly Shape Bumper = Solid("Bumper", 30f, 18f, 0f, Box(0f, 0f, 30f, 18f, 6f, 6f, 4f, 4f), Style.Light);
    public static readonly Shape Trigger = Solid("Trigger", 26f, 22f, 0f, Box(0f, 0f, 26f, 22f, 9f, 9f, 4f, 4f), Style.Light);
    public static readonly Shape Menu = Glyph("Menu", 26f, 20f, Box(0f, 0f, 26f, 20f, 6f),
        (Union(Box(7.5f, 6f, 11f, 2f, 0f), Box(7.5f, 9f, 11f, 2f, 0f), Box(7.5f, 12f, 11f, 2f, 0f)), Style.White));
    // Two overlapping squares, the back one showing only outside the front one.
    public static readonly Shape View = Glyph("View", 26f, 20f, Box(0f, 0f, 26f, 20f, 6f),
        (Subtract(Ring(Box(11f, 4.5f, 7f, 7f, 1f), 1.5f), Box(7.5f, 8f, 7f, 7f, 1f)), Style.White),
        (Ring(Box(7.5f, 8f, 7f, 7f, 1f), 1.5f), Style.White));
    public static readonly Shape DpadUpDown = Glyph("DpadUpDown", 24f, 24f, Circle(12f, 12f, 12f),
        (Union(Box(10f, 5f, 4f, 14f, 1f), Box(5f, 10f, 14f, 4f, 1f)), Style.DpadRest),
        (Union(Box(10f, 5f, 4f, 5f, 1f), Box(10f, 14f, 4f, 5f, 1f)), Style.White));
    public static readonly Shape Sigil = Solid("Sigil", 64f, 64f, 0f, SwordOverGoblet(64f), Style.White);
    public static readonly Shape Funnel = Solid("Funnel", 16f, 16f, 0f, Union(Convex((1f, 2f), (15f, 2f), (9.6f, 8.6f), (6.4f, 8.6f)), Convex((6.4f, 8f), (9.6f, 8f), (9.6f, 14f), (6.4f, 12.4f))), Style.White);
    public static readonly Shape SortOrder = Solid("SortOrder", 16f, 16f, 0f, Union(Convex((4f, 1f), (7f, 5f), (1f, 5f)), Box(3f, 5f, 2f, 10f, 0f), Convex((12f, 15f), (9f, 11f), (15f, 11f)), Box(11f, 1f, 2f, 10f, 0f)), Style.White);
    public static readonly Shape LockBody = Solid("Lock", 12f, 14f, 0f, Union(Box(1f, 6f, 10f, 8f, 1.5f, 1.5f, 1.5f, 1.5f), Line(3.5f, 6f, 3.5f, 3.5f, 1.6f), Line(8.5f, 6f, 8.5f, 3.5f, 1.6f), Line(3.5f, 3f, 8.5f, 3f, 1.6f)), Style.White);
    public static readonly Shape Knob = Solid("Knob", Style.KnobSize, Style.KnobSize, 0f, Circle(10f, 10f, 10f), Style.Light);
    public static readonly Shape Arrow = Solid("Arrow", 7f, 10f, 0f, Convex((0f, 0f), (7f, 5f), (0f, 10f)), Style.White);
    public static readonly Shape Close = new("Close", Style.BackSize, Style.BackSize, 0f, Layers((Circle(17f, 17f, 17f), Style.Light),
        (Union(Line(13f, 13f, 21f, 21f, 1.15f), Line(21f, 13f, 13f, 21f, 1.15f)), Style.BackInk)));
    public static readonly Shape PanelFill = new("PanelFill", 1f, 64f, 0f, (_, y) => Style.Lerp(Style.PanelTop, Style.PanelBottom, y / 64f));
    // box-shadow 8px 0 30px: the panel's edge offset by 8 and blurred with a standard deviation of 15.
    public static readonly Shape PanelShadow = new("PanelShadow", Style.PanelShadowWidth, 1f, 0f,
        (x, _) => Style.WithAlpha(Style.PanelShadow, Style.PanelShadow.a * (1f - Normal((x - 8f) / 15f))));

    public static readonly Shape Pill = Solid("Pill", Style.ChipHeight + Stretch, Style.ChipHeight + Stretch, Style.ChipHeight / 2f,
        Box(0f, 0f, Style.ChipHeight + Stretch, Style.ChipHeight + Stretch, Style.ChipHeight / 2f), Style.White);
    // A prompt's panel and its messages: dark, with a faint cream edge.
    public static readonly Shape Dialog = Framed("Dialog", 2f * DialogRadius + Stretch, 2f * DialogRadius + Stretch, DialogRadius, DialogRadius, 1f,
        Style.Hex(0x181f2e, 0.97f), Style.Hex(0xd3bc8e, 0.26f));
    // The focus ring around a button, 3 units outside its pill.
    public static readonly Shape PillRing = Solid("PillRing", 2f * RingRadius + Stretch, 2f * RingRadius + Stretch, RingRadius,
        Ring(Box(0f, 0f, 2f * RingRadius + Stretch, 2f * RingRadius + Stretch, RingRadius), 2f), Style.White);
    public static readonly Shape Spinner = Solid("Spinner", 22f, 22f, 0f, Subtract(Ring(Circle(11f, 11f, 10f), 2f), Convex((11f, 11f), (22f, 0f), (22f, 11f))), Style.White);
    public static readonly Shape Check = Solid("Check", 16f, 16f, 0f, Union(Line(3f, 8.5f, 6.5f, 12f, 1.1f), Line(6.5f, 12f, 13f, 4.5f, 1.1f)), Style.White);
    public static readonly Shape Grid = Solid("Grid", 20f, 20f, 0f, Union(Box(1f, 1f, 8f, 8f, 2f), Box(11f, 1f, 8f, 8f, 2f), Box(1f, 11f, 8f, 8f, 2f), Box(11f, 11f, 8f, 8f, 2f)), Style.White);
    public static readonly Shape Badge = Solid("Badge", 12f + Stretch, 12f + Stretch, 6f, Box(0f, 0f, 12f + Stretch, 12f + Stretch, 6f, 6f, 4f, 4f), Style.Light);

    public static readonly Shape Card = Solid("Card", 2f * Style.CardRadius + Stretch, 2f * Style.CardRadius + Stretch, Style.CardRadius,
        Box(0f, 0f, 2f * Style.CardRadius + Stretch, 2f * Style.CardRadius + Stretch, Style.CardRadius), Style.Strip);
    // box-shadow 0 2px 6px under a card; placed CardShadowMargin outside the card on every side.
    public const float CardShadowMargin = 3f * Style.CardShadowSigma;
    public static readonly Shape CardShadow = Blurred("CardShadow", CardShadowMargin, Style.CardRadius, (Style.CardShadowSigma, Style.CardShadow));
    // The selected card's box-shadow 0 0 0 3px white, 0 0 14px 3px: a white ring with a glow, drawn behind the card.
    public const float GlowMargin = Style.GlowRing + 3f * Style.GlowSigma;
    public static readonly Shape Glow = Blurred("Glow", 3f * Style.GlowSigma, Style.CardRadius + Style.GlowRing,
        (Style.GlowSigma, Style.Glow), (0f, Style.White));

    public static readonly Shape[] Art = [.. Enumerable.Range(0, 6).Select(rarity => ArtOf(rarity, false))];
    public static readonly Shape[] BlockedArt = [.. Enumerable.Range(0, 6).Select(rarity => ArtOf(rarity, true))];

    // The character list's cards, with corners as tight as the game's own list.
    private const float RosterRadius = 3f, RosterCorner = 8f;
    public static readonly Shape RosterPlate = Solid("RosterPlate", 2f * RosterRadius + Stretch, 2f * RosterRadius + Stretch, RosterRadius,
        Box(0f, 0f, 2f * RosterRadius + Stretch, 2f * RosterRadius + Stretch, RosterRadius), Style.White);
    public static readonly Shape[] RosterArt = [.. Enumerable.Range(0, 6).Select(rarity => ArtOf(rarity, false, RosterRadius, RosterCorner, "Roster"))];

    // The badges carry a shadow, so they are drawn BadgeMargin larger than the badge on every side.
    public const float BadgeMargin = 3f;
    public static readonly Shape ShownBadge = BadgeOf("ShownBadge", Style.Cream, Style.BackInk,
        Union(Stroke(Intersect(Circle(8f, 12.875f, 8.125f), Circle(8f, 3.125f, 8.125f)), 0.8f), Circle(8f, 8f, 2.4f)));
    public static readonly Shape EquippedBadge = BadgeOf("EquippedBadge", Style.BadgeDark, Style.Text,
        Union(Convex((11.5f, 1.5f), (14.5f, 1.5f), (14.5f, 4.5f), (6.5f, 12.5f), (3.5f, 9.5f)), Line(2f, 9f, 7f, 14f, 0.8f),
            Line(3.5f, 12.5f, 1.5f, 14.5f, 0.8f)));
    public static readonly Shape BlockedLabel = Solid("BlockedLabel", 2f * Style.BlockedRadius + Stretch, 2f * Style.BlockedRadius + Stretch,
        Style.BlockedRadius, Box(0f, 0f, 2f * Style.BlockedRadius + Stretch, 2f * Style.BlockedRadius + Stretch, Style.BlockedRadius),
        Style.BlockedFill);

    private static readonly Dictionary<Shape, Sprite> sprites = [];

    // The focus frame, design A of .work/reliquary-ui/focus: a 2-unit white ring on the marked part's own rounding, and
    // outside it a warm glow like CSS's box-shadow 0 0 14px 3px, whose strength the panel breathes.
    public const float FocusGlowMargin = 14f;
    private const float GlowSigma = 5f;
    private static readonly Dictionary<float, Shape> focusRings = [], focusGlows = [];

    public static Shape FocusRing(float radius)
    {
        if (focusRings.TryGetValue(radius, out Shape ring))
            return ring;
        float size = 2f * radius + Stretch;
        return focusRings[radius] = Solid($"FocusRing{radius}", size, size, radius, Ring(Box(0f, 0f, size, size, radius), 2f), Style.White);
    }

    public static Shape FocusGlow(float radius)
    {
        if (focusGlows.TryGetValue(radius, out Shape glow))
            return glow;
        float size = 2f * (FocusGlowMargin + radius) + Stretch;
        Field outline = Box(FocusGlowMargin, FocusGlowMargin, size - 2f * FocusGlowMargin, size - 2f * FocusGlowMargin, radius);
        Color warm = Style.Hex(0xf0d6a0, 0.75f);
        return focusGlows[radius] = new Shape($"FocusGlow{radius}", size, size, FocusGlowMargin + radius, (x, y) =>
        {
            float distance = outline(x, y);
            float strength = distance <= 0f ? 0f : MathF.Exp(-distance * distance / (2f * GlowSigma * GlowSigma));
            return Style.WithAlpha(warm, warm.a * strength);
        });
    }

    public static Sprite Get(Shape shape)
    {
        if (sprites.TryGetValue(shape, out Sprite sprite) && sprite)
            return sprite;
        sprite = Create(shape);
        sprites[shape] = sprite;
        return sprite;
    }

    private static Sprite Create(Shape shape)
    {
        int width = (int)MathF.Round(shape.Width * Density), height = (int)MathF.Round(shape.Height * Density);
        var bytes = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            // Texture2D stores the bottom row first.
            float y = shape.Height - (row + 0.5f) / Density;
            for (int column = 0; column < width; column++)
            {
                Color color = shape.Pixel((column + 0.5f) / Density, y);
                int at = (row * width + column) * 4;
                bytes[at] = Byte(color.r);
                bytes[at + 1] = Byte(color.g);
                bytes[at + 2] = Byte(color.b);
                bytes[at + 3] = Byte(color.a);
            }
        }
        var pixels = new Il2CppStructArray<Color32>(width * height);
        // Color32 is four RGBA bytes, the raster's own layout, so the pixels go into the array's data in one copy.
        Marshal.Copy(bytes, 0, pixels.Pointer + ArrayData, bytes.Length);
        // Linear, so the card colours reach the screen as written: the game draws UI without converting sRGB textures back,
        // and an sRGB texture came out darker on the Deck (#e9e5dc drew as #d0c8b6).
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
        {
            name = "Reliquary " + shape.Name,
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(pixels);
        texture.Apply();
        var rect = new Rect { m_XMin = 0f, m_YMin = 0f, m_Width = width, m_Height = height };
        var pivot = new Vector2 { x = 0.5f, y = 0.5f };
        // Relative to the canvas scaler's 100 reference pixels per unit, so a border in texture pixels is Density times its
        // size in canvas units.
        float border = shape.Border * Density;
        var sprite = Sprite.Create(texture, rect, pivot, 100f * Density, 0, SpriteMeshType.FullRect,
            new Vector4 { x = border, y = border, z = border, w = border });
        sprite.name = texture.name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static byte Byte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

    // A signed distance in units at a point measured from the top left: negative inside, positive outside.
    private delegate float Field(float x, float y);

    private static Field Subtract(Field shape, Field cut) => (x, y) => MathF.Max(shape(x, y), -cut(x, y));


    // The mod's icon, drawn in a 200-unit box: a sword in front of a goblet with an inset outline, with a gap cut around the sword.
    private static Field SwordOverGoblet(float size)
    {
        const float Tilt = 34f * MathF.PI / 180f, GobletScale = 0.82f;
        float cos = MathF.Cos(Tilt), sin = MathF.Sin(Tilt);
        Field upright = Union(Circle(0f, 0f, 11f), Line(0f, 0f, 0f, 36f, 6.5f),
            Convex((-46f, 37f), (-12f, 29f), (12f, 29f), (46f, 37f), (12f, 49f), (-12f, 49f)),
            Convex((-10.5f, 44f), (10.5f, 44f), (9.5f, 188f), (0f, 214f), (-9.5f, 188f)));
        Field sword = (x, y) => upright(cos * (x - 156f) + sin * (y - 15f), cos * (y - 15f) - sin * (x - 156f));
        Field bowl = Intersect(Circle(100f, 36f, 74f), Convex((0f, 14f), (200f, 14f), (200f, 200f), (0f, 200f)));
        Field goblet = Union(Ring(bowl, 10f), (x, y) => bowl(x, y) + 18f,
            Convex((90f, 100f), (110f, 100f), (106f, 165f), (94f, 165f)),
            Convex((94f, 158f), (106f, 158f), (140f, 176f), (146f, 186f), (54f, 186f), (60f, 176f)));
        Field placed = (x, y) => GobletScale * goblet(100f + (x - 110f) / GobletScale, 104f + (y - 108f) / GobletScale);
        Field icon = Union(Subtract(placed, (x, y) => sword(x, y) - 7f), sword);
        return (x, y) => icon(x * 200f / size, y * 200f / size) * size / 200f;
    }

    private static Shape Solid(string name, float width, float height, float border, Field field, Color color)
        => new(name, width, height, border, Layers((field, color)));

    // A rounded rectangle filling the whole shape, with a ring inside its edge.
    private static Shape Framed(string name, float width, float height, float radius, float border, float ring, Color fill, Color edge)
    {
        var outline = Box(0f, 0f, width, height, radius);
        return new(name, width, height, border, Layers((outline, fill), (Ring(outline, ring), edge)));
    }

    // A controller glyph: a dark plate with a faint ring inside its edge, and white marks on it.
    private static Shape Glyph(string name, float width, float height, Field plate, params (Field, Color)[] marks)
        => new(name, width, height, 0f, Layers([(plate, Style.GlyphFill), (Ring(plate, 2f), Style.GlyphRing), .. marks]));

    // Paints each field's colour over the ones before it, anti-aliased across one texture pixel. Transparent pixels keep
    // the first colour, so filtering does not pull dark fringes in from outside the shape.
    private static Func<float, float, Color> Layers(params (Field Field, Color Color)[] layers) => (x, y) =>
    {
        float r = 0f, g = 0f, b = 0f, a = 0f;
        foreach (var (field, color) in layers)
        {
            float alpha = color.a * Coverage(field(x, y));
            r = color.r * alpha + r * (1f - alpha);
            g = color.g * alpha + g * (1f - alpha);
            b = color.b * alpha + b * (1f - alpha);
            a = alpha + a * (1f - alpha);
        }
        return a > 0f ? new Color { r = r / a, g = g / a, b = b / a, a = a } : Style.WithAlpha(layers[0].Color, 0f);
    };

    // How much of a texture pixel at this signed distance the shape covers, anti-aliased across one texture pixel.
    private static float Coverage(float distance) => Math.Clamp(0.5f - distance * Density, 0f, 1f);

    // A rounded rectangle 9-sliced at margin + radius, with layers of the same outline blurred by each sigma (0 is sharp)
    // and inset by margin, so it is placed margin outside the box it shadows.
    private static Shape Blurred(string name, float margin, float radius, params (float Sigma, Color Color)[] layers)
    {
        float size = 2f * (margin + radius) + Stretch;
        var outline = Box(margin, margin, size - 2f * margin, size - 2f * margin, radius);
        return new(name, size, size, margin + radius,
            Layers([.. layers.Select(layer => (layer.Sigma > 0f ? Soft(outline, layer.Sigma) : outline, layer.Color))]));
    }

    // A card's art: a 160° gradient in its rarity's colours, rounded at the card's top corners and more at its bottom
    // right. A blocked weapon's is greyed and darkened.
    private static Shape ArtOf(int rarity, bool blocked, float radius = Style.CardRadius, float corner = Style.ArtCorner, string kind = "")
    {
        float size = Style.ArtSize, angle = Style.ArtAngle * MathF.PI / 180f;
        // CSS angles run clockwise from up; y runs down here.
        float dx = MathF.Sin(angle), dy = -MathF.Cos(angle);
        float length = MathF.Abs(size * dx) + MathF.Abs(size * dy);
        var outline = Box(0f, 0f, size, size, radius, radius, corner, 0f);
        Color from = Style.ArtFrom[rarity], to = Style.ArtTo[rarity];
        return new($"{kind}Art{rarity}{(blocked ? "Blocked" : "")}", size, size, 0f, (x, y) =>
        {
            float t = Math.Clamp(((x - size / 2f) * dx + (y - size / 2f) * dy) / length + 0.5f, 0f, 1f);
            Color color = Style.Lerp(from, to, t);
            if (blocked)
            {
                float grey = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
                color = Style.Lerp(color, new Color { r = grey, g = grey, b = grey, a = 1f }, Style.BlockedGrey);
                color = new Color { r = color.r * Style.BlockedBrightness, g = color.g * Style.BlockedBrightness, b = color.b * Style.BlockedBrightness, a = 1f };
            }
            return Style.WithAlpha(color, Coverage(outline(x, y)));
        });
    }

    // A badge with its 16-unit glyph centred, and box-shadow 0 1px 2px under it.
    private static Shape BadgeOf(string name, Color fill, Color ink, Field glyph)
    {
        float size = Style.BadgeSize + 2f * BadgeMargin, inset = BadgeMargin + (Style.BadgeSize - Style.BadgeGlyph) / 2f;
        var plate = Box(BadgeMargin, BadgeMargin, Style.BadgeSize, Style.BadgeSize, Style.BadgeRadius);
        return new(name, size, size, 0f, Layers((Soft(Box(BadgeMargin, BadgeMargin + 1f, Style.BadgeSize, Style.BadgeSize, Style.BadgeRadius), 1f),
            Style.BadgeShadow), (plate, fill), ((x, y) => glyph(x - inset, y - inset), ink)));
    }

    private static Field Circle(float cx, float cy, float radius)
        => (x, y) => MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius;

    private static Field Box(float left, float top, float width, float height, float radius)
        => Box(left, top, width, height, radius, radius, radius, radius);

    private static Field Box(float left, float top, float width, float height, float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        float cx = left + width / 2f, cy = top + height / 2f, hw = width / 2f, hh = height / 2f;
        return (x, y) =>
        {
            float radius = x < cx ? y < cy ? topLeft : bottomLeft : y < cy ? topRight : bottomRight;
            float qx = MathF.Abs(x - cx) - hw + radius, qy = MathF.Abs(y - cy) - hh + radius;
            float outside = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qy, 0f) * MathF.Max(qy, 0f));
            return outside + MathF.Min(MathF.Max(qx, qy), 0f) - radius;
        };
    }

    // A segment from (x1, y1) to (x2, y2) with round ends.
    private static Field Line(float x1, float y1, float x2, float y2, float halfWidth)
        => (x, y) =>
        {
            float dx = x2 - x1, dy = y2 - y1;
            float t = Math.Clamp(((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy), 0f, 1f);
            float ex = x - x1 - dx * t, ey = y - y1 - dy * t;
            return MathF.Sqrt(ex * ex + ey * ey) - halfWidth;
        };

    // A convex polygon. Inside, the distance is to the nearest edge's line; outside, to the nearest side.
    private static Field Convex(params (float X, float Y)[] points)
    {
        float cx = points.Average(point => point.X), cy = points.Average(point => point.Y);
        Field[] edges = [.. points.Select((point, i) => Edge(point.X, point.Y, points[(i + 1) % points.Length].X, points[(i + 1) % points.Length].Y, cx, cy))];
        Field[] sides = [.. points.Select((point, i) => Line(point.X, point.Y, points[(i + 1) % points.Length].X, points[(i + 1) % points.Length].Y, 0f))];
        return (x, y) =>
        {
            float inside = edges.Max(edge => edge(x, y));
            return inside <= 0f ? inside : sides.Min(side => side(x, y));
        };
    }

    // The distance from the line through a and b, negative on the side of c.
    private static Field Edge(float ax, float ay, float bx, float by, float cx, float cy)
    {
        float nx = by - ay, ny = ax - bx, length = MathF.Sqrt(nx * nx + ny * ny);
        float sign = (cx - ax) * nx + (cy - ay) * ny > 0f ? -1f : 1f;
        return (x, y) => sign * ((x - ax) * nx + (y - ay) * ny) / length;
    }

    private static Field Union(params Field[] fields) => (x, y) => fields.Min(field => field(x, y));

    private static Field Intersect(Field a, Field b) => (x, y) => MathF.Max(a(x, y), b(x, y));

    // The outline itself, halfWidth either side, as an SVG stroke draws it.
    private static Field Stroke(Field shape, float halfWidth) => (x, y) => MathF.Abs(shape(x, y)) - halfWidth;

    // Layers paints this field as the shape blurred by a Gaussian of the given standard deviation: its coverage is
    // the normal distribution of the distance, which is exact along a straight edge.
    private static Field Soft(Field shape, float sigma) => (x, y) => (0.5f - Normal(-shape(x, y) / sigma)) / Density;

    // The band of the given width just inside the shape's edge, as CSS's inset box-shadow draws it.
    private static Field Ring(Field shape, float width) => (x, y) =>
    {
        float distance = shape(x, y);
        return MathF.Max(distance, -distance - width);
    };

    // The standard normal cumulative distribution, from Abramowitz and Stegun's 7.1.26 approximation of erf.
    private static float Normal(float z)
    {
        float x = MathF.Abs(z) / MathF.Sqrt(2f);
        float t = 1f / (1f + 0.3275911f * x);
        float erf = 1f - ((((1.061405429f * t - 1.453152027f) * t + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t
            * MathF.Exp(-x * x);
        return z >= 0f ? (1f + erf) / 2f : (1f - erf) / 2f;
    }
}
