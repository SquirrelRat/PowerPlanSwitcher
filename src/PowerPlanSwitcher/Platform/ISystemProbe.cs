using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Platform;

/// <summary>Reads the machine state the rules need.</summary>
public interface ISystemProbe : IDisposable
{
    TimeSpan GetInputIdleTime();

    bool IsScreensaverRunning();

    bool IsWorkstationLocked();

    PowerSource GetPowerSource();

    bool HasBattery();

    void WatchLock(Action<bool> onChanged);
}
