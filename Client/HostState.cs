using System.Reflection;

namespace OblivionMenuSkip;

internal static class HostState
{
    // Read-only readiness check. ReadyM continues to own player restoration and saving.
    public static bool PlayerRestored()
    {
        try
        {
            var di = Assembly.Load("OblivionMpCSharpMod").GetType("OblivionMpCSharpMod.DI", true)!;
            var instance = di.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            if (instance is null) return false;
            var state = di.GetProperty("OblivionClientState")?.GetValue(instance);
            if (state?.GetType().GetProperty("CurrentAreaId")?.GetValue(state) is null) return false;
            var save = di.GetProperty("PlayerSave")?.GetValue(instance);
            return save?.GetType().GetField("_restoreCompleted", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(save) is true;
        }
        catch { return false; } // Host services may still be initializing.
    }
}
