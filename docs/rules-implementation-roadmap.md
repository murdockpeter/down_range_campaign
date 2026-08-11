# Down Range complete rules implementation roadmap

Status: living engineering plan  
Authority: Down Range Rules v1.4.2 and the optional Armored Addendum supplied in `docs/official/DownRangeLatest`  
Scope: the standard campaign Unity tactical resolver; One Star should consume the same rules engine after the engine is separated from its scenario UI

## Objective

Deliver an offline Unity implementation that:

1. adjudicates every generally applicable rule in Rules v1.4.2;
2. supports every optional Armored Addendum module as an explicit scenario option;
3. teaches the corresponding physical tabletop procedure;
4. records a deterministic, replayable Rules Trace with the exact source document, section, and PDF page;
5. returns authoritative tactical facts to Campaign Command without putting campaign-level adjudication inside Unity.

“Implemented” means the rule is data-driven, available through the UI, persistent across save/resume, traced and cited, covered by automated tests, exercised by a tutorial or validation scenario, and free of invented adjudication.

## Source precedence

1. `Rules Compressed-278da66fbe36c91eae0252e2830de80b.pdf`
2. `Armored Addendum Compressed-617a27e4519c9747b41005d25c9c39b4.pdf` when that optional module is enabled
3. Unit Cards for unit/equipment data, not for overriding the full rules
4. Quick Start only as a teaching summary; it does not override the full rules
5. Scenario-specific rules after the core rules, and only for that scenario

Lemuria and One Star provide content and scenario rules. Their content should enter through data packages rather than branches in the core resolver.

## Status vocabulary

- **Verified**: faithful, persistent, cited, and tested.
- **Partial**: playable code exists, but at least one rule choice, timing case, data case, citation, UI path, or test is missing.
- **Absent**: no authoritative gameplay path exists.
- **Provisional**: current behavior includes a digital house rule and must not be presented as an exact implementation.

## Current baseline

| Area | Status | Current capability | Principal remainder |
| --- | --- | --- | --- |
| Dice and basic tests | Partial | Deterministic dice, Advantage/Disadvantage cancellation, queued physical results | Apply automatic failure and manual-dice prompting to every test; represent full dice pools and opposed/static tests consistently |
| Rounds, turns, actions | Partial | Initiative, side turns, Focus costs, saved Reactions, action validation | General action/timing engine; legal Reaction types; marker expiry; interruption stack; simultaneous/end-turn effects |
| Movement | Partial | Measured paths, impaired segments, terrain costs, Sprint, facing | Vertical movement, falls/crashes, transport interaction, formations/base clearance, scenario movement effects |
| Visibility | Partial | Eye-height physics LOS, terrain/foliage/unit blockers, smoke blocking | Sensor types, darkness, night vision, white light, observation inheritance, exposed crew/passenger visibility |
| Direct attacks | Partial | Range, LOS, Skill, cover, Damage, static/dice Defense, suppression | Complete modifier registry, training, weapon Focus, armor targeting edge cases, all automatic failures, attack declarations as resumable commands |
| Fan weapons | Provisional | Automatic additional targets in a 45-degree cone | Player selects targets and distributes repeated attacks; show cone preview; enforce allocation before rolling |
| Explosives | Provisional | Radius damage, cover from blast center, smoke/illumination effects, d8 miss scatter | Point targeting, official post-miss impact placement by the entitled player, miss-radius UI, artillery/mortar procedure, effect variants and timing |
| Limited ammunition | Partial | Numeric ammunition decrement | Rulebook marker semantics, reload/resupply scenario actions, per-weapon expenditure timing, UI warnings |
| Assistance and crew-served weapons | Partial | Focused Assist and static modifier | Plausibility/range validation, declared assisted test, assistant lifecycle, weapon crew requirements and casualty effects |
| Medicine | Mostly verified | Focus, adjacency, medical Skill table, persistence and trace | Universal automatic-failure handling; training/equipment data validation; additional timing tests |
| Environmental damage | Partial | Persistent hazard roll and extinguish action | Falls, crashes, damaging-effect dice definitions, ignition/spread, flammability, fire duration, vehicle/structure interactions |
| Equipment | Absent/partial | Radios and some mobility traits exist | Night vision, body armor, white light, specialist/training tags, equipment actions and emissions |
| Basic vehicles | Partial | Movement traits, weapons, passengers, remote flag | Primary/secondary crew, operators, one-use-per-weapon timing, linked weapons, exposed/enclosed passengers, overload, bailout, destruction, amphibious/flying edge cases |
| Command and signals | Partial | Enhanced initiative, Main Effort prototype, radio observation, emissions | Voice chains, situational-awareness relay, command hierarchy, Main Effort propagation and every-test effect, degraded-control restrictions |
| Electronic warfare | Provisional | Combined SIGINT/jam action and remote disruption | Separate official actions, ranges/radii, system Difficulty, Reaction use, emission detection/share, jam timing/removal, autonomous exceptions |
| Unmanned vehicles | Partial | Operator link and operator Skill for armed attacks | Signal legality each turn, platform failure states, crash resolution, autonomous behavior and electronic-attack distinctions |
| Advanced armor | Partial | Three systems, holistic steps, selected-system attacks, simple repair | Exact status effects, player damage allocation, crew targeting, recovery/towing, repairs/assists, destruction/crew results |
| Structures and obstacles | Absent | Terrain blocks movement/LOS | Structure Defense/damage, floors/collapse, rubble, clearing, cope cages, mines, mobility obstacles and breaching |
| Teaching and trace | Partial | Action hover help, six drills, persistent trace, PDF page opening | Rule-ID registry, exact citations, contextual roll prompts, marker illustrations, interactive tutorials, trace replay and coverage audit |
| Campaign bridge | Partial | Additive request/result contract and idempotent result import | Version 2 schema for equipment/effects/vehicles, result export for ammunition/systems/passengers/effects, migration and compatibility fixtures |

