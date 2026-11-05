using MelonLoader;
using Reliquary.Ui;
using UnityEngine;

namespace Reliquary;

public sealed class ReliquaryMod : MelonMod
{
    // Touching IL2CPP too early breaks the login.
    private const long ArmDelayMs = 45_000;
    private long initializedAt;
    private Panel? panel;

    public override void OnInitializeMelon() => initializedAt = Environment.TickCount64;

    public override void OnLateUpdate()
    {
        if (panel == null)
        {
            if (Environment.TickCount64 - initializedAt < ArmDelayMs)
                return;
            panel = new Panel(message => LoggerInstance.Msg(message), message => LoggerInstance.Warning(message));
        }
        panel.Update(Time.unscaledTime, Time.unscaledDeltaTime);
    }
}
