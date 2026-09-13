using System.Reflection;
using Microsoft.Extensions.Logging;

namespace OblivionMenuSkip;

// ReadyM authenticates before the game enters a world. Reuse its native world-save
// hook once the real main menu is ready; this never creates or selects a user save.
internal sealed class MainMenuStart : IDisposable
{
    static bool finishedForProcess;
    static bool dispatchedForProcess;
    readonly ILogger logger;
    UnrealBridge? bridge;
    NativeWeakObject menu, model, controller, world;
    byte originalButtons;
    bool presentationOwned, fadeOwned, finished, dispatched;
    double nextPoll, fadeAt, dispatchedAt;
    string lastGate="";
    void Waiting(string reason)
    {
        if(lastGate==reason)return;
        lastGate=reason;Log?.LogInformation("[MenuSkip] Main-menu entry: {State}",reason);
    }

    public MainMenuStart(ILogger logger) => this.logger = logger;
    public bool Completed => finished;
    ILogger Log => logger;
    UnrealBridge U => bridge ??= new UnrealBridge();

    public void Frame(double now, bool playerReady)
    {
        if (finished) return;
        if (playerReady)
        {
            finishedForProcess = true;
            finished = true;
            RestorePresentation();
            if (dispatched) Log?.LogInformation("[MenuSkip] Server starter loaded from the main menu.");
            return;
        }
        if (finishedForProcess || (dispatchedForProcess && !dispatched))
        {
            finished = true;
            return;
        }
        if (now < nextPoll) return;
        nextPoll = now + .25;

        try
        {
            if (dispatched)
            {
                if (now - dispatchedAt > 120)
                    Fail("The server starter did not finish loading within two minutes. The normal menu has been restored.");
                return;
            }

            if (presentationOwned)
            {
                if (!ServerConnected(out _))
                {
                    Fail("The server connection closed before loading began.");
                    return;
                }
                if (now - fadeAt < .4) return;
                var liveMenu = U.ResolveWeak(menu);
                var liveWorld = U.ResolveWeak(world);
                if (liveMenu == 0 || liveWorld == 0 || !U.Boolean(liveMenu, "IsVisible"))
                {
                    Fail("The main menu changed before the server starter could load.");
                    return;
                }
                var library = U.DefaultObject("/Script/Altar.Default__VLevelChangeData");
                var level = U.Pointer(library, "GetLevelChangeData", ("InWorld", liveWorld));
                if (level == 0) throw new InvalidOperationException("The native level-loading service is unavailable.");

                // Mark before dispatch: even an uncertain native result must not issue a second load.
                dispatched = dispatchedForProcess = true;
                dispatchedAt = now;
                U.Call(level, "QuickLoadSaveAfterFadeToBlack");
                Log?.LogInformation("[MenuSkip] Loading the ReadyM server starter from the main menu.");
                return;
            }

            if (!ServerConnected(out var host)){Waiting("waiting for accepted network connection");return;}
            var live = U.FindInstance("WBP_LegacyMenu_Main_C");
            if(live==0)live=U.FindInstance("VLegacyMainMenu");
            if (live == 0){Waiting("waiting for main-menu widget");return;}
            if(!U.Boolean(live,"IsVisible")){Waiting("waiting for visible main menu");return;}
            var viewModel = U.FindInstance("VMainMenuViewModel");
            if(viewModel==0){Waiting("waiting for main-menu model");return;}
            var buttons=U.ResultBytes(U.Call(viewModel,"GetButtonsVisibility"),"GetButtonsVisibility")[0];
            // ReadyM has already signed in. Visible normal menu buttons are the
            // readiness signal; the game's onboarding/movie flags can remain set.
            if(buttons!=2){Waiting("waiting for normal menu buttons (state "+buttons+")");return;}

            ValidateStarter(host!);
            var currentWorld = U.Pointer(live, "GetCurrentWorld");
            if (currentWorld == 0){Waiting("waiting for menu world");return;}
            var gameplay = U.DefaultObject("/Script/Engine.Default__GameplayStatics");
            var currentController = U.Pointer(gameplay, "GetPlayerController",
                ("WorldContextObject", currentWorld), ("PlayerIndex", 0));
            if (currentController == 0 || U.Object(currentController, "PlayerCameraManager") == 0){Waiting("waiting for menu camera");return;}
            Waiting("menu ready; fading into server load");

            menu = U.Weak(live);
            model = U.Weak(viewModel);
            world = U.Weak(currentWorld);
            controller = U.Weak(currentController);
            originalButtons = U.ResultBytes(U.Call(viewModel, "GetButtonsVisibility"), "GetButtonsVisibility")[0];
            presentationOwned = true;
            U.Call(viewModel, "SetButtonsVisibility", ("NewButtonsVisibility", (byte)0));
            fadeOwned = true;
            Fade(currentController, 0, 1, .35f, true);
            fadeAt = now;
        }
        catch (Exception ex)
        {
            Fail(ex.GetBaseException().Message);
        }
    }