## Architectural work before adding more rule branches

The current `BattleRuntime` owns UI, command validation, dice, resolution, state mutation, teaching text, tracing, and persistence. Completing the rules directly in that class would make timing and modifier bugs increasingly likely. The next implementation phase should separate these responsibilities without changing gameplay.

### Target modules

- `RulesEngine`: accepts a command and immutable battle snapshot; returns required choices/rolls and rule events.
- `CommandCatalog`: legal actions, costs, target schemas, prerequisites, Reaction eligibility, and tabletop instructions.
- `ModifierResolver`: named Advantage, Disadvantage, and static modifiers with cancellation and trace output.
- `DiceService`: deterministic or physical dice; supports pause/resume for required rolls and automatic-failure semantics.
- `TimingEngine`: declaration, Reaction window, resolution, consequence, duration expiry, and end-turn checkpoints.
- `EffectEngine`: suppression, smoke, illumination, fire, emissions, jams, Main Effort, hazards, and scenario effects.
- `SpatialRules`: measurement, paths, blast geometry, cones, base contact, terrain, visibility, sensors, and vertical distance.
- `RuleSourceRegistry`: stable rule ID to document, section, PDF page, teaching text, and optional module.
- `BattlePersistence`: versioned command/event snapshots and migrations.
- `BattleRuntime`: presentation and input only; it renders engine-provided choices and events.

### Core command lifecycle

Every action should follow the same state machine:

1. `Declare` actor, action, targets, weapon/equipment, point/area, and optional mode.
2. `Validate` cost, timing, training, range, LOS, communication, ammunition, and scenario permissions.
3. `OfferReactions` in the correct order without mutating the declared action.
4. `CollectChoices` such as Fan allocation, blast placement, damage allocation, passengers bailing out, or targeted system.
5. `CollectRolls` from deterministic or physical dice.
6. `Resolve` into typed rule events.
7. `Apply` events atomically to battle state.
8. `Trace` every source, modifier, roll, comparison, choice, and duration.
9. `Persist` the command, events, resulting state, and RNG position.

