using UnityEngine;
using UnityEngine.UI;
using static Reliquary.Ui.Widgets;
using Object = UnityEngine.Object;

namespace Reliquary.Ui;

// Scrolling, as on iOS: every list keeps a continuous offset (ScrollState). A drag moves the content with the pointer,
// a flick coasts and slows, the ends stretch and spring back, and the wheel eases by a step. The content node moves
// every frame without a rebuild. A long list draws only the rows near its window and redraws just those rows when the
// window moves past them. The controller's focus eases its row into view. A click acts on release, when the pointer
// has barely moved.
internal sealed partial class Panel
{
    private sealed class ScrollList(bool across, float wheelStep)
    {
        public readonly ScrollState State = new();
        public readonly bool Across = across;
        public readonly float WheelStep = wheelStep;
        public RectTransform Window, Content;
        public Vector2 Home;
        public float Margin = float.PositiveInfinity, DrawnFrom, DrawnTo;
        public Action<RectTransform> Draw;
        // Runs whenever the content moves, for a scroll bar.
        public Action Moved;
        public bool Live => Content;
    }

    private const float DragStart = 10f;

    private readonly ScrollList strip = new(true, FaceStep * 3f), templateList = new(false, 66f), teamList = new(false, TeamRowHeight),
        rosterList = new(false, CardHeight + CardGapY), pickerList = new(false, 154f), setList = new(false, 45f * 2f), detailList = new(false, 60f);
    private ScrollList[] Lists => [strip, templateList, teamList, rosterList, pickerList, setList, detailList];

    private readonly List<ScrollList> scrollers = [];
    private bool pressed, dragging;
    private Vector2 pressAt;
    private ScrollList dragged;
    private Action pressClick;

    // An overlay takes the mouse from what lies under it.
    private void ClearInput()
    {
        hits.Clear();
        scrollers.Clear();
    }

    // A scrolled window at (x, y, width, height) over content of the given length, measured along the scroll axis from
    // the window's start. draw places the content's parts in stage units as if unscrolled; a list with a margin draws
    // only what lies within margin of the window, read from DrawnFrom and DrawnTo. Returns the content node.
    private RectTransform Scrolled(ScrollList list, RectTransform parent, string name, float x, float y, float width, float height, float length, Action<RectTransform> draw, float margin = float.PositiveInfinity, Action<ScrollState> before = null)
    {
        RectTransform content = Clip(parent, name, x, y, width, height);
        list.Window = content.parent.TryCast<RectTransform>();
        list.Content = content;
        list.Home = content.anchoredPosition;
        list.Margin = margin;
        list.Draw = draw;
        list.Moved = null;
        list.State.Measure(list.Across ? width : height, length);
        before?.Invoke(list.State);
        scrollers.Add(list);
        DrawWindow(list);
        return content;
    }

    private void DrawWindow(ScrollList list)
    {
        float view = list.State.View, offset = list.State.Offset;
        list.DrawnFrom = offset - list.Margin;
        list.DrawnTo = offset + view + list.Margin;
        list.Draw(list.Content);
        PlaceContent(list);
    }

    private static void PlaceContent(ScrollList list)
    {
        float offset = list.State.Offset;
        list.Content.anchoredPosition = list.Across ? V(list.Home.x - offset, list.Home.y) : V(list.Home.x, list.Home.y + offset);
        list.Moved?.Invoke();
    }

    // Whether the content span [start, end], measured from the window's start, lies within what the list draws.
    private static bool Drawn(ScrollList list, float start, float end) => end >= list.DrawnFrom && start <= list.DrawnTo;

    // Eases the focus into view after the controller moved it, or puts it there at once when the list first shows.
    private void Follow(ScrollState state, bool moved, bool first, float start, float end, float pad)
    {
        if (first)
            state.Reveal(start, end, pad, Time.unscaledTime, ease: false);
        else if (moved)
            state.Reveal(start, end, pad, Time.unscaledTime);
    }

    // Each frame: the physics moves, the content follows, and a long list redraws its rows when the window leaves them.
    private void UpdateScrolling(float now)
    {
        foreach (ScrollList list in Lists)
        {
            if (!list.Live)
                continue;
            if (list.State.Step(now) || list.State.Dragging)
                PlaceContent(list);
            float offset = list.State.Offset;
            if (!float.IsPositiveInfinity(list.Margin) && (offset < list.DrawnFrom || offset + list.State.View > list.DrawnTo))
            {
                Widgets.BeginReuse(list.Content);
                try
                {
                    DrawWindow(list);
                }
                finally
                {
                    Widgets.EndReuse();
                }
            }
        }
    }

    private ScrollList ScrollerAt(Vector2 point)
    {
        for (int i = scrollers.Count - 1; i >= 0; i--)
            if (scrollers[i].Window && RectTransformUtility.RectangleContainsScreenPoint(scrollers[i].Window, point, null))
                return scrollers[i];
        return null;
    }

    private Action HitAt(Vector2 point)
    {
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Rect && RectTransformUtility.RectangleContainsScreenPoint(hits[i].Rect, point, null))
                return hits[i].Click;
        return null;
    }

    // The pointer along a list's axis in canvas units, decreasing as the content is pulled forward.
    private static float Along(ScrollList list, Vector2 point)
    {
        float scale = Math.Min(Screen.width / Style.ReferenceWidth, Screen.height / Style.ReferenceHeight);
        return list.Across ? point.x / scale : -point.y / scale;
    }

    private void MouseInput(float now)
    {
        var point = new Vector2 { x = Input.mousePosition.x, y = Input.mousePosition.y };
        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f && !dragging && ScrollerAt(point) is { } turned)
            turned.State.Wheel(-MathF.Sign(wheel) * turned.WheelStep, now);
        if (Input.GetMouseButtonDown(0))
        {
            pressed = true;
            dragging = false;
            pressAt = point;
            dragged = ScrollerAt(point);
            // A press that stops a coasting list only stops it.
            pressClick = dragged != null && dragged.State.Catch() ? null : HitAt(point);
            return;
        }
        if (!pressed)
            return;
        if (Input.GetMouseButton(0))
        {
            float moved = MathF.Sqrt((point.x - pressAt.x) * (point.x - pressAt.x) + (point.y - pressAt.y) * (point.y - pressAt.y));
            if (!dragging && dragged != null && dragged.Live && moved > DragStart)
            {
                dragging = true;
                dragged.State.BeginDrag(Along(dragged, point), now);
            }
            if (dragging && dragged.Live)
                dragged.State.DragTo(Along(dragged, point), now);
            return;
        }
        pressed = false;
        if (dragging)
            dragged?.State.EndDrag(now);
        else if (pressClick != null)
        {
            pressClick();
            dirty = true;
        }
        dragging = false;
    }

    // Portraits not yet loaded load one per frame, so scrolling never waits on them. A fallback name is tried when the
    // first has no icon, as for the circle portraits.
    private readonly List<(RawImage Image, string Icon, string Fallback, int Size)> portraitQueue = [];

    private void QueuePortrait(RawImage image, string icon, string fallback, int size)
    {
        image.enabled = false;
        portraitQueue.Add((image, icon, fallback, size));
    }

    private void LoadOnePortrait()
    {
        while (portraitQueue.Count > 0)
        {
            var (image, icon, fallback, size) = portraitQueue[0];
            portraitQueue.RemoveAt(0);
            if (!image)
                continue;
            Texture texture = icons.Small(icon, size) ?? (fallback != null ? icons.Small(fallback, size) : null);
            image.texture = texture;
            image.enabled = texture;
            return;
        }
    }
}
