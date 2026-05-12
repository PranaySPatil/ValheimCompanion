# Valhein — Valheim Mod Project

A BepInEx + Jotunn mod for Valheim. Stub package: `JotunnModStub`. Plugin GUID `com.jotunn.jotunnmodstub`. Targets .NET Framework 4.8, C# 10.

The folder is named "Valhein" (Unreal projects directory) but the project is a Valheim C# mod, not Unreal.

## Critical: deploy path

This machine uses **r2modman** for runtime. The Steam install's `BepInEx\plugins\` is **ignored** when Valheim is launched via r2modman. The mod must land in the r2modman profile folder.

Paths are baked into `Environment.props` at the solution root (gitignored):

- `VALHEIM_INSTALL` → `C:\Program Files (x86)\Steam\steamapps\common\Valheim`
- `MOD_DEPLOYPATH` → `C:\Users\Pranay\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\plugins`

Environment variables of the same name override `Environment.props` if set.

To launch the modded game: open **r2modman → "Start modded"**. Launching from Steam runs vanilla and the mod won't load.

## Build pipeline

Build = `Ctrl+Shift+B` in VS, or `MSBuild.exe JotunnModStub.sln -t:Build`:

1. Jotunn's prebuild task generates `<Valheim>\valheim_Data\Managed\publicized_assemblies\*.dll`. These expose Valheim's internal types so the project can compile against `Player`, `Console`, etc. Runs only when `DoPrebuild.props` has `ExecutePrebuild=true`.
2. `JotunnModStub.dll` compiles to `JotunnModStub\bin\Debug\`.
3. `publish.ps1` post-build copies `.dll` + `.pdb` + `.dll.mdb` into `$(MOD_DEPLOYPATH)\JotunnModStub\`.

If `Environment.props` is missing or its paths are wrong, both steps may silently no-op (build succeeds, mod never loads).

## Project layout

- `JotunnModStub\JotunnModStub.cs` — plugin entry (`Awake()`) + custom `ConsoleCommand` classes
- `JotunnModStub\JotunnModStub.csproj` — Jotunn NuGet reference; post-build at `JotunnModStub.csproj:110-113`; F5 launches `valheim.exe -console` at `JotunnModStub.csproj:42-47`; `CopyToUnity` target at `JotunnModStub.csproj:83-108` (only runs if `JotunnModUnity\` folder exists — 3 legacy Xbox/PlayFab DLLs there are conditional because they no longer ship with Valheim)
- `JotunnModStub\Properties\IgnoreAccessModifiers.cs` — opens access to game internals
- `JotunnModStub\Package\manifest.json` — Thunderstore manifest (Release packaging only)
- `publish.ps1` — post-build deploy script; reads `MOD_DEPLOYPATH`, falls back to `<VALHEIM_INSTALL>\BepInEx\plugins`
- `DoPrebuild.props` — toggles Jotunn's publicized-assembly generation. Must be `true` on first build per machine; can flip back to `false` afterward for faster incremental builds.
- `Environment.props` — per-machine paths. Gitignored. Auto-imported by `packages\JotunnLib.*\build\Paths.props`.

## Version note (intentional mismatch)

- NuGet `JotunnLib` reference: **2.20.3** (compile-time)
- r2modman runtime Jotunn: **2.29.0**

`[BepInDependency(Jotunn.Main.ModGuid)]` doesn't enforce a version match. Stable APIs (`CommandManager`, `ConfigManager`, basic prefab cloning) work across this gap. If a runtime `MethodNotFoundException` or `TypeLoadException` shows up, upgrade the NuGet package to match the runtime version.

## Dev gotchas

- **Console commands collide silently.** Valheim's `devcommands` set includes `heal`, `god`, `pos`, `tod`, etc. Jotunn refuses to register a duplicate without throwing — the mod loads but the command is unreachable. Check `help` in-game or pick a uniquely-named command.
- **Mods don't hot-reload.** Fully quit Valheim between iterations.
- **"0 succeeded, 1 up-to-date" is normal.** MSBuild's incremental-build output. The previous build is current and already deployed; no recompile needed.
- **First build per machine is slow.** Publicizing ~10 Valheim DLLs takes 30–60s. Subsequent builds skip when files are current or `DoPrebuild.props` is `false`.
- **Don't deploy to Steam's BepInEx folder.** It's not where r2modman launches read from. Sanity-check the deployed DLL is under `MOD_DEPLOYPATH\JotunnModStub\`, not `<VALHEIM_INSTALL>\BepInEx\plugins\JotunnModStub\`.

## Logs

`<r2modman profile>\BepInEx\LogOutput.log` is the source of truth when launched via r2modman. Look for `[Info: JotunnModStub] ModStub has landed` to confirm the plugin loaded. Absence = dependency missing or DLL deployed to wrong folder.
