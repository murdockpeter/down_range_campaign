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
- Movement now samples the declared path in short segments. Only impaired segments cost twice their measured distance, while water, wet ground, dense woods, steep slopes, authored terrain, road use, and impassable structures are evaluated against unit mobility properties (Rules 2.3 and 2.3.1).
- Saved Reactions now interrupt declared movement, fire, suppression, signals, treatment, and mission actions before the trigger resolves. Each eligible reactor may Fire, Sprint once at normal movement, or Pass and retain the Reaction; an incapacitated triggering unit loses its pending action (Rules 2.2.2.2 and example 6.5.2).
- Mission objectives are data-driven. Observation zones enforce range, LOS, once-per-round progress, and consecutive-round continuity; identification objectives enforce specified targets, range, LOS, and a static Difficulty Skill check under the non-attack action rules (Rules 2.7).
- Mission reconnaissance now uses the rules' open, partial, and total concealment classifications. Open enemy LOS confirms detection; partial concealment requires the observer to pass a scenario-defined Skill check with Disadvantage; total concealment blocks detection. The alarm test itself is a Mission #2 scenario adjudication, not an additional universal spotting rule (Rules 2.4.1.1 and 2.7).
- Data-driven extraction zones require units to deploy beyond the friendly band and return effective before they count. Mission duration is enforced after both sides complete the final specified round; these are scenario contract rules rather than universal tactical actions.

## Remaining implementation work

This section previously listed the pre-expansion backlog and is now stale: several listed systems have playable implementations, but some remain partial or use provisional digital adjudication. The maintained status matrix, milestone plan, test gates, and next sprint are in [`rules-implementation-roadmap.md`](rules-implementation-roadmap.md).

The roadmap deliberately distinguishes **present in the UI** from **verified against the complete tabletop rule**. In particular, automatic Fan target allocation and d8 explosive miss direction are provisional and must be replaced by the player choices specified in the Rules before those systems are called complete.
