using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerPlanSwitcher.Platform;

/// <summary>A power plan as the OS reports it.</summary>
public sealed record PowerPlanInfo(Guid Guid, string Name);

/// <summary>
/// Thin wrapper over powrprof.dll. Every call returns or reports rather than throwing.
/// This is the only file in the app that talks to the power APIs.
/// </summary>
public static class PowerPlanApi
{
    private const uint AccessScheme = 16;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;
    private const uint ErrorSuccess = 0;

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userPowerKey, ref Guid activePolicyGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    /// <summary>Every power plan on this machine, in the OS order.</summary>
    public static IReadOnlyList<PowerPlanInfo> ListPlans()
    {
        var plans = new List<PowerPlanInfo>();

        try
        {
            for (uint index = 0; ; index++)
            {
                uint size = (uint)Marshal.SizeOf<Guid>();
                IntPtr buffer = Marshal.AllocHGlobal((int)size);

                try
                {
                    uint result = PowerEnumerate(
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        AccessScheme,
                        index,
                        buffer,
                        ref size);

                    if (result == ErrorNoMoreItems)
                    {
                        break;
                    }

                    if (result != ErrorSuccess)
                    {
                        return plans;
                    }

                    var guid = Marshal.PtrToStructure<Guid>(buffer);
                    var name = ReadPlanName(guid);
                    plans.Add(new PowerPlanInfo(guid, name));
                }
                catch (Exception)
                {
                    return plans;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch (Exception)
        {
            // Return what we have; callers treat empty as "unknown".
        }

        return plans;
    }

    /// <summary>The active plan, or null when it could not be read.</summary>
    public static PowerPlanInfo? GetActivePlan()
    {
        IntPtr pointer = IntPtr.Zero;

        try
        {
            try
            {
                uint result = PowerGetActiveScheme(IntPtr.Zero, out pointer);
                if (result != ErrorSuccess)
                {
                    return null;
                }

                var guid = Marshal.PtrToStructure<Guid>(pointer);
                return new PowerPlanInfo(guid, ReadPlanName(guid));
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    try
                    {
                        LocalFree(pointer);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes a plan active and confirms it took effect.
    /// Returns true only when the OS reports the new plan as active.
    /// </summary>
    public static bool TrySetActivePlan(Guid planGuid)
    {
        try
        {
            Guid target = planGuid;
            uint result = PowerSetActiveScheme(IntPtr.Zero, ref target);
            if (result != ErrorSuccess)
            {
                return false;
            }

            return GetActivePlan()?.Guid == planGuid;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ReadPlanName(Guid planGuid)
    {
        uint size = 0;
        IntPtr probe = IntPtr.Zero;

        try
        {
            try
            {
                // First call with a null buffer reports the size we need.
                uint first = PowerReadFriendlyName(
                    IntPtr.Zero,
                    ref planGuid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ref size);

                if (first != ErrorSuccess && first != ErrorMoreData)
                {
                    return planGuid.ToString();
                }

                if (size == 0 || size > 4096)
                {
                    return planGuid.ToString();
                }

                probe = Marshal.AllocHGlobal((int)size);

                uint result = PowerReadFriendlyName(
                    IntPtr.Zero,
                    ref planGuid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    probe,
                    ref size);

                if (result != ErrorSuccess)
                {
                    return planGuid.ToString();
                }

                return Marshal.PtrToStringUni(probe) ?? planGuid.ToString();
            }
            catch (Exception)
            {
                return planGuid.ToString();
            }
            finally
            {
                if (probe != IntPtr.Zero)
                {
                    try
                    {
                        Marshal.FreeHGlobal(probe);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception)
        {
            return planGuid.ToString();
        }
    }
}