## Milestone plan

### M0 — Rules manifest and truth audit

Goal: establish a machine-readable definition of completeness before further mechanics.

- [ ] Create stable IDs for every applicable section of Rules 2.1 through 5.2 and Armored Addendum 2 through 4.
- [ ] Record document filename, printed section, PDF page, optional-module flag, and implementation status for each ID.
- [ ] Correct known citation mappings: EW is Rules 5.1.2; Command is Rules 5.2; vehicle weapons are Rules 4.2; passengers are Rules 4.3; explosive misses are Rules 3.2.4.1; advanced vehicle damage is Armored Addendum 2.2; and repair is Armored Addendum 2.4.
- [ ] Mark scenario-only adjudications distinctly from universal rules.
- [ ] Mark current automatic Fan allocation and d8 explosive scatter as provisional in the UI and trace until replaced.
- [ ] Add a validation test that fails when an action or trace category lacks a registered source.
- [ ] Update `docs/down-range-rules-audit.md` from the manifest rather than maintaining a second manual list.

Exit gate: one generated coverage report lists every rule as verified, partial, absent, content-only, or intentionally out of scope.

### M1 — Extract the deterministic rules engine

Goal: create a testable rules core before expanding behavior.

- [ ] Introduce immutable command DTOs and typed rule-event DTOs.
- [ ] Move dice/test logic out of `BattleRuntime`.
- [ ] Move direct attack, medicine, movement allowance, and detection into command resolvers.
- [ ] Add a generic pending-choice/pending-roll state that survives save/resume.
- [ ] Make state changes atomic after all Reactions and rolls resolve.
- [ ] Preserve compatibility with existing contract-version-1 battle saves.
- [ ] Build edit-mode tests that run without rendering a scene.

Exit gate: current Mission 1 and Mission 2 golden replays produce identical results before and after extraction.

### M2 — Universal dice, modifiers, timing, and markers

Rules: 2.1–2.2, PDF pages 5–6.

- [ ] Implement one `TestDefinition` for static Difficulty, opposed Defense, Damage, medicine, EW, command, repair, recovery, and scenario tests.
- [ ] Enforce automatic failure on every applicable test after Advantage/Disadvantage selects the kept natural die.
- [ ] Replace the free-form physical-dice queue with contextual prompts naming die type, purpose, and kept-die rule.
- [ ] Centralize Advantage/Disadvantage sources and cancellation.
- [ ] Centralize static modifiers and prohibit accidental modification of die size.
- [ ] Model Focus, Reaction, suppression, emission, Main Effort, limited ammo, and other tabletop markers as typed effects with owner/timing.
- [ ] Implement exact start-turn/end-turn/start-next-turn expiry checkpoints.
- [ ] Generalize Reaction legality so newly supported reaction-capable actions require no custom prompt code.

Exit gate: table-driven tests cover every combination of normal, Advantage, Disadvantage, cancellation, modifier, automatic failure, physical dice, and save/resume mid-roll.

### M3 — Spatial rules, movement, visibility, and equipment

Rules: 2.3–2.5 and 3.3, PDF pages 6–9 and 21.

- [ ] Define bases, contact distance, unit height, sensor height, and vertical positions explicitly.
- [ ] Complete normal, impaired, Sprint, road-bound, tracked, amphibious, flying, climbing, and impassable movement profiles.
- [ ] Add fall/crash triggers to vertical/flying movement.
- [ ] Separate cover from concealment in the LOS result rather than storing only open/partial/blocked.
- [ ] Implement darkness and lighting state.
- [ ] Implement night vision, IR illumination, white light, body armor, radio, and specialist/training equipment tags.
- [ ] Implement sensor-specific visibility and attacks against white-light emitters.
- [ ] Add 3D previews for movement cost, base contact, LOS height, concealment, and invalid destinations.

Exit gate: a spatial fixture scene exercises every terrain/mobility/sensor combination at one-inch boundaries.

