using UnityEngine;
using UnityEngine.UI;
using static Reliquary.Ui.Widgets;
using Object = UnityEngine.Object;

namespace Reliquary.Ui;

// Hover for keyboard and mouse: cards lift 3 units with a shadow and brighten, buttons get a cream edge with a soft glow, rows get
// a pale wash, faces and the All characters tile a cream ring that draws in, and a tab a faint preview of its underline.
// A press dips a card or button to 96%. Hover is apart from the focus frame, and off in the controller layout.
//
// The effects are a few nodes of their own under the hovered part, eased every frame here. A redraw drops them and puts
// them back at once on the part still under the pointer, so a redraw never replays the hover.
internal sealed partial class Panel
{
    private enum Hover { None, Lift, Edge, Wash, Ring, Tab }

    private sealed class HitArea
    {
        public RectTransform Rect, Window;
        public Action Click;
        public Hover Kind;
        public float X, Y, Width, Height, Radius;
        // Light parts darken under the wash; lifted groups move with a card.
        public bool Light;
        public RectTransform[] Lift = [];
        public string Key;
    }

    private sealed class HoverEffect
    {
        public HitArea Area;
        public float Shown, Press;
        public bool Leaving, Pressed;
        public readonly List<GameObject> Nodes = [];
        public CanvasGroup Fade;
        public RectTransform Grow;
        public Vector2[] Base = [];
    }

    private const float HoverInSeconds = 0.12f, HoverOutSeconds = 0.18f, PressSeconds = 0.06f, LiftUnits = 3f, PressScale = 0.96f;
    private static readonly Color Wash = Style.Hex(0xece5d8, 0.12f), LightWash = Style.Hex(0x3b3f4a, 0.10f), Brighten = Style.Hex(0xffffff, 0.08f),
        EdgeInk = Style.Hex(0xd3bc8e, 0.9f), TabPreview = Style.Hex(0xd3bc8e, 0.45f), LiftShade = Style.Hex(0x000000, 0.35f);

    private readonly List<HoverEffect> effects = [];
    private readonly Dictionary<string, float> keptHover = [];
    private Vector2 hoverPoint = new() { x = -1f, y = -1f };
    private bool hoverStale;

    private void Hit(Transform parent, float x, float y, float width, float height, Action click, Hover kind = Hover.None, float radius = 0f, bool light = false, params RectTransform[] lift)
    {
        RectTransform rect = Node("Hit", parent);
        TopLeft(rect, x, y, width, height);
        hits.Add(new HitArea
        {
            Rect = rect, Window = hitWindow, Click = click, Kind = kind, X = x, Y = y, Width = width, Height = height, Radius = radius, Light = light, Lift = lift,
            Key = $"{kind}|{parent.GetInstanceID()}|{x:F1}|{y:F1}|{width:F1}|{height:F1}",
        });
    }

