using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Platform;

/// <summary>
/// Reads the machine state the rules need. Every member is safe to call
/// repeatedly and never throws to the caller.
/// </summary>
public sealed class SystemProbe : ISystemProbe
{
    private const int SpiGetScreensaverRunning = 114;
    private const uint DesktopReadObjects = 1;
    private const int ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    // Note: no ExactSpelling games here. user32 only exports the W/A
    // variants, so the entry point must name SystemParametersInfoW exactly.
    // (ExactSpelling=true with the bare name throws EntryPointNotFound and
    // silently disables screensaver detection.)
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint param,
        ref bool value,
        uint winIni);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    private volatile bool _locked;
    private bool _disposed;
    private bool _watching;
    private SessionSwitchEventHandler? _sessionSwitchHandler;

    /// <summary>Raised when the lock state changes. May fire off the UI thread.</summary>
    public event Action<bool>? LockStateChanged;

    /// <summary>How long since the last keyboard or mouse input.</summary>
    public TimeSpan GetInputIdleTime()
    {
        try
        {
            var info = new LastInputInfo
            {
                Size = (uint)Marshal.SizeOf<LastInputInfo>(),
            };

            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            uint now = unchecked((uint)Environment.TickCount);
            uint elapsed = unchecked(now - info.Time);

            // TickCount wraps every 49.7 days; unsigned subtraction wraps with it.
            return TimeSpan.FromMilliseconds(elapsed);
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>True when a screensaver is actually running right now.</summary>
    public bool IsScreensaverRunning()
    {
        bool running = false;
        try
        {
            SystemParametersInfo(SpiGetScreensaverRunning, 0, ref running, 0);
        }
        catch (Exception)
        {
            return false;
        }

        return running;
    }

    /// <summary>Battery when unplugged, AC when plugged in, unknown on a read failure.</summary>
    public PowerSource GetPowerSource()
    {
        try
        {
            if (GetSystemPowerStatus(out SystemPowerStatus status))
            {
                return ClassifyPowerSource(status.AcLineStatus, status.BatteryFlag);
            }
        }
        catch (Exception)
        {
            // Fall through to Unknown.
        }

        return PowerSource.Unknown;
    }

    /// <summary>True when the machine reports battery hardware.</summary>
    public bool HasBattery()
    {
        try
        {
            if (GetSystemPowerStatus(out SystemPowerStatus status))
            {
                // 128 means no battery; 255 means the OS did not say.
                return status.BatteryFlag == 255 || (status.BatteryFlag & 128) == 0;
            }
        }
        catch (Exception)
        {
            // Show the battery column rather than hide a real one.
        }

        return true;
    }

    /// <summary>
    /// Maps the raw Win32 values to a power source. ACLineStatus is
    /// 0 when offline (on battery), 1 when online (on AC), 255 when unknown.
    /// A machine with no battery hardware (flag 128) is always AC.
    /// </summary>
    public static PowerSource ClassifyPowerSource(byte acLineStatus, byte batteryFlag) =>
        Core.PowerSourceClassifier.Classify(acLineStatus, batteryFlag);

    /// <summary>The last lock state seen. Updated by <see cref="WatchLock"/>.</summary>
    public bool IsWorkstationLocked() => _locked;

    /// <summary>
    /// Starts listening for lock and unlock. <paramref name="onChanged"/> is
    /// called with true on lock and false on unlock (on a SystemEvents thread).
    /// Callers must marshal to the UI thread. Never throws.
    /// </summary>
    public void WatchLock(Action<bool> onChanged)
    {
        try
        {
            if (_watching)
            {
                return;
            }

            _watching = true;
            _locked = IsOnLockDesktop();

            _sessionSwitchHandler = (_, args) =>
            {
                try
                {
                    bool locked = args.Reason == SessionSwitchReason.SessionLock;
                    bool unlocked = args.Reason == SessionSwitchReason.SessionUnlock;

                    if (!locked && !unlocked)
                    {
                        return;
                    }

                    if (locked == _locked)
                    {
                        return;
                    }

                    _locked = locked;
                    try
                    {
                        onChanged(locked);
                    }
                    catch (Exception)
                    {
                        // Caller failures must not kill SystemEvents delivery.
                    }

                    try
                    {
                        LockStateChanged?.Invoke(locked);
                    }
                    catch (Exception)
                    {
                    }
                }
                catch (Exception)
                {
                }
            };

            SystemEvents.SessionSwitch += _sessionSwitchHandler;
        }
        catch (Exception)
        {
            // Lock watching is best-effort; polling still works without it.
            _watching = false;
        }
    }

    /// <summary>
    /// The input desktop is Winlogon's while the lock screen is up, and a normal
    /// process cannot open it. That denies us, which is how we detect the lock
    /// state without polling windows.
    /// </summary>
    private static bool IsOnLockDesktop()
    {
        try
        {
            IntPtr desktop = OpenInputDesktop(0, false, DesktopReadObjects);
            if (desktop == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error() == ErrorAccessDenied;
            }

            try
            {
                CloseDesktop(desktop);
            }
            catch (Exception)
            {
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_sessionSwitchHandler is not null)
            {
                SystemEvents.SessionSwitch -= _sessionSwitchHandler;
                _sessionSwitchHandler = null;
            }
        }
        catch (Exception)
        {
        }

        _watching = false;
    }
}
