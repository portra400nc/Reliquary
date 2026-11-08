using UnityEngine;
using static Reliquary.Ui.Widgets;
using Object = UnityEngine.Object;

namespace Reliquary.Ui;

// Short tweens for the panel's parts, in milliseconds of unscaled time. The panel rebuilds its canvas on every change,
// so a tween drives the new build's parts and simply ends if they are destroyed. A press of any button finishes every
// tween at once, so input never waits on one.
internal sealed class Motion
{
    private sealed class Tween
    {
        public Object Owner;
        public Action<float> Apply;
        public Action Done;
        public float Start, Duration;
        public bool EaseIn;
    }

    private readonly List<Tween> tweens = [];
    private float now;

    public bool Busy => tweens.Count > 0;

    public void Update(float time)
    {
        now = time;
        for (int i = 0; i < tweens.Count; i++)
        {
            Tween tween = tweens[i];
            if (!tween.Owner)
            {
                tweens.RemoveAt(i--);
                continue;
            }
            float t = tween.Duration <= 0f ? 1f : Math.Clamp((now - tween.Start) / tween.Duration, 0f, 1f);
            tween.Apply(Ease(t, tween.EaseIn));
            if (t >= 1f)
            {
                tweens.RemoveAt(i--);
                tween.Done?.Invoke();
            }
        }
    }

    public void FinishAll()
    {
        List<Tween> all = [.. tweens];
        tweens.Clear();
        foreach (Tween tween in all)
        {
            if (tween.Owner)
                tween.Apply(1f);
            tween.Done?.Invoke();
        }
    }

    public void Fade(RectTransform rect, float from, float to, float ms, float delay = 0f, bool easeIn = false, Action done = null)
    {
        CanvasGroup group = Group(rect);
        Add(rect, ms, delay, easeIn, t => group.alpha = Lerp(from, to, t), done);
    }

    // Moves the part from its place plus (dx, dy) to its place, or with away from its place to its place plus (dx, dy).
    public void Shift(RectTransform rect, float dx, float dy, float ms, float delay = 0f, bool away = false, bool easeIn = false)
    {
        Vector2 home = rect.anchoredPosition;
        Add(rect, ms, delay, easeIn, t =>
        {
            float k = away ? t : 1f - t;
            rect.anchoredPosition = V(home.x + dx * k, home.y - dy * k);
        });
    }

    // Scales the part about (x, y) in its parent's units from the top left, which keeps a stretched part's rect in place.
    public void Grow(RectTransform rect, float x, float y, float from, float to, float ms, float delay = 0f, bool easeIn = false)
    {
        PivotAt(rect, x, y);
        Add(rect, ms, delay, easeIn, t =>
        {
            float s = Lerp(from, to, t);
            rect.localScale = new Vector3 { x = s, y = s, z = 1f };
        });
    }

    // Glides a part placed with TopLeft from one rect to another, in its parent's units.
    public void Glide(RectTransform rect, Vector4 from, Vector4 to, float ms)
        => Add(rect, ms, 0f, false, t => TopLeft(rect, Lerp(from.x, to.x, t), Lerp(from.y, to.y, t), Lerp(from.z, to.z, t), Lerp(from.w, to.w, t)));

    // Pops the part about its centre, or about (x, y) in its parent's units for a part that fills its parent.
    public void Pop(RectTransform rect, float ms, float x = float.NaN, float y = float.NaN)
    {
        if (float.IsNaN(x))
            CenterPivot(rect);
        else
            PivotAt(rect, x, y);
        const float delay = 0f;
        Add(rect, ms, delay, false, t =>
        {
            // Up past full size and back, like the mockup's 0.5, 1.15, 1 keyframes.
            float s = t < 0.7f ? Lerp(0.5f, 1.15f, t / 0.7f) : Lerp(1.15f, 1f, (t - 0.7f) / 0.3f);
            rect.localScale = new Vector3 { x = s, y = s, z = 1f };
        });
    }

    // A quick press: down to 96% and back.
    public void Dip(RectTransform rect, float ms = 80f)
    {
        CenterPivot(rect);
        Add(rect, ms, 0f, false, t =>
        {
            float s = 1f - 0.04f * MathF.Sin(t * MathF.PI);
            rect.localScale = new Vector3 { x = s, y = s, z = 1f };
        });
    }

    public void After(RectTransform owner, float ms, Action done) => Add(owner, 0f, ms, false, _ => { }, done);

    public static CanvasGroup Group(RectTransform rect)
    {
        CanvasGroup group = rect.gameObject.GetComponent<CanvasGroup>();
        if (!group)
        {
            group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
        }
        return group;
    }

    private static void PivotAt(RectTransform rect, float x, float y)
    {
        Rect area = rect.rect;
        if (area.width <= 0f || area.height <= 0f)
            return;
        Vector2 before = rect.pivot;
        var pivot = V(x / area.width, 1f - y / area.height);
        rect.pivot = pivot;
        rect.anchoredPosition += V((pivot.x - before.x) * area.width, (pivot.y - before.y) * area.height);
    }

    private static void CenterPivot(RectTransform rect)
    {
        Rect area = rect.rect;
        Vector2 before = rect.pivot;
        var pivot = V(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.anchoredPosition += V((pivot.x - before.x) * area.width, (pivot.y - before.y) * area.height);
    }

    private void Add(Object owner, float ms, float delay, bool easeIn, Action<float> apply, Action done = null)
    {
        var tween = new Tween { Owner = owner, Apply = apply, Done = done, Start = now + delay / 1000f, Duration = ms / 1000f, EaseIn = easeIn };
        apply(0f);
        tweens.Add(tween);
    }

    // Close to the mockup's cubic-bezier(.2, .7, .2, 1) going out, and a quadratic coming in.
    private static float Ease(float t, bool easeIn) => easeIn ? t * t : 1f - (1f - t) * (1f - t) * (1f - t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
