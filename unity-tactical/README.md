# Down Range Tactical Resolver

Offline Unity 6000.2 tactical resolution client for Down Range Campaign Command.

The campaign tracker writes a versioned `battle-request.json`, launches the Windows player with `--battle-request <absolute-path>`, and imports the resulting `battle-result.json`. The Unity client also writes `battle-state.json` beside the request after every material action.

## Editor

Open this directory with Unity `6000.2.12f1`. Enter Play Mode to use the bundled sample request. The sample deliberately omits a map path; battles launched from Campaign Command receive a copied map asset.

## Build

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.2.12f1\Editor\Unity.exe' -batchmode -quit -projectPath . -executeMethod DownRange.Editor.BuildGame.PerformBuild -logFile -
```

The player is generated at `Build/DownRangeTactical.exe`. Windows Mono is used for the development build, so IL2CPP and MSVC are not required.

New battles default to Tabletop Teaching Mode. The right panel provides progressive drills, physical-dice input, full mouse-over procedures, and actions for Fan/Radius weapons, Assist, command/EW, vehicles, passengers, and environmental effects. Every resolution is retained in Rules Trace with a page-targeted link to the packaged Rules or Armored Addendum PDF.
