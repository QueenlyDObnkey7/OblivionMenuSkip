# Oblivion Menu Skip

A small C# mod for **Oblivion Remastered through ReadyM / OBMP**. After ReadyM connects and the native main menu is ready, it fades the menu out and automatically loads ReadyM's supplied `world.sav`. Players do not need to click Continue, New, or Load.

This package contains only automatic main-menu entry. It has no character selection, character creation, custom spawn location, teleport commands, account database, or character-save system. ReadyM remains responsible for authentication, the starter save, player restoration, and gameplay.

## Install on a ReadyM server

1. Stop the server.
2. Extract the release ZIP into the server directory so the layout is:

   ```text
   mods/
     OblivionMenuSkip/
       manifest.json
       client/
         OblivionMenuSkip.Client.dll
   ```

3. Restart the server. Players must fully close Oblivion and reconnect through ReadyM so the launcher downloads the mod.

This is client-side behavior distributed by the server's mod loader. No server DLL, ESP, PAK, changes to the game's Data folder, or extra UI mod are required. ReadyM supplies UE4SS and its C# host. This is not a standalone vanilla Steam mod or a Lua script.

Do not enable this alongside another mod that automatically loads a starter from the main menu: both could issue a load. Remove this mod folder to uninstall, then restart the server and reconnect clients.

## Behavior and limits

- Waits for a requested ReadyM connection with an assigned player identity, a visible native main menu, and normal menu buttons.
- Validates that a nonempty `world.sav` exists beside the host's `dlls` directory. It never creates, edits, copies, or bundles that save.
- Hides the menu buttons, fades out over 0.35 seconds, and invokes the native starter-load function once per mod assembly lifetime. A failed/uncertain dispatch is not retried automatically.
- Stops after ReadyM reports a restored player in a multiplayer area. It does not run again on fast travel or cell changes.
- Restores owned menu buttons and camera fade when loading completes, the mod unloads, or an error occurs. A dispatched load times out after two minutes.
- Uses native game loading after dispatch; it does not add a custom loading screen, spinner, or character preview.

The code uses version-sensitive ReadyM internals and UE4SS native exports. It targets the locally inspected OBMP 0.2 host and Unreal function contracts. Build checks validate the source and contracts; **the extracted standalone release still needs an in-game test**. Other game/ReadyM/UE4SS versions may require updates. Launching the unmodded game from Steam is unaffected.

## Build from source

Requires Windows x64, the .NET 10 SDK, and an existing ReadyM client installation. Launch/connect through ReadyM at least once to obtain the host assemblies.

From this repository:

```powershell
./BUILD.ps1
```

The default reference directory is `%APPDATA%/ReadyM.Launcher/Oblivion/Mods/OblivionMp/dlls`. For another installation:

```powershell
./BUILD.ps1 -SdkDirectory 'D:/ReadyM/Oblivion/Mods/OblivionMp/dlls'
```

The build references ReadyM's assemblies without redistributing them. NuGet restores the logging compile-time dependency. Output:

- `dist/OblivionMenuSkip-0.1.0.zip`: server-distributed release.
- `dist/OblivionMenuSkip-0.1.0-source.zip`: source suitable for uploading to a GitHub repository.

The source archive uses an explicit file list. It excludes build intermediates, host DLLs, player saves, logs, local installation paths, and other mods. Upload the source archive's contents to the repository and attach the release ZIP to a GitHub release. No repository is created or published by the build script.

## In-game check

Test with this as the only automatic menu-loading mod. Connect through ReadyM and leave the keyboard/mouse untouched. Confirm the menu loads into normal server gameplay, then fast travel and confirm no second starter load occurs. Check the client UE4SS log for `[MenuSkip]` messages. Also verify that a missing starter produces an error and leaves the normal menu usable.

## License

MIT; see `LICENSE`. The license covers this source, not ReadyM, UE4SS, or game files, which are not included.
