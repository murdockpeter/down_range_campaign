# Down Range Campaign Command

An offline Electron campaign layer for **Down Range v1.4.2**. It keeps operational bookkeeping outside the tabletop rules: every tabletop battle is one tactical action whose results update the next campaign turn.

## Included

- Named-area campaign map with friendly, contested, enemy, and unknown control
- Persistent task-force strength, readiness, critical ammunition, assets, and status
- Lightweight logistics and KIA / WIA-L / WIA-S / RTD casualty tracking
- Intelligence holdings with source grading, confidence, and age
- Mission package with commander's intent, METT-TC, OCOKA, objectives, force allocation, and synchronization matrix
- Briefing-only Electron tactical tab that hands the authoritative battle to Unity
- Persistent tactical saves and a mission-end handoff that updates objectives, casualties, force strength, and the AAR
- Standalone offline Unity tactical resolver with deterministic node-specific terrain, a one-inch logical terrain grid, smoothed shared-mesh edges, automatic eye-height terrain LOS, a versioned JSON request/result bridge, and resumable per-battle saves
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

Open **Tactical battle** in the left rail for the mission briefing, then choose **Open in Unity** to play. Campaign Command writes a terrain-aware `battle-request.json` in a private per-battle exchange directory and launches the offline player. Unity creates the target node as a deterministic one-inch terrain grid and spawns the imported, painted 3D miniatures appropriate to each unit's role. Select a miniature or roster entry, click terrain within its allowance to move, and select an opposing miniature to target. Movement persists the figure's new facing.

The scrollable **Unit Actions** panel keeps every tactical choice visible: Move, Fire, Suppress, Hold Reaction, Sprint, Radio Fires Observation, Treat Casualty, and Observe Relay. Each button includes an immediate summary and READY/UNAVAILABLE state. Mouse-over help appears both in the panel and beside the pointer with the action cost, requirements, full effect, and any current blocking reason; clicking an unavailable action also reports that reason in the status area. Focus is shown as the whole-turn cost of actions that require it, rather than as a standalone bonus.

Every tactical action has a distinct, procedurally generated offline sound cue: move preparation and movement, firing, suppression, held reactions, sprinting, radio observation, focused medical care, relay observation, LOS measurement, turn changes, impacts, mission completion, and unavailable-action feedback. The **Sound/Muted** control persists its setting locally.

The Unity header includes a persistent **Rules Trace** tab (`F2`). Every accepted or rejected command records its inputs, rules modifiers, path or LOS classifications, dice, thresholds, arithmetic, interrupt ordering, and final ruling. Entries survive battle resume, can be read newest-first or chronologically, can be copied to the clipboard, and are included in the exported tactical result.

Mission-zone overlays are hidden by default. Use **Show mission zones** in Unit Control to reveal or hide extraction and other scenario boundaries without changing their scoring.

Unity automatically traces line of sight from a figure's eye or sensor height. Terrain rises, buildings, solid structures, trunks, and actual intervening miniature silhouettes block the line; foliage applies partial concealment. The measured line and selected-target line report the classification and first blocker, and firing rechecks the result immediately before adjudication.

Movement is adjudicated along the complete declared path rather than by destination distance alone. Normal segments cost their measured length; mud, water, dense woods, steep slopes, crawling, and applicable off-road travel cost double, while buildings and terrain impassable to the selected unit reject the move. Saved Reactions pause a declared enemy action before resolution and offer Fire, Sprint, or Pass; the pending action is then revalidated or canceled. Mission objectives carry their own action, zone, LOS, target, Difficulty, continuity, and progress definitions instead of relying on relay-specific objective IDs. Reconnaissance contracts can also define an extraction edge, concealment-based detection distance and Difficulty, an alarm objective, and a hard turn deadline; Unity returns the alarm reason and deadline status to the tracker.

Generated woodland uses deterministic mixtures of broadleaf trees, tiered conifers, birch/aspen, narrow young trees, dead snags, and undergrowth. Light woods are open and provide partial foliage concealment; heavy woods form darker clustered groves whose dense canopy and brush can completely block sight.

Roads and rails are terrain-conforming shared meshes rather than rigid boxes. They are subdivided at one-inch intervals and sample the exact smoothed ground triangles, eliminating long floating sections and terrain-cutting slabs on slopes.

Unity autosaves `battle-state.json` after every material action and exports `battle-result.json` when the mission ends. Campaign Command watches for that result and automatically applies objective scoring, final unit positions and facings, force losses, casualties, and the tactical log exactly once.

After Mission #1 is adjudicated, the **After action** view offers **Begin Mission #2**. Mission #2, **Ghost Frequency**, is a close-collection operation at the generated Grebņeva Relay compound with its own mission package, MCPP workspace, intelligence update, deployment, and Unity tactical battle. Existing campaign momentum, history, forces, supplies, and casualties carry forward; casualties due to return that turn are restored to their parent unit.

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