### M4 — Complete attack and weapon framework

Rules: 2.6 and 3.1–3.3, PDF pages 9–17.

- [ ] Implement declarative weapon profiles: range, Difficulty, Damage dice/modifier, Fan, Radius, ammunition, Focus, crew, anti-air, indirect, variants, and linked grouping.
- [ ] Implement the complete official attack Advantage and Disadvantage lists.
- [ ] Enforce training and weapon-specific Focus requirements.
- [ ] Enforce anti-air and flying-target rules without allowing unrelated Advantage to erase mandatory Disadvantage.
- [ ] Complete weapons-versus-armor die restrictions and targeted visible-system exception.
- [ ] Let the player select Fan targets and allocate all repeated attacks before rolling.
- [ ] Preview the 45-degree Fan cone and reject targets outside it.
- [ ] Resolve Fan suppression against up to the allowed number of selected units.
- [ ] Track each weapon’s once-per-turn use and limited-ammunition marker independently.
- [ ] Complete crew-served primary/assistant declarations, Focus, stationary requirements, casualties, and static modifiers.

Exit gate: reproduce the rulebook’s ZBL-09 automatic-fire example as a golden event/trace test.

### M5 — Explosives, indirect fire, and environmental effects

Rules: 2.9 and 3.2.4–3.2.5, PDF pages 13–14 and 17–21.

- [ ] Permit attacks against a board point rather than requiring a unit target.
- [ ] On a miss, calculate `Radius / 2 × amount missed` and open an impact-placement choice for the player entitled by the rules.
- [ ] Show intended point, legal miss circle, candidate targets, cover rays, and final blast radius before confirmation.
- [ ] Use one Skill roll and a separate Damage roll for every affected unit.
- [ ] Determine complete/partial cover independently from the blast center for each target.
- [ ] Implement damaging explosives, smoke, visible illumination, IR illumination, artillery, and mortars.
- [ ] Implement smoke/illumination creation and expiry exactly at the creator’s next-turn checkpoint.
- [ ] Implement falls and crashes by distance and Defense die type.
- [ ] Implement persistent damaging effects once per turn.
- [ ] Implement fire ignition, spread to flammable objects, ongoing damage, and Focus to extinguish.

Exit gate: deterministic blast tests cover hit, every miss margin, edge clipping, friendly fire, complete cover, partial cover, smoke, illumination, and save/resume during impact placement.

### M6 — Basic vehicles, crews, passengers, and unmanned systems

Rules: 4.1–4.4, PDF pages 22–23.

- [ ] Represent primary crew, secondary operators, controlled weapons/equipment, and crew casualties.
- [ ] Allow the primary operator to move and operate one controlled weapon/equipment item as specified.
- [ ] Track each vehicle weapon’s once-per-turn use.
- [ ] Implement legal same-type linked-weapon attacks against one target.
- [ ] Complete standard and nonstandard embark/disembark procedures.
- [ ] Implement enclosed, exposed, and completely exposed passenger/crew targeting.
- [ ] Implement overloaded capacity, half Move, and required Disadvantage.
- [ ] Implement vehicle destruction and pre-declaration bailout Reactions.
- [ ] Implement remote operator Focus/Signal, operator Skill for armed RPV attacks, autonomous behavior, jam failure, and flying-platform crashes.

Exit gate: a transport tutorial covers loading, overload, movement, exposed fire, bailout, destruction, remote control, jamming, and crash resolution.

### M7 — Command, signal, situational awareness, and EW

Rules: 5.1–5.2, PDF pages 25–26.

