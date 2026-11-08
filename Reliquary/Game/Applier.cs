namespace Reliquary;

// Sends one equip request at a time and waits for the server's change to reach the slot before the next, because each
// swap decides what the next request finds. The whole queue stops at the first refusal.
internal sealed class Applier(Gear gear, Action<IReadOnlyList<Move>, Move?> finished)
{
    private const int ConfirmMs = 4_000;
    private readonly Queue<Move> queue = new();
    private readonly List<Move> done = [];
    private Move current;
    private long sent;
    private bool waiting;

    public bool Busy => waiting || queue.Count > 0;
    public int Done => done.Count;
    public int Total { get; private set; }

    public void Start(IEnumerable<Move> moves)
    {
        queue.Clear();
        done.Clear();
        foreach (Move move in moves)
            queue.Enqueue(move);
        Total = queue.Count;
        Next();
    }

    public void Update()
    {
        if (!waiting)
            return;
        if (gear.Equipped(current.Avatar, current.Slot) == current.Item)
        {
            done.Add(current);
            Next();
        }
        else if (Environment.TickCount64 - sent > ConfirmMs)
        {
            waiting = false;
            queue.Clear();
            finished(done, current);
        }
    }

    private void Next()
    {
        if (queue.Count == 0)
        {
            waiting = false;
            finished(done, null);
            return;
        }
        current = queue.Dequeue();
        sent = Environment.TickCount64;
        waiting = true;
        gear.Send(current.Avatar, current.Item);
    }
}