    static bool ServerConnected(out Assembly? host)=>ServerIdentity(out host) is not null;

    internal static string? ServerIdentity(out Assembly? host)
        => TryReadConnection(out var identity, out host) ? identity : null;

    internal static bool TryReadConnection(out string? identity, out Assembly? host)
    {
        identity = null;
        host = null;
        try
        {
            host = Assembly.Load("OblivionMpCSharpMod");
            var di = host.GetType("OblivionMpCSharpMod.DI", true)!;
            var instance = di.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var connection = di.GetProperty("RelayClient", BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance)?.GetValue(instance);
            if (connection is null) return false;
            var requested = connection.GetType().GetProperty("RequestedConnect")?.GetValue(connection);
            if (requested is not bool connected) return false;
            if (!connected) return true;
            var playerProperty = connection.GetType().GetProperty("PlayerId");
            if (playerProperty is null) return false;
            var playerId = playerProperty.GetValue(connection);
            if (playerId is null) return true; // Readable, but no accepted player yet.
            identity = playerId.ToString();
            return !string.IsNullOrEmpty(identity);
        }
        catch
        {
            // Host initialization can precede connection service readiness. Poll again without
            // resolving launch parameters or reading the credential-bearing handshake.
            return false;
        }
    }

    static void ValidateStarter(Assembly host) => StarterFile.Validate(host.Location);

    void Fade(nint playerController, float from, float to, float duration, bool hold)
    {
        var manager = U.Object(playerController, "PlayerCameraManager");
        if (manager != 0)
            U.Call(manager, "StartCameraFade", ("FromAlpha", from), ("ToAlpha", to), ("Duration", duration),
                ("Color", new byte[16]), ("bShouldFadeAudio", false), ("bHoldWhenFinished", hold));
    }

    void Fail(string reason)
    {
        finished = finishedForProcess = true;
        RestorePresentation();
        Log?.LogWarning("[MenuSkip] Automatic main-menu start stopped: {Reason}", reason);
    }

    void RestorePresentation()
    {
        if (bridge is null) return;
        if (presentationOwned)
        {
            try
            {
                var liveModel = U.ResolveWeak(model);
                if (liveModel != 0) U.Call(liveModel, "SetButtonsVisibility", ("NewButtonsVisibility", originalButtons));
            }
            catch (Exception ex) { Log?.LogWarning("[MenuSkip] Could not restore main-menu buttons: {Reason}", ex.GetBaseException().Message); }
        }
        if (fadeOwned)
        {
            try
            {
                var liveController = U.ResolveWeak(controller);
                if (liveController != 0) Fade(liveController, 1, 0, .2f, false);
            }
            catch (Exception ex) { Log?.LogWarning("[MenuSkip] Could not release main-menu fade: {Reason}", ex.GetBaseException().Message); }
        }
        presentationOwned = fadeOwned = false;
        menu = model = controller = world = default;
    }

    public void Dispose()
    {
        RestorePresentation();
        finished = true;
    }
}
