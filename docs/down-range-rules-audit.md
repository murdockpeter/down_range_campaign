# Down Range rules authority and implementation audit

## Authoritative source

The sole official input for this project is the six-file collection supplied in:

`C:\Users\Peter G. Robbins\Desktop\ToDo_New\RulesAndSuch\DownRange\DownRangeLatest`

The packaged copies under `docs/official/DownRangeLatest` were verified by SHA-256:

| Document | SHA-256 |
| --- | --- |
| Rules | `0C116CDE48A35CE46F90F074DDDCCC32A201A978FA7860E35AAAFC1E97BB05A8` |
| Quick Start | `4AEB5980BC4BD01EADD6C750E6560935FDC4FA6D78AD5E0501F2ADA104FCDF98` |
| One Star | `3AC7E0F4ED475B04555B7265D8B291F64C3ED11BA298CA3A9AC06D443235573F` |
| Armored Addendum | `F3691FB9DDD97D869BB8A0A3A98AC339339A4E8E1E5B7087322F453A6B0C2241` |
| Lemuria Sourcebook | `C9AB185B1DB04EAE8CBE978249385C4587A006F9AFD98B24207193027594D455` |
| Unit Cards | `3082CF9E0E473F0CB8F9BCD4EF58A49019F40BECDF9AE435821FAE6288C021F3` |

Older rulebooks, cards, sourcebooks, research documents, tactics documents, and the unrelated Unity of Command II manual were removed from `docs/official`. Existing campaign saves are migrated to the six-document catalog.

## Adjudication precedence

The full 33-page **Rules v1.4.2** governs tactical adjudication. The Quick Start is a summary aid. The supplied Quick Start medical table conflicts with Rules Table 2-2: Quick Start lists 1–3 / 4–5 / 6–8 / 9–10, while the full Rules table lists 1–2 / 3–4 / 5–7 / 8+. The resolver follows the full Rules table.

## Corrected in this pass

- Advantages and disadvantages now cancel completely whenever at least one of each applies (Rules 2.1.5), regardless of how many sources exist on either side.
- Sprint now grants troop units a second distinct Move rather than one oversized movement segment. A saved Reaction can be converted into one normal Move (Rules 2.3.1.1 and the example in 6.5.2).
- Focus is no longer presented as a standalone bonus. It is the whole-turn cost attached to a focused task (Rules 2.2.2).
- Medical treatment requires a medically equipped/trained unit, touching bases represented by the 1.5-inch adjacency threshold, and no prior movement. It consumes the entire turn as Focus (Rules 2.8).
- Fires observation requires the radio user to see the target and grants attack Advantage until the observer's next turn, including across a round boundary (Rules 5.1).
- Suppression can be attempted without direct line of sight when the first visible aim point is within six inches of the target. It still requires range and a weapon capable of attacking the target (Rules 2.6.5).
- Blocked LOS results now retain the first blocker's distance so the six-inch suppression exception can be adjudicated from generated terrain.

## Known implementation work still required

The current campaign scenario uses a deliberately narrow subset of Down Range. These full-rule systems remain partial or unimplemented and should not yet be described as complete:

- automatic-weapon Fan attacks against multiple targets and repeated attacks;
- explosive Radius placement, area damage, smoke, illumination, and ammunition tracking;
- dice-based vehicle Defense, weapon-versus-armor die restrictions in Unity, and the Armored Addendum damage systems;
- crew-served weapon assistance and static Skill modifiers;
- weapon-specific Focus requirements, training, anti-air penalties, and sensor-specific concealment;
- Main Effort, command initiative signaling, situational-awareness relays, EW actions, and remote-piloted vehicle Focus;
- a true interrupt stack in which a Reaction is resolved before its triggering action;
- path-by-path impaired movement and unit-specific terrain exemptions;
- suppression Fan/cone selection against multiple units. The current UI resolves one selected target at a time.

This audit should be updated whenever one of these systems becomes authoritative in the Unity resolver.
