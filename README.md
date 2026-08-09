# Down Range Campaign Command

An offline Electron campaign layer for **Down Range v1.4.2**. It keeps operational bookkeeping outside the tabletop rules: every tabletop battle is one tactical action whose results update the next campaign turn.

## Included

- Named-area campaign map with friendly, contested, enemy, and unknown control
- Persistent task-force strength, readiness, critical ammunition, assets, and status
- Lightweight logistics and KIA / WIA-L / WIA-S / RTD casualty tracking
- Intelligence holdings with source grading, confidence, and age
- Mission package with commander's intent, METT-TC, OCOKA, objectives, force allocation, and synchronization matrix
- Playable Silent Lantern tactical battle on the supplied 3:2 map, with measured token movement, initiative, actions, reactions, combat, suppression, radio observation, medicine, and mission objectives
- Persistent tactical saves and a mission-end handoff that updates objectives, casualties, force strength, and the AAR
- Standalone offline Unity tactical resolver with a versioned JSON request/result bridge and resumable per-battle saves
- Dedicated One Star 3D preview with a procedural 72" × 60" Calloni tabletop, scenario timeline, force arrivals, tactical camera, measured movement, and physics-based LOS
- AAR adjudication that advances the campaign and adjusts momentum from −3 to +3
- Campaign history plus JSON import/export
- Offline field library containing all 20 PDFs from the supplied DownRange folder
- Google terrain/satellite basemap with draggable campaign nodes and an offline schematic fallback

The opening campaign, **Operation Iron Lantern**, is ready to play in Latvia's Kārsava–Malnava corridor. Mission #1, **Silent Lantern**, is a reconnaissance from the Nūmerne Heights toward the fictional Grebņeva Relay.

A clean 3:2 Tabletop Simulator board texture and playthrough guide are included at `assets/maps/silent-lantern-tts-map-v1.png` and `scenarios/mission-01-silent-lantern-tts.md`.

## Run

```powershell
npm install
npm start
```

Campaign progress autosaves to Electron's per-user application-data folder. The packaged seed remains unchanged and can be restored by deleting that saved state.

Open **Tactical battle** in the left rail to play. Select a unit from the roster or map, drag an active unit to move it, then select an opposing token as the target. The right rail exposes the actions available under the Down Range turn sequence. Because cover and concealment depend on the miniature's eye-level line of sight, choose the applicable target/LOS condition before resolving fire. Play is local hotseat: end the active side's turn to pass control.

The tactical screen also provides **Launch Unity**. Campaign Command copies the selected map into a private per-battle exchange directory, writes `battle-request.json`, and launches the offline player. Unity autosaves `battle-state.json` after every material action and exports `battle-result.json` when the mission ends. Return to Campaign Command and choose **Import result** to apply objectives, force losses, casualties, and the tactical log exactly once.

Choose **One Star 3D** from the same tactical command bar to launch the separate Calloni scenario module. Its current implementation status and controls are documented in `docs/one-star-3d.md`.

Google Maps is optional. Open the key control in the lower-left rail and enter a Maps JavaScript API browser key. The key is protected with Electron `safeStorage`, saved outside the repository, and never included in campaign exports. For browser restrictions, allow `http://127.0.0.1:43118/*`.

## Verify and package

```powershell
npm run check
npm run unity:build
npm run dist
```

## License note

Down Range is © Nicholas Royer and licensed under CC BY-NC-SA 4.0 except where otherwise noted. This is an unofficial, non-commercial campaign companion. Independently authored research and tactics PDFs retain their own terms.