- [ ] Model automatic voice chains using each unit’s Move distance.
- [ ] Model radio networks, Signal actions, emission markers, and exact expiry.
- [ ] Implement situational-awareness sharing: radio and voice-chain units can target what the signaler sees, subject to weapon plausibility.
- [ ] Complete fires observation duration and target modifiers.
- [ ] Split SIGINT, radio jamming, and unmanned-system jamming into their official actions.
- [ ] Implement EW Radius, system Difficulty, Reaction timing, optional free sharing Signal, failure emissions, and jam restrictions.
- [ ] Implement enhanced initiative as an explicit commander action and next-initiative effect.
- [ ] Implement Main Effort eligibility, radio/voice delivery, subordinate-command propagation, and Advantage on every eligible Skill test.
- [ ] Apply degraded/disabled Control restrictions to communication, Main Effort, EW, attacks, and all actions exactly.

Exit gate: a command/EW tutorial demonstrates voice-chain propagation, radio extension, SIGINT, both jam types, Main Effort delegation, and enhanced initiative with complete trace citations.

### M8 — Advanced Armor module

Armored Addendum: chapters 2–4, PDF pages 3–10.

- [ ] Add scenario toggles for vehicle systems, structures, cope cages, mines, and mobility obstacles independently.
- [ ] Apply every Operational/Degraded/Disabled Mobility, Firepower, and Control effect from Table 2-1.
- [ ] Implement holistic damage-step calculation and player allocation.
- [ ] Implement targeted-system attacks, visibility, Disadvantage, armor restriction waiver, and system-only allocation.
- [ ] Implement attacks against exposed crew.
- [ ] Implement vehicle recovery, towing, and improvised recovery.
- [ ] Complete mechanic repair and assisting mechanics.
- [ ] Implement structure material Defense, floors, collapse, occupant damage, and rubble creation.
- [ ] Implement rubble clearing.
- [ ] Implement cope cages.
- [ ] Implement mines, minefields, mobility obstacles, avoidance, breaching, and clearing.

Exit gate: each optional module can be enabled alone, survives save/result export, and has a focused automated/tutorial scenario.

### M9 — Data, cards, scenarios, and campaign contract v2

Goal: remove hard-coded role inference and allow the complete rules to be authored safely.

- [ ] Define JSON schemas for units, dice, weapons, equipment, crew stations, passengers, command hierarchy, effects, structures, obstacles, terrain rules, and scenario options.
- [ ] Import/author the supplied Unit Cards as reviewed data with provenance.
- [ ] Add schema validation with actionable errors before Unity launches.
- [ ] Replace role-name heuristics with explicit capabilities while retaining a migration layer.
- [ ] Version the bridge to contract v2 and retain a v1 reader.
- [ ] Export ammunition, vehicle systems, crew/passengers, equipment loss, structures, effects, and scenario facts needed by the campaign.
- [ ] Keep campaign-only consequences in `applyBattleResult`, driven by exported facts.
- [ ] Add fixture contracts for infantry, automatic weapons, explosives, vehicles, EW, structures, and One Star.

Exit gate: every supplied card used by a scenario validates against the schema, and v1/v2 migration tests pass.

### M10 — Rules teaching product and full regression suite

- [ ] Replace generic hover prose with rule-ID-driven tabletop steps and marker instructions.
- [ ] Show “why available/unavailable” from the same validation result used by the engine.
- [ ] Prompt physical dice at the exact moment and identify which miniature/card statistic supplies each die.
- [ ] Add clickable citations for every computation, including the correct Rules or Armored Addendum document.
- [ ] Add trace filters by unit, action, rule, roll, and round.
- [ ] Add command replay from the initial state and detect divergence.
- [ ] Build progressive tutorials: movement/LOS, direct fire, cover/modifiers, Reactions/suppression, specialists/medicine, Fan, explosives, command/EW, transport/unmanned, armor, structures/obstacles, and combined arms.
- [ ] Recreate the complete Rules chapter 6 example as an automated playable tutorial.
- [ ] Add a tabletop mode that hides computed outcomes until physical results are entered.
- [ ] Add a certification checklist showing which rule families the player has practiced, stored separately from campaign state.

Exit gate: every implemented rule ID has at least one automated test, one trace fixture, and either an interactive tutorial step or an explicit content-only designation.

## Cross-cutting engineering backlog

