# One Star 3D module

The Unity player supports a dedicated `--one-star` launch mode. Campaign Command exposes it through **Tactical battle → One Star 3D** without replacing or modifying a pending campaign battle.

## Implemented foundation

- A 72" × 60" procedural Calloni tabletop using one Unity world unit per tabletop inch.
- Eleven scenario landmarks with data-defined positions, footprints, floors, grid references, discovery flags, and descriptions.
- Grand Calloni Hotel, Neustadt Building, the Barrows, police station, warehouse complex, 8-Twelve, both markets, Shattered Lines, Calloni Clinic, and the LPM safe house.
- Narrative, seize-and-hold, destroy-enemy, and rescue/rendition scenario modes.
- The complete twelve-round narrative event outline and scheduled force arrivals.
- Orthographic miniature-table camera with pan, rotate, and zoom controls.
- Selectable 3D force markers and measured outdoor repositioning.
- Licensed textured OBJ playtest models for USMC infantry roles, drones, PLANMC infantry stand-ins, EQ2050, and ZBL-09 vehicles.
- Physics-based 3D LOS against buildings, rubble, tree trunks, vehicles, and intervening units.
- Optional facilitator view for due hidden contacts.
- Local save/resume of round, selected mode, marker positions, and marker status.

## Controls

- **Left click:** select a visible unit marker.
- **Right click:** move the selected marker within its printed movement allowance.
- **L:** toggle 3D LOS, then click an origin and target.
- **WASD / arrows:** pan the table.
- **Q / E or middle-drag:** rotate the camera.
- **Mouse wheel:** zoom.
- **F1:** toggle the One Star guide.

## Subsequent implementation passes

1. Semantic terrain polygons, cover classes, concealment volumes, and generated-map validation overlays.
2. Enterable building floors, doors, windows, roof cutaways, stairs, and vertical movement.
3. Individual force rosters, weapons, combat actions, casualties, EW, UAS, indirect fire, and vehicles.
4. Location discovery tables, civilians, notable characters, facilitator decisions, and solo opposition behavior.
5. Full victory adjudication and campaign request/result integration.
6. Optimized modular building art and a complete individual-miniature roster replacing remaining procedural placeholders.

## Attribution

One Star, Down Range, and the imported playtest models are © Nicholas Royer. This private, noncommercial adaptation uses the scenario and models under the CC BY-NC-SA 4.0 terms supplied with the collection and published by the creator. The original model license is retained at `unity-tactical/Assets/Resources/Models/OneStar/LICENSE-Down-Range-Models.txt`. Project-specific code and placeholder geometry do not imply endorsement by the creator.
