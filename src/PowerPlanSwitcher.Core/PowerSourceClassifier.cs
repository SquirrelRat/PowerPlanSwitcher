namespace PowerPlanSwitcher.Core;

/// <summary>
/// Maps raw Win32 power-status values to a power source.
/// ACLineStatus is 0 when offline (battery), 1 when online (AC), 255 when unknown.
/// A machine with no battery hardware (flag 128) is always AC.
/// Pure logic, kept in Core so it is unit-testable.
/// </summary>
public static class PowerSourceClassifier
{
    public static PowerSource Classify(byte acLineStatus, byte batteryFlag)
    {
        const byte NoSystemBatteryFlag = 128;

        if ((batteryFlag & NoSystemBatteryFlag) != 0)
        {
            return PowerSource.Ac;
        }

        return acLineStatus == 0 ? PowerSource.Battery : PowerSource.Ac;
    }
}