- [ ] Replace string states (`"downed"`, `"partial"`, `"disabled"`) with serialized enums plus migration.
- [ ] Replace percentage-only positions with board-inch coordinates internally; convert percentages only at the contract/UI boundary.
- [ ] Give effects stable IDs, source unit, start/end timing, stacking policy, and removal reason.
- [ ] Separate rule state from display preferences such as mission-zone visibility and Teaching Mode.
- [ ] Add save schema version and migrations independent of the request/result contract version.
- [ ] Add deterministic command/event replay and RNG-consumption assertions.
- [ ] Add undo only for uncommitted declarations/choices, never after hidden information or dice are resolved.
- [ ] Add structured error codes; UI text should not be the engine API.
- [ ] Add localization-safe teaching strings after rule behavior stabilizes.
- [ ] Keep procedural terrain colliders and visual masks generated from the same semantic map data.
- [ ] Profile large battles and pool UI/marker/effect objects before increasing force size.

## Test strategy

### Unit tests

Use table-driven tests for dice, modifiers, timing, geometry, movement costs, weapon eligibility, damage, status transitions, communication chains, EW, and every optional armor rule.

### Golden rule examples

Store command sequences and expected events/traces for:

- Rules chapter 6 full example;
- Quick Start examples where they do not conflict with the full rules;
- one fixture per Fan, Radius, medicine, vehicle, command, EW, structure, mine, and environmental edge case;
- One Star rule interactions actually used by that scenario.

### Contract tests

Round-trip v1 and v2 JSON between JavaScript and C#. Verify additive defaults, migrations, invalid-data rejection, reset behavior, idempotent result import, and tactical facts mapped into campaign state exactly once.

### Scene and executable tests

- Headless Unity validation for rules and resources.
- Physics scenes for LOS, cover, concealment, smoke, heights, structures, passengers, and blast cover.
- Automated player smoke launch from an isolated exchange directory.
- Manual visual checklist at tabletop, miniature, and close-camera zoom levels.

### Definition-of-done gate for each rule ID

- [ ] authoritative text and citation reviewed;
- [ ] data model/schema present;
- [ ] legality and timing implemented;
- [ ] all player choices exposed before dice;
- [ ] deterministic and physical dice supported;
- [ ] state persists/resumes at every pending choice;
- [ ] Rules Trace is complete and opens the exact PDF page;
- [ ] unit and edge-case tests pass;
- [ ] teaching text explains the physical procedure;
- [ ] tutorial/golden scenario exercises the rule;
- [ ] campaign result contains any lasting tactical facts.

## Recommended implementation order

Do not begin with another isolated action button. The recommended sequence is:

1. M0 rules manifest and citation correction.
2. M1 engine extraction and resumable command lifecycle.
3. M2 universal dice/timing/effects.
4. M4 Fan allocation and M5 official explosive placement, removing the two provisional house rules first.
5. M3 equipment/visibility.
6. M6 basic vehicles and unmanned systems.
7. M7 command/signal/EW.
8. M8 Advanced Armor.
9. M9 data/contract v2.
10. M10 teaching, replay, tutorials, and certification gates continuously, with final completion after all mechanics stabilize.

## Next coding sprint

The next bounded sprint should establish the foundation and remove the most visible rule divergences:

- [ ] Build `RuleSourceRegistry` and the generated coverage report.
- [ ] Correct EW/Command and Advanced Armor citations.
- [ ] Introduce `BattleCommand`, `RuleEvent`, `PendingChoice`, and `PendingRoll` DTOs.
- [ ] Extract attack resolution and dice from `BattleRuntime` without behavioral changes.
- [ ] Implement contextual physical-dice prompts with save/resume.
- [ ] Replace automatic Fan target selection with a cone-selection/allocation phase.
- [ ] Replace d8 explosive scatter with official legal-impact placement.
- [ ] Add golden tests for both changes and rebuild/smoke-test Unity.

Completing that sprint creates the architecture needed to implement the rest without repeatedly rewriting the UI or timing code.
