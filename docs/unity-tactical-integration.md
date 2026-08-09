# Unity tactical integration

Campaign Command and the Unity tactical resolver communicate exclusively through a versioned, offline JSON contract. Neither process requires a network service or direct access to the other's in-memory state.

## Lifecycle

1. The renderer sends the current campaign state to Electron's privileged main process.
2. The main process creates a UUID-named directory below the application's per-user `unity-battles` data directory.
3. It derives the mission target's terrain profile and writes `battle-request.json` using contract version 1.
4. It saves the request ID as the campaign's pending Unity battle and launches `DownRangeTactical.exe --battle-request <path>`.
5. Unity restores a matching `battle-state.json` if present, otherwise it initializes deterministic dice from the request seed.
6. Unity rewrites `battle-state.json` after every material action.
7. Ending the mission writes objective score, outcome, casualties, unit state, and `battle-result.json` in the same directory.
8. Campaign Command polls the exchange while a mission is pending and automatically imports the result into the campaign and AAR.
8. Campaign Command validates the contract and pending request ID, applies the result, saves the campaign, and prepares the AAR.

Imported result IDs are retained in campaign state. Re-importing a result is idempotent and cannot decrement force strength or create casualties twice.

## Contract ownership

The canonical JavaScript serializer and importer are in `src/unity-bridge.cjs`. Matching C# data-transfer objects are in `unity-tactical/Assets/Scripts/TacticalModels.cs`. Additive fields are safe; breaking changes require incrementing `contractVersion` in both implementations.

The request owns:

- campaign and mission identity;
- rules version and deterministic random seed;
- copied map path and physical board dimensions;
- scenario objectives;
- unit deployment, statistics, equipment, and campaign force IDs.

The result owns:

- final round, alarm, and observation state;
- final unit positions and health states;
- objective completion;
- categorized BLUE casualties;
- the ordered tactical event log.

Campaign effects remain the campaign tracker's responsibility. Unity reports tactical facts; the tracker updates persistent force strength, casualty ledgers, objective scores, momentum, and history.

## Terrain grid

Every campaign target node maps to a deterministic terrain archetype. The tactical board uses a one-inch logical grid (`gridCellSize: 1`) and renders that data as a shared Unity mesh rather than separate blocks. Neighbor height samples are smoothed before mesh creation, and vertex colors interpolate across contiguous terrain boundaries. Optional `cells` entries can override an individual cell's `type` and `elevation`, providing the contract needed for a later visual map-mask editor without changing the runtime renderer.

Current archetypes cover wooded ridge, relay compound, farmland, small town, railhead, highway junction, dam crossing, and forward base terrain.

## Extension path

Future contract-compatible modules can add weapon ammunition, blast points, vehicles and armor dice, embarked units, signals/EW effects, indirect fires, terrain polygons, scenario triggers, and AI orders. These should remain scenario data wherever possible, while core adjudication stays in the deterministic C# rules layer.

## External 3D asset reference

- Down Range creator collections: https://www.printables.com/@nroyer/collections
- Intended future use: evaluate the available printable objects as source material for Unity terrain, vehicles, and battlefield props.
- Before bundling an object, verify its individual license and attribution requirements, then convert and optimize the print-oriented mesh for real-time rendering, materials, scale, and collision.
