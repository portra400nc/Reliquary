using System.Runtime.InteropServices;

namespace Reliquary.Ui;

// XInput's button bits.
[Flags]
internal enum PadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Menu = 0x0010,
    View = 0x0020,
    LeftStick = 0x0040,
    LT = 0x0400,
    RT = 0x0800,
    LB = 0x0100,
    RB = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

// The controller as CameraTools reads it: XInput by P/Invoke, xinput1_4.dll first, then xinput9_1_0.dll. Only the
// buttons the panel uses and both sticks are kept.
internal static class Pad
{
    private const int LeftStickDeadzone = 7849;
    private const int RightStickDeadzone = 8689;
    private const float StickPress = 0.5f;
    private const byte TriggerPress = 128;
    private const uint ErrorSuccess = 0;
    private const float ScanInterval = 2f;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint packetNumber;
        public ushort buttons;
        public byte leftTrigger;
        public byte rightTrigger;
        public short thumbLX;
        public short thumbLY;
        public short thumbRX;
        public short thumbRY;
    }

    private static class XInput14
    {
        [DllImport("xinput1_4.dll")]
        public static extern uint XInputGetState(uint userIndex, out XInputState state);
    }

    private static class XInput910
    {
        [DllImport("xinput9_1_0.dll")]
        public static extern uint XInputGetState(uint userIndex, out XInputState state);
    }

    private enum Library { Untried, XInput14, XInput910, Missing }

    private static Library library;
    // Every connected pad is read and their input combined, because the virtual pad that drives the Deck remotely can take
    // a slot and move the Deck's own pad.
    private static readonly bool[] connected = new bool[4];
    private static float nextScan;
    private static bool settle;

    public static PadButtons Held { get; private set; }
    public static PadButtons Previous { get; private set; }
    // The left stick pushed past half way along its stronger axis, as a D-pad press.
    public static PadButtons Stick { get; private set; }
    // The right stick's vertical push from -1 (down) to 1 (up), the strongest of all pads, for scrolling.
    public static float RightY { get; private set; }

    public static bool Pressed(PadButtons button) => (Held & button) != 0 && (Previous & button) == 0;

    public static bool Changed => Held != Previous || Held != PadButtons.None || Stick != PadButtons.None;

    // A button held when the panel opens does not press on the next poll.
    public static void Settle() => settle = true;

    // Polling an empty slot is slow, so a missing pad is looked for at most every two seconds.
    public static void Poll(float now, Action<string> log)
    {
        Previous = Held;
        Held = Stick = PadButtons.None;
        RightY = 0f;
        if (library == Library.Untried)
            Load(log);
        if (library == Library.Missing)
            return;
        bool scan = now >= nextScan;
        if (scan)
            nextScan = now + ScanInterval;
        for (uint slot = 0; slot < connected.Length; slot++)
        {
            if (!connected[slot] && !scan)
                continue;
            bool was = connected[slot];
            connected[slot] = GetState(slot, out XInputState raw) == ErrorSuccess;
            if (connected[slot] && !was)
                log($"controller found in XInput slot {slot}");
            if (connected[slot])
                Read(raw);
        }
        if (settle)
            Previous = Held;
        settle = false;
    }

    private static void Read(XInputState raw)
    {
        Held |= (PadButtons)raw.buttons & (PadButtons.DpadUp | PadButtons.DpadDown | PadButtons.DpadLeft | PadButtons.DpadRight
            | PadButtons.LB | PadButtons.RB | PadButtons.A | PadButtons.B | PadButtons.X | PadButtons.Y | PadButtons.Menu | PadButtons.View | PadButtons.LeftStick);
        if (raw.leftTrigger > TriggerPress)
            Held |= PadButtons.LT;
        if (raw.rightTrigger > TriggerPress)
            Held |= PadButtons.RT;
        float magnitude = MathF.Sqrt((float)raw.thumbLX * raw.thumbLX + (float)raw.thumbLY * raw.thumbLY);
        float scale = magnitude <= LeftStickDeadzone ? 0f : Math.Min((magnitude - LeftStickDeadzone) / (32767f - LeftStickDeadzone), 1f) / magnitude;
        float x = raw.thumbLX * scale, y = raw.thumbLY * scale;
        Stick |= MathF.Abs(y) >= MathF.Abs(x)
            ? y >= StickPress ? PadButtons.DpadUp : y <= -StickPress ? PadButtons.DpadDown : PadButtons.None
            : x >= StickPress ? PadButtons.DpadRight : x <= -StickPress ? PadButtons.DpadLeft : PadButtons.None;
        int right = raw.thumbRY;
        float push = Math.Abs(right) <= RightStickDeadzone ? 0f : Math.Min((Math.Abs(right) - RightStickDeadzone) / (32767f - RightStickDeadzone), 1f) * Math.Sign(right);
        if (Math.Abs(push) > Math.Abs(RightY))
            RightY = push;
    }

    private static void Load(Action<string> log)
    {
        foreach (Library candidate in new[] { Library.XInput14, Library.XInput910 })
        {
            library = candidate;
            try
            {
                GetState(0, out _);
                log($"controller: XInput loaded from {(candidate == Library.XInput14 ? "xinput1_4.dll" : "xinput9_1_0.dll")}");
                return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        library = Library.Missing;
        log("controller: neither xinput1_4.dll nor xinput9_1_0.dll loaded; the panel takes the keyboard only");
    }

    private static uint GetState(uint index, out XInputState state)
        => library == Library.XInput14 ? XInput14.XInputGetState(index, out state) : XInput910.XInputGetState(index, out state);
}
