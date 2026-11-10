using UnityEngine;
using static Reliquary.Ui.Widgets;

namespace Reliquary.Ui;

// The mouse, as in the game: the wheel or a drag with the left button scrolls the part under the pointer, and a click
// acts when the button comes up without the pointer having moved. Each build registers its scrollable windows. A
// window scrolled by the mouse stays where it was put, away from the controller's focus, until the focus moves.
internal sealed partial class Panel
{
    // Rows moves a stepped list by whole rows (forward is down, or right across), Units moves a smooth one by canvas units.
    private sealed record Scroller(RectTransform Rect, bool Across, float Step, Action<int> Rows, Action<float> Units);

    private const float DragStart = 10f;
    private const float DetailWheelStep = 40f;

    private readonly List<Scroller> scrollers = [];
    private int? stripTop, rosterTop, pickerTop, setTop, teamTop;
    private bool pressed, dragging;
    private Vector2 pressAt, lastPoint;
    private Scroller dragged;
    private Action pressClick;
    private float dragCarry;

    // An overlay takes the mouse from what lies under it.
    private void ClearInput()
    {
        hits.Clear();
        scrollers.Clear();
    }

    private void Scroll(Transform parent, float x, float y, float width, float height, float step, Action<int> rows, bool across = false)
    {
        RectTransform rect = Node("Scroll", parent);
        TopLeft(rect, x, y, width, height);
        scrollers.Add(new Scroller(rect, across, step, rows, null));
    }

    private void ScrollSmooth(Transform parent, float x, float y, float width, float height, Action<float> units)
    {
        RectTransform rect = Node("Scroll", parent);
        TopLeft(rect, x, y, width, height);
        scrollers.Add(new Scroller(rect, false, 1f, null, units));
    }

    private Scroller ScrollerAt(Vector2 point)
    {
        for (int i = scrollers.Count - 1; i >= 0; i--)
            if (scrollers[i].Rect && RectTransformUtility.RectangleContainsScreenPoint(scrollers[i].Rect, point, null))
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

    private void MouseInput()
    {
        var point = new Vector2 { x = Input.mousePosition.x, y = Input.mousePosition.y };
        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f && !pressed && ScrollerAt(point) is { } turned)
        {
            if (turned.Units != null)
                turned.Units(-wheel * DetailWheelStep);
            else
                turned.Rows(wheel > 0f ? -1 : 1);
            dirty = true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            pressed = true;
            dragging = false;
            pressAt = lastPoint = point;
            dragged = ScrollerAt(point);
            pressClick = HitAt(point);
            dragCarry = 0f;
            return;
        }
        if (!pressed)
            return;
        if (Input.GetMouseButton(0))
        {
            float moved = MathF.Sqrt((point.x - pressAt.x) * (point.x - pressAt.x) + (point.y - pressAt.y) * (point.y - pressAt.y));
            if (!dragging && dragged != null && moved > DragStart)
                dragging = true;
            if (dragging)
                Drag(point);
            lastPoint = point;
            return;
        }
        pressed = false;
        if (!dragging && pressClick != null)
        {
            pressClick();
            dirty = true;
        }
        dragging = false;
    }

    // The content follows the pointer: pulling up shows what is further down, pulling left what is further right.
    private void Drag(Vector2 point)
    {
        float scale = Math.Min(Screen.width / Style.ReferenceWidth, Screen.height / Style.ReferenceHeight);
        float units = (dragged.Across ? lastPoint.x - point.x : point.y - lastPoint.y) / scale;
        if (dragged.Units != null)
        {
            dragged.Units(units);
            return;
        }
        dragCarry += units;
        int rows = (int)(dragCarry / dragged.Step);
        if (rows == 0)
            return;
        dragCarry -= rows * dragged.Step;
        dragged.Rows(rows);
        dirty = true;
    }

    // The strip moves without changing who is chosen; a click on a face chooses them.
    private void ScrollStrip(int faces)
    {
        int count = gear.Characters.Count;
        if (count > 9)
            stripTop = Math.Clamp(StripStart() + faces, 0, count - 9);
    }

    private void ScrollRoster(int rows)
    {
        int last = Math.Max(0, (Math.Max(roster.Count, 1) - 1) / RosterColumns - RosterRows + 1);
        rosterTop = Math.Clamp(RosterFirstRow() + rows, 0, last);
    }

    private void ScrollPicker(int rows)
    {
        int last = Math.Max(0, (pickerItems.Count - 1) / PickerColumns - PickerRows + 1);
        pickerTop = Math.Clamp(PickerFirst() + rows, 0, last);
    }

    private void ScrollSets(int rows)
    {
        int last = Math.Max(0, (setRows.Count + 1) / 2 - SetRowsShown);
        setTop = Math.Clamp(SetFirstRow() + rows, 0, last);
    }

    private void ScrollTeams(int rows)
    {
        int last = Math.Max(0, library.Teams.Count - TeamRowsShown);
        teamTop = Math.Clamp(TeamFirstRow() + rows, 0, last);
    }

    private int TeamFirstRow()
    {
        int count = library.Teams.Count, last = Math.Max(0, count - TeamRowsShown);
        return teamTop is { } top ? Math.Clamp(top, 0, last) : Math.Max(0, Math.Min(team - TeamRowsShown + 1, last));
    }
}
