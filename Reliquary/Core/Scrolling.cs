namespace Reliquary;

// A scroll position that moves like iOS: the content follows a drag exactly, a flick keeps going and slows down, and
// past either end it stretches like a rubber band and springs back. Offsets and pointers are in canvas units along the
// scroll axis; a pointer that decreases pulls the content forward. Times are seconds. No snapping to rows.
public sealed class ScrollState
{
    // iOS's normal deceleration: the speed falls to 0.998 of itself each millisecond.
    private const float DecelerationPerSecond = 2.002f;
    private const float StopSpeed = 12f;
    private const float MaxSpeed = 9000f;
    // A spring that brings the content back inside, near critically damped, settling in about 0.4 s.
    private const float Stiffness = 220f;
    private const float Damping = 29.7f;
    private const float VelocityWindow = 0.08f;
    private const float StillBeforeRelease = 0.06f;
    private const float EaseSeconds = 0.22f;
    private const float Substep = 1f / 240f;

    private readonly List<(float Time, float Pointer)> samples = [];
    private bool dragging;
    private float dragStartOffset, dragStartPointer, velocity, lastStep = float.NaN;
    private float easeFrom, easeTo, easeStart = float.NaN;

    public float Offset { get; private set; }
    public float View { get; private set; }
    public float Content { get; private set; }
    public float Max => MathF.Max(0f, Content - View);
    public bool Dragging => dragging;
    public bool Moving => dragging || velocity != 0f || !float.IsNaN(easeStart) || Offset < 0f || Offset > Max;

    // The window's length and the content's, taken from each drawing.
    public void Measure(float view, float content)
    {
        View = view;
        Content = content;
        if (!Moving)
            Offset = Clamp(Offset);
    }

    public void Jump(float offset)
    {
        Stop();
        Offset = Clamp(offset);
    }

    // Moves by the given units at once, for a stick held on the list.
    public void Nudge(float by)
    {
        if (dragging || by == 0f)
            return;
        Stop();
        Offset = Clamp(Offset + by);
    }

    // A press on a moving list stops it where it is, as a tap does on iOS.
    public bool Catch()
    {
        bool wasMoving = velocity != 0f || !float.IsNaN(easeStart);
        Stop();
        return wasMoving;
    }

    public void BeginDrag(float pointer, float time)
    {
        Stop();
        dragging = true;
        dragStartOffset = Offset;
        dragStartPointer = pointer;
        samples.Clear();
        samples.Add((time, pointer));
    }

    public void DragTo(float pointer, float time)
    {
        if (!dragging)
            return;
        float raw = dragStartOffset + (dragStartPointer - pointer);
        Offset = raw < 0f ? -Band(-raw) : raw > Max ? Max + Band(raw - Max) : raw;
        samples.Add((time, pointer));
        while (samples.Count > 2 && time - samples[0].Time > VelocityWindow)
            samples.RemoveAt(0);
    }

    public void EndDrag(float time)
    {
        if (!dragging)
            return;
        dragging = false;
        velocity = 0f;
        var (lastTime, lastPointer) = samples[^1];
        var (firstTime, firstPointer) = samples[0];
        if (time - lastTime <= StillBeforeRelease && lastTime - firstTime > 0.001f)
            velocity = Math.Clamp(-(lastPointer - firstPointer) / (lastTime - firstTime), -MaxSpeed, MaxSpeed);
        lastStep = time;
    }

    // A wheel notch eases by the given units; notches in a row add up.
    public void Wheel(float by, float time)
    {
        if (dragging)
            return;
        float from = float.IsNaN(easeStart) ? Offset : easeTo;
        EaseTo(Clamp(from + by), time);
    }

    // Brings [start, end] into view with pad around it, easing there or jumping.
    public void Reveal(float start, float end, float pad, float time, bool ease = true)
    {
        if (dragging)
            return;
        float target = Offset;
        if (start - pad < target)
            target = start - pad;
        else if (end + pad > target + View)
            target = end + pad - View;
        target = Clamp(target);
        if (MathF.Abs(target - Offset) < 0.01f)
            return;
        if (ease)
            EaseTo(target, time);
        else
            Jump(target);
    }

    // Advances the fling, the ease and the spring. Returns whether the offset changed.
    public bool Step(float time)
    {
        float dt = float.IsNaN(lastStep) ? 0f : MathF.Min(time - lastStep, 0.05f);
        lastStep = time;
        if (dragging || dt <= 0f)
            return false;
        float before = Offset;
        if (!float.IsNaN(easeStart))
        {
            float t = MathF.Min(1f, (time - easeStart) / EaseSeconds);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            Offset = easeFrom + (easeTo - easeFrom) * eased;
            if (t >= 1f)
                easeStart = float.NaN;
            return Offset != before;
        }
        // Small fixed steps: at the Deck's frame times one big step makes the spring unstable and throws the list.
        for (float left = dt; left > 0f; left -= Substep)
            Advance(MathF.Min(Substep, left));
        return Offset != before;
    }

    private void Advance(float dt)
    {
        float bound = Offset < 0f ? 0f : Offset > Max ? Max : Offset;
        if (bound != Offset)
        {
            // Outside: a damped spring pulls the content back, which also turns a fling into a bounce.
            float acceleration = -Stiffness * (Offset - bound) - Damping * velocity;
            velocity += acceleration * dt;
            Offset += velocity * dt;
            float after = Offset < 0f ? 0f : Offset > Max ? Max : Offset;
            if (MathF.Abs(Offset - after) < 0.3f && MathF.Abs(velocity) < StopSpeed * 4f)
            {
                Offset = after;
                velocity = 0f;
            }
        }
        else if (velocity != 0f)
        {
            velocity *= MathF.Exp(-DecelerationPerSecond * dt);
            Offset += velocity * dt;
            if (MathF.Abs(velocity) < StopSpeed)
                velocity = 0f;
        }
    }

    // iOS's rubber band: the pull past the end shrinks the further it goes, never beyond the window's length.
    private float Band(float past) => View <= 0f ? 0f : (1f - 1f / (past * 0.55f / View + 1f)) * View;

    private float Clamp(float offset) => Math.Clamp(offset, 0f, Max);

    private void EaseTo(float target, float time)
    {
        velocity = 0f;
        easeFrom = Offset;
        easeTo = target;
        easeStart = time;
        lastStep = time;
    }

    private void Stop()
    {
        velocity = 0f;
        easeStart = float.NaN;
    }
}
