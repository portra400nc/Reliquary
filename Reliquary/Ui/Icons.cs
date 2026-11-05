namespace Reliquary;

// A rectangle in UV units, as a RawImage's uvRect takes it.
public readonly record struct UvRect(float X, float Y, float Width, float Height)
{
    // A sprite's textureRect and its texture's size, both in pixels from the bottom left like UV space.
    public static UvRect Of(float x, float y, float width, float height, float textureWidth, float textureHeight)
        => new(x / textureWidth, y / textureHeight, width / textureWidth, height / textureHeight);
}

// The order the panel asks for its cards' icons: the rows that overlap the view first, then the rest in order. Row r
// of the grid starts at padTop + r * (rowHeight + rowGap) in content space, and the view shows scroll to scroll + view.
public static class IconOrder
{
    public static IEnumerable<int> VisibleFirst(int count, int columns, float scroll, float view, float padTop, float rowHeight, float rowGap)
    {
        float pitch = rowHeight + rowGap;
        int rows = (count + columns - 1) / columns;
        int first = Math.Clamp((int)MathF.Floor((scroll - padTop - rowHeight) / pitch) + 1, 0, rows);
        int last = Math.Clamp((int)MathF.Ceiling((scroll + view - padTop) / pitch) - 1, first - 1, rows - 1);
        int from = Math.Min(first * columns, count), to = Math.Min((last + 1) * columns, count);
        for (int i = from; i < to; i++)
            yield return i;
        for (int i = 0; i < from; i++)
            yield return i;
        for (int i = to; i < count; i++)
            yield return i;
    }
}

// Icons by name for the session. Get loads an icon it has not seen, at most loadsPerFrame a frame, so the callers
// asked first in a frame load first and the rest wait for a later frame. A name the load returns null for is missing
// for the session and is never loaded again.
public sealed class IconCache<T>(Func<string, T> load, int loadsPerFrame) where T : class
{
    private readonly Dictionary<string, T> loaded = [];
    private readonly HashSet<string> missing = [];
    private int frame = int.MinValue;
    private int loads;

    public IReadOnlyCollection<T> Loaded => loaded.Values;

    // The icon called name, or once name is missing the one called fallback; null while this frame's loads are spent or
    // when neither exists.
    public T Get(string name, string fallback, int frame)
        => Find(name, frame) ?? (fallback != null && missing.Contains(name) ? Find(fallback, frame) : null);

    public void Clear()
    {
        loaded.Clear();
        missing.Clear();
    }

    private T Find(string name, int now)
    {
        if (loaded.TryGetValue(name, out T icon))
            return icon;
        if (missing.Contains(name))
            return null;
        if (now != frame)
        {
            frame = now;
            loads = 0;
        }
        if (loads >= loadsPerFrame)
            return null;
        loads++;
        icon = load(name);
        if (icon == null)
            missing.Add(name);
        else
            loaded[name] = icon;
        return icon;
    }
}
