using Microsoft.Extensions.Logging;
using ReadyM.Api.DI;
using ReadyM.Modloader.Mods;
using ReadyM.Sdk.Common;

namespace OblivionMenuSkip;

public sealed class Mod : ModBase
{
    internal static MainMenuStart? Entry;
    public override string Name => "Oblivion Menu Skip";

    protected override void RegisterServices(IDependencyContainer services)
        => services.RegisterSingleton<ModSystemBase, MenuSkipSystem>();

    public override void Start()
    {
        Entry = new MainMenuStart(Logger);
        Logger.LogInformation("[MenuSkip] Loaded; waiting for the ReadyM connection and native main menu.");
    }

    public override void DeInit()
    {
        Entry?.Dispose();
        Entry = null;
        base.DeInit();
    }
}

public sealed class MenuSkipSystem(ILogger logger) : ModSystemBase
{
    protected override void OnUpdate(UpdateTick tick)
    {
        if (Mod.Entry is not { Completed: false } entry) return;
        try { entry.Frame(Environment.TickCount64 / 1000d, HostState.PlayerRestored()); }
        catch (Exception ex)
        {
            entry.Dispose();
            logger.LogError(ex, "[MenuSkip] Stopped; native menu presentation restored where available.");
        }
    }
}