    private HitArea AreaAt(Vector2 point, bool liftOnly = false)
    {
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            HitArea area = hits[i];
            if (liftOnly && area.Kind != Hover.Lift)
                continue;
            if (area.Rect && RectTransformUtility.RectangleContainsScreenPoint(area.Rect, point, null)
                && (area.Window is not { } window || window && RectTransformUtility.RectangleContainsScreenPoint(window, point, null)))
                return area;
        }
        return null;
    }

    // Before a redraw: the effects go at once, remembering how far each had come in.
    private void DropHover()
    {
        keptHover.Clear();
        foreach (HoverEffect effect in effects)
        {
            if (!effect.Leaving)
                keptHover[effect.Area.Key] = effect.Shown;
            Remove(effect);
        }
        effects.Clear();
        hoverStale = true;
    }

    // Each frame, and right after a redraw (advance false): the parts under the pointer take the hover, the rest let it
    // go, and every effect eases.
    private void UpdateHover(bool advance = true)
    {
        var point = new Vector2 { x = Input.mousePosition.x, y = Input.mousePosition.y };
        bool moved = hoverStale || point.x != hoverPoint.x || point.y != hoverPoint.y || Lists.Any(list => list.Live && list.State.Moving);
        hoverPoint = point;
        hoverStale = false;
        if (padLayout || dragging || closing)
        {
            foreach (HoverEffect effect in effects)
                effect.Leaving = true;
        }
        else if (moved)
        {
            HitArea top = AreaAt(point);
            HitArea lifted = top is { Kind: Hover.Lift } ? top : AreaAt(point, liftOnly: true);
            var wanted = new List<HitArea>();
            if (top is { Kind: not Hover.None })
                wanted.Add(top);
            if (lifted != null && lifted != top)
                wanted.Add(lifted);
            foreach (HoverEffect effect in effects)
                effect.Leaving = !wanted.Any(area => area.Key == effect.Area.Key);
            foreach (HitArea area in wanted)
                if (!effects.Any(effect => !effect.Leaving && effect.Area.Key == area.Key))
                    effects.Add(Show(area, keptHover.GetValueOrDefault(area.Key)));
        }
        keptHover.Clear();
        bool down = Input.GetMouseButton(0) && !dragging;
        float dt = advance ? Math.Max(0f, frameTime) : 0f;
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            HoverEffect effect = effects[i];
            effect.Pressed = down && !effect.Leaving && effect.Area.Kind is Hover.Lift or Hover.Edge && AreaAt(point) is { } under && (under == effect.Area || effect.Area.Kind == Hover.Lift && under.Kind != Hover.Lift);
            effect.Shown = effect.Leaving ? Math.Max(0f, effect.Shown - dt / HoverOutSeconds) : Math.Min(1f, effect.Shown + dt / HoverInSeconds);
            effect.Press = effect.Pressed ? Math.Min(1f, effect.Press + dt / PressSeconds) : Math.Max(0f, effect.Press - dt / PressSeconds);
            if (!Apply(effect))
            {
                Remove(effect);
                effects.RemoveAt(i);
            }
        }
    }

    private HoverEffect Show(HitArea area, float shown)
    {
        var effect = new HoverEffect { Area = area, Shown = shown };
        RectTransform at = area.Rect;
        if (!at)
            return effect;
        switch (area.Kind)
        {
            case Hover.Lift:
            {
                RectTransform group = area.Lift.Length > 0 ? area.Lift[0] : at.parent.TryCast<RectTransform>();
                // The shadow goes under the card's parts, the brightening over them.
                RectTransform layer = Node("HoverShadow", group);
                layer.SetAsFirstSibling();
                Fill(layer);
                float margin = Shapes.LiftShadowMargin;
                Image shade = Picture(layer, "Shade", Shapes.LiftShadow(area.Radius), LiftShade);
                TopLeft(shade.rectTransform, area.X - margin, area.Y - margin + 6f, area.Width + 2f * margin, area.Height + 2f * margin);
                RectTransform top = Node("HoverLight", group);
                Fill(top);
                Image light = Picture(top, "Light", area.Radius > 0f ? Shapes.Rounded(area.Radius) : null, Brighten);
                TopLeft(light.rectTransform, area.X, area.Y, area.Width, area.Height);
                effect.Nodes.Add(layer.gameObject);
                effect.Nodes.Add(top.gameObject);
                effect.Fade = Motion.Group(layer);
                Motion.Group(top);
                effect.Base = [.. area.Lift.Select(rect => rect ? rect.anchoredPosition : default)];
                break;
            }
            case Hover.Edge:
            {
                RectTransform layer = Node("Hover", at);
                Fill(layer);
                float r = area.Radius + 2f;
                if (!area.Light)
                    Fill(Picture(layer, "Wash", Shapes.Rounded(area.Radius), Style.Hex(0xece5d8, 0.06f)).rectTransform);
                Image glow = Picture(layer, "Glow", Shapes.FocusGlow(r), Style.WithAlpha(Style.White, 0.45f));
                Place(glow.rectTransform, V(0f, 0f), V(1f, 1f), V(0.5f, 0.5f), V(0f, 0f), V(4f + 2f * Shapes.FocusGlowMargin, 4f + 2f * Shapes.FocusGlowMargin));
                Image ring = Picture(layer, "Ring", Shapes.FocusRing(r), EdgeInk);
                Place(ring.rectTransform, V(0f, 0f), V(1f, 1f), V(0.5f, 0.5f), V(0f, 0f), V(4f, 4f));
                effect.Nodes.Add(layer.gameObject);
                effect.Fade = Motion.Group(layer);
                effect.Base = [.. area.Lift.Select(rect => rect ? rect.anchoredPosition : default)];
                break;
            }
            case Hover.Wash:
            {
                Image wash = Picture(at, "Hover", area.Radius > 0f ? Shapes.Rounded(area.Radius) : null, area.Light ? LightWash : Wash);
                Fill(wash.rectTransform);
                effect.Nodes.Add(wash.gameObject);
                effect.Fade = Motion.Group(wash.rectTransform);
                break;
            }
            case Hover.Ring:
            {
                RectTransform layer = Node("Hover", at);
                Place(layer, V(0f, 0f), V(1f, 1f), V(0.5f, 0.5f), V(0f, 0f), V(8f, 8f));
                Fill(Picture(layer, "Ring", Shapes.FocusRing(area.Radius + 4f), Style.Cream).rectTransform);
                effect.Nodes.Add(layer.gameObject);
                effect.Fade = Motion.Group(layer);
                effect.Grow = layer;
                break;
            }
            case Hover.Tab:
            {
                Image line = Picture(at, "Hover", null, TabPreview);
                TopLeft(line.rectTransform, 0f, area.Height - 2f, area.Width, 3f);
                line.rectTransform.pivot = V(0.5f, 0.5f);
                line.rectTransform.anchoredPosition = V(area.Width / 2f, -(area.Height - 0.5f));
                effect.Nodes.Add(line.gameObject);
                effect.Fade = Motion.Group(line.rectTransform);
                effect.Grow = line.rectTransform;
                break;
            }
        }
        return effect;
    }

    // Returns false once the effect has faded out.
    private bool Apply(HoverEffect effect)
    {
        HitArea area = effect.Area;
        if (!area.Rect || effect.Fade == null || !effect.Fade)
            return false;
        float shown = Ease(effect.Shown, effect.Leaving), press = 1f - (1f - PressScale) * effect.Press;
        effect.Fade.alpha = shown;
        foreach (GameObject node in effect.Nodes.Skip(1))
            if (node && node.GetComponent<CanvasGroup>() is { } group)
                group.alpha = shown;
        if (effect.Grow)
        {
            float s = area.Kind == Hover.Tab ? shown : 0.85f + 0.15f * shown;
            effect.Grow.localScale = new Vector3 { x = s, y = area.Kind == Hover.Tab ? 1f : s, z = 1f };
        }
        // Cards rise; cards and buttons dip about their centre while pressed.
        for (int i = 0; i < area.Lift.Length; i++)
        {
            RectTransform group = area.Lift[i];
            if (!group)
                continue;
            Rect box = group.rect;
            float cx = area.X + area.Width / 2f - box.width * group.pivot.x, cy = area.Y + area.Height / 2f - box.height * (1f - group.pivot.y);
            float rise = area.Kind == Hover.Lift ? LiftUnits * shown : 0f;
            Vector2 home = i < effect.Base.Length ? effect.Base[i] : default;
            group.anchoredPosition = V(home.x + cx * (1f - press), home.y + rise - cy * (1f - press));
            group.localScale = new Vector3 { x = press, y = press, z = 1f };
        }
        return !(effect.Leaving && effect.Shown <= 0f);
    }

    private static float Ease(float t, bool leaving) => leaving ? t * t : 1f - (1f - t) * (1f - t) * (1f - t);

    private void Remove(HoverEffect effect)
    {
        foreach (GameObject node in effect.Nodes)
            if (node)
                Object.Destroy(node);
        for (int i = 0; i < effect.Area.Lift.Length; i++)
        {
            RectTransform group = effect.Area.Lift[i];
            if (!group)
                continue;
            group.anchoredPosition = i < effect.Base.Length ? effect.Base[i] : default;
            group.localScale = new Vector3 { x = 1f, y = 1f, z = 1f };
        }
    }
}
