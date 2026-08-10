const missionTwo = {
  campaign: {
    date: '2031-09-14',
    phase: 'Intelligence & Reconnaissance — Close Collection',
    weather: 'Low overcast · 9°C · intermittent drizzle · visibility 700 m'
  },
  mission: {
    number: 2,
    title: 'Ghost Frequency',
    type: 'Special Reconnaissance',
    status: 'planning',
    locationId: 'radio',
    time: '2031-09-14 2145L',
    duration: '10 tactical turns',
    situation: 'Silent Lantern preserved the reconnaissance element and did not alert the enemy, but it failed to resolve the Grebņeva Relay antenna, guard routine, or access routes. A short-duration emission window is expected tonight. Task Force Lantern must exploit the enemy’s unchanged routine and close the intelligence gap before the window ends.',
    intent: {
      purpose: 'Positively identify the relay’s EW equipment and security pattern so Task Force Lantern can choose between interdiction, deception, or a later raid.',
      method: 'Insert a compact scout and signals-observation element onto the relay’s southern approach, establish close observation during the emission window, and withdraw without entering or assaulting the compound.',
      endState: 'The antenna and security element are identified, at least 75% of BLUE remains effective, and RED has no confirmed indication that the relay was observed.'
    },
    metttc: {
      Mission: 'Conduct close reconnaissance of the Grebņeva Relay; collect rather than seize terrain.',
      Enemy: 'Routine security patrol, fixed sentries, EW operator, and a possible reaction force from the A13. RED is assessed as unaware of Mission 1.',
      'Terrain & Weather': 'Relay hardstand, service road, drainage ground, scattered woods, and open cleared arcs; drizzle and low cloud reduce long observation.',
      'Troops & Support': 'Five-person scout/signals element and one Raven UAS. Mortars remain on call for emergency disengagement only.',
      Time: 'The useful emission window is assessed from 2200L to 2315L. Complete collection and withdraw by Turn 10.',
      'Civil Considerations': 'A maintenance vehicle may use the southern service road. Do not detain personnel or damage civilian infrastructure.'
    },
    ocoka: {
      'Observation & Fields of Fire': 'The compound clearing favors its sentries; the southern drainage and western treeline offer intermittent views of the antenna hardstand.',
      'Cover & Concealment': 'Drainage banks, wet brush, and service-road cuts conceal low movement. The final approach crosses exposed ground.',
      Obstacles: 'Perimeter fencing, wet ditches, security lighting, and the service road canalize movement near the compound.',
      'Key Terrain': 'Southern drainage bend, western treeline observation pocket, antenna hardstand, and the service-road gate.',
      'Avenues of Approach': 'The southern drainage is slow and concealed; the western treeline is faster but exposed to the patrol; the service road is unsuitable except for emergency withdrawal.'
    },
    objectives: [
      { id: 'o1', text: 'Observe the Grebņeva Relay for two uninterrupted turns', points: 2, complete: false, type: 'observe-zone', actionLabel: 'OBSERVE RELAY', side: 'blue', x: 73, y: 22, radius: 18, requiredProgress: 2, progress: 0, lastProgressRound: 0, uninterrupted: true, requiresLos: true },
      { id: 'o2', text: 'Identify the EW antenna and security element', points: 2, complete: false, type: 'identify-units', actionLabel: 'IDENTIFY RELAY PERSONNEL', side: 'blue', radius: 24, requiredProgress: 2, progress: 0, lastProgressRound: 0, difficulty: 4, requiresLos: true, targetUnitIds: ['r5', 'r3', 'r4'], identifiedUnitIds: [] },
      { id: 'o3', text: 'Extract at least 75% of BLUE effective through the southern edge', points: 1, complete: false, type: 'extract-force', side: 'blue', threshold: .75, edge: 'south', depth: 18 },
      { id: 'o4', text: 'Avoid raising the alarm', points: 1, complete: false, type: 'avoid-alarm', side: 'blue', radius: 36, difficulty: 4, requiresLos: true }
    ],
    allocation: [
      '1st Squad scout team (4)',
      'Platoon HQ radio operator (1)',
      'UAS Team Raven quadcopter (1)'
    ],
    sync: [
      { phase: 'Approach', who: 'Scout team', what: 'Occupy southern drainage and confirm the withdrawal lane', when: 'Turns 1–3', where: 'Relay southern approach', trigger: 'Patrol clears the western track' },
      { phase: 'Collection', who: 'Scout + radio operator', what: 'Observe antenna, EW operator, and guard routine', when: 'Turns 4–7', where: 'Western observation pocket', trigger: 'Emitter becomes active' },
      { phase: 'Screen', who: 'Raven UAS', what: 'Check service road and eastern reaction route', when: 'On call', where: 'Offset south of the compound', trigger: 'Ground element reports movement or loss of view' },
      { phase: 'Withdrawal', who: 'All', what: 'Break observation and return south', when: 'Turns 8–10', where: 'Drainage withdrawal lane', trigger: 'Collection complete, compromise, or Turn 8' }
    ]
  },
  planning: {
    missionNumber: 2,
    currentStep: 0,
    completed: [],
    problem: {
      currentState: 'BLUE retained its reconnaissance force and secrecy, but Mission 1 produced only negative information. The relay remains active and its protection is unresolved.',
      desiredState: 'The relay’s EW equipment, guard pattern, and reaction routes are positively identified without confirming BLUE presence.',
      problemStatement: 'Task Force Lantern needs close-range collection, but the relay clearing, patrol pattern, wet approaches, and short emission window make prolonged observation increasingly dangerous.',
      specifiedTasks: 'Observe the relay during the emission window, identify the EW antenna and security element, preserve the force, and avoid an alarm.',
      impliedTasks: 'Confirm patrol timing; establish a concealed OP; screen the A13 reaction route; maintain a withdrawal lane; report and exfiltrate.',
      essentialTasks: 'Complete two uninterrupted observation turns and withdraw with at least 75% effective strength.',
      friendlyCog: 'The small scout/signals element’s ability to remain concealed while sharing timely observations.',
      enemyCog: 'The relay-centered warning network and its routine security element.',
      criticalVulnerability: 'A predictable emission window and unchanged guard routine caused by BLUE’s undetected Mission 1 withdrawal.',
      assumptions: 'RED has no confirmed warning from Mission 1. The relay emits during the assessed window. The A13 reaction force is not already at the compound.',
      limitations: 'Do not enter or assault the compound. Avoid civilian casualties and infrastructure damage. Withdraw by Turn 10.',
      ccirs: 'Has RED detected BLUE? Which antenna supports EW? What force protects the relay? Is a reaction force moving from the A13?',
      missionStatement: 'At 2145L on 14 September, a Task Force Lantern scout/signals element conducts close reconnaissance of the Grebņeva Relay to identify its EW equipment and security pattern, then withdraws by Turn 10 without raising the alarm.'
    },
    coas: [
      { name: 'COA 1 — Southern Drainage', concept: 'Use wet drainage ground to reach a close observation pocket south-west of the relay.', mainEffort: 'Scout team establishes the close OP and conducts visual identification.', supportingEffort: 'Raven screens the eastern service road in brief windows.', reserve: 'Radio operator remains behind the forward scout pair on the withdrawal lane.', phases: 'Drainage approach → close OP → timed collection → withdrawal south.', risk: 'Slow movement may leave too little time to observe and withdraw.', feasible: true, acceptable: true, suitable: true, distinguishable: true, complete: true },
      { name: 'COA 2 — Western Treeline', concept: 'Move rapidly along the western trees to observe across the compound clearing.', mainEffort: 'Scout team uses a longer-range visual OP.', supportingEffort: 'Radio operator correlates observed activity with the emission window.', reserve: 'Raven remains grounded unless the patrol masks the OP.', phases: 'Treeline bound → establish distant OP → correlate → withdraw west then south.', risk: 'The patrol can sweep the same treeline and cut off the direct return route.', feasible: true, acceptable: true, suitable: true, distinguishable: true, complete: true },
      { name: 'COA 3 — Split Sensors', concept: 'Keep the ground element in deep concealment and use displaced Raven passes to build the picture from multiple angles.', mainEffort: 'Raven conducts short, offset collection passes.', supportingEffort: 'Ground scouts visually confirm the guard routine from the drainage.', reserve: 'One scout pair protects the launch site and recovery route.', phases: 'Secure launch site → alternating air/ground collection → recover → exfiltrate.', risk: 'EW detection or loss of the UAS could expose the mission without positively identifying personnel.', feasible: true, acceptable: true, suitable: true, distinguishable: true, complete: true }
    ],
    wargame: {
      technique: 'Key events / action–reaction–counteraction',
      enemyMostLikely: 'The patrol and sentries maintain their normal routine, investigating only a clear visual, sound, or electronic anomaly.',
      enemyMostDangerous: 'The EW operator detects the Raven or a BLUE transmission, the gate sentry fixes the ground element, and a reaction force enters from the A13 service road.',
      rows: [
        { event: 'Cross the final covered approach', action: 'BLUE moves through the southern drainage toward the OP.', reaction: 'The patrol pauses near the western clearing before continuing its routine.', civilian: 'Possible maintenance vehicle on the service road.', counteraction: 'Hold below the drainage bank; cross only after both patrol and vehicle clear.' },
        { event: 'Emitter activation', action: 'BLUE observes the antenna and correlates personnel activity.', reaction: 'EW operator may conduct a perimeter scan or brief equipment check.', civilian: 'No expected involvement inside the compound.', counteraction: 'Remain passive; use Raven only if the ground view cannot identify the equipment.' },
        { event: 'Withdrawal', action: 'BLUE breaks observation and moves south.', reaction: 'If suspicious, the patrol searches the western treeline while the gate closes.', civilian: 'Late traffic may illuminate the road crossing.', counteraction: 'Use the drainage route; emergency smoke or mortar support only to break confirmed contact.' }
      ],
      branches: 'If the southern drainage is compromised, displace to the western treeline and accept longer-range collection. If the Raven is detected, recover only if doing so does not expose the ground element.',
      sequels: 'Successful identification enables a relay interdiction or deception mission. Failure without alarm produces a remote SIGINT collection attempt; an alarm produces a RED counter-reconnaissance operation.',
      decisionPoints: 'Abort on confirmed alarm, movement of a reaction force onto the southern route, loss of the withdrawal lane, or BLUE falling below 75% effective strength.',
      refinements: 'Keep transmissions short, define the Raven no-fly trigger, and identify the latest turn on which observation can begin while preserving withdrawal time.'
    },
    comparison: {
      criteria: [
        { name: 'Collection certainty', weight: 5, scores: [5, 4, 3] },
        { name: 'Avoid detection', weight: 5, scores: [4, 3, 4] },
        { name: 'Force preservation', weight: 4, scores: [4, 3, 5] },
        { name: 'Time available', weight: 3, scores: [3, 5, 4] },
        { name: 'Withdrawal reliability', weight: 4, scores: [5, 3, 4] }
      ],
      selected: 0,
      rationale: 'COA 1 provides the best combination of positive identification and a protected withdrawal lane. Its time risk is manageable if BLUE aborts the approach when the OP is not established by Turn 4.'
    },
    orders: {
      situation: 'RED remains unaware of the prior reconnaissance attempt. The Grebņeva Relay continues periodic emissions under routine patrol and sentry protection. Low cloud and drizzle aid approach but shorten reliable observation.',
      mission: 'At 2145L on 14 September, a Task Force Lantern scout/signals element conducts close reconnaissance of the Grebņeva Relay to identify its EW equipment and security pattern, then withdraws by Turn 10 without raising the alarm.',
      execution: 'Commander’s intent\nPurpose: Enable a deliberate campaign decision against the relay.\nMethod: Use the southern drainage to establish a close visual OP, correlate the emission window with observed equipment and personnel, and retain a protected withdrawal lane.\nEnd state: Required intelligence collected, BLUE at 75% strength or better, and RED unalerted.\n\nConcept: Execute in four phases—approach, collection, sensor screen, and withdrawal. Do not trade concealment for speed unless required to preserve the withdrawal timeline.',
      admin: 'Carry mission-essential ammunition, optics, and one Raven UAS. Mortars are emergency disengagement support only. A serious casualty or inability to move a casualty along the drainage triggers abort.',
      commandSignal: 'Scout leader controls. Radio operator succeeds command. Primary communications are visual and prearranged signals; radio is reserved for compromise, casualty, collection complete, or abort. Report antenna type, personnel, patrol timing, and reaction route.'
    },
    transition: { mapBrief: false, confirmationBrief: false, commsCheck: false, rehearsal: false, casualtyPlan: false, abortCriteria: false, uasPlan: false, questions: '', ready: false }
  },
  tactical: {
    scenario: 'Ghost Frequency', round: 1, activeSide: 'blue', actedSides: [], initiative: { blue: 4, red: 3, first: 'blue' },
    selectedId: 'b1', targetId: 'r1', cover: 'open', impairedMovement: false, alarm: false, observationTurns: 0, completed: false, committed: false,
    log: [{ id: 'mission-2-start', round: 1, text: 'BLUE begins the concealed approach during the relay emission window.', kind: 'system' }],
    units: [
      { id: 'b1', side: 'blue', name: 'Scout lead', role: 'Team leader', forceId: 'sq1', kind: 'troop', x: 12, y: 84, facing: 0, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'm4', name: 'M4 carbine', range: 36, difficulty: 3, damage: { sides: 6 } }], radio: true },
      { id: 'b2', side: 'blue', name: 'Automatic rifleman', role: 'Scout team', forceId: 'sq1', kind: 'troop', x: 16, y: 88, facing: 0, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'm249', name: 'M249 SAW', range: 36, difficulty: 4, damage: { sides: 6 }, fan: 2 }], radio: false },
      { id: 'b3', side: 'blue', name: 'Scout rifleman', role: 'Scout team', forceId: 'sq1', kind: 'troop', x: 20, y: 84, facing: 0, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'm4', name: 'M4 carbine', range: 36, difficulty: 3, damage: { sides: 6 } }], radio: false },
      { id: 'b4', side: 'blue', name: 'Scout medic', role: 'Combat lifesaver', forceId: 'sq1', kind: 'troop', x: 24, y: 88, facing: 0, move: 8, skill: 6, medicalSkill: 8, defense: 4, status: 'healthy', weapons: [{ id: 'm4', name: 'M4 carbine', range: 36, difficulty: 3, damage: { sides: 6 } }], radio: false },
      { id: 'b5', side: 'blue', name: 'Radio operator', role: 'Platoon HQ', forceId: 'hq', kind: 'troop', x: 9, y: 90, facing: 0, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'm4', name: 'M4 carbine', range: 36, difficulty: 3, damage: { sides: 6 } }], radio: true },
      { id: 'b6', side: 'blue', name: 'Raven UAS', role: 'Unarmed ISR', forceId: 'uas', kind: 'vehicle', x: 13, y: 94, facing: 0, move: 48, skill: 6, defense: 3, status: 'healthy', weapons: [], flying: true, radio: true },
      { id: 'r1', side: 'red', name: 'Patrol leader', role: 'Security patrol', kind: 'troop', x: 49, y: 38, facing: 180, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'ak', name: 'AK rifle', range: 28, difficulty: 3, damage: { sides: 6 } }], radio: true },
      { id: 'r2', side: 'red', name: 'Patrol rifleman', role: 'Security patrol', kind: 'troop', x: 55, y: 43, facing: 180, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'ak', name: 'AK rifle', range: 28, difficulty: 3, damage: { sides: 6 } }], radio: false },
      { id: 'r3', side: 'red', name: 'West sentry', role: 'Relay guard', kind: 'troop', x: 67, y: 18, facing: 180, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'ak', name: 'AK rifle', range: 28, difficulty: 3, damage: { sides: 6 } }], radio: false },
      { id: 'r4', side: 'red', name: 'Gate sentry', role: 'Relay guard', kind: 'troop', x: 78, y: 33, facing: 180, move: 8, skill: 6, defense: 4, status: 'healthy', weapons: [{ id: 'ak', name: 'AK rifle', range: 28, difficulty: 3, damage: { sides: 6 } }], radio: false },
      { id: 'r5', side: 'red', name: 'EW operator', role: 'Specialist', kind: 'troop', x: 73, y: 22, facing: 180, move: 8, skill: 6, specialistSkill: 8, defense: 4, status: 'healthy', weapons: [{ id: 'ak', name: 'AK rifle', range: 28, difficulty: 3, damage: { sides: 6 } }], radio: true, ew: true }
    ]
  }
};

function activateMissionTwo(state) {
  if (!state?.campaign || !state?.mission) throw new Error('Campaign state is unavailable.');
  if (Number(state.mission.number) === 2) {
    const next = structuredClone(state);
    const before = JSON.stringify(next.mission.objectives || []);
    for (const objective of next.mission.objectives || []) {
      const definition = missionTwo.mission.objectives.find(item => item.id === objective.id); if (!definition) continue;
      const progress = objective.progress; const complete = objective.complete; const identifiedUnitIds = objective.identifiedUnitIds; const lastProgressRound = objective.lastProgressRound;
      Object.assign(objective, structuredClone(definition));
      objective.complete = Boolean(complete);
      if (progress != null || definition.progress != null) objective.progress = Number(progress || 0);
      if (lastProgressRound != null || definition.lastProgressRound != null) objective.lastProgressRound = Number(lastProgressRound || 0);
      if (identifiedUnitIds != null || definition.identifiedUnitIds != null) objective.identifiedUnitIds = identifiedUnitIds || objective.identifiedUnitIds || [];
    }
    return { state: next, activated: false, upgraded: before !== JSON.stringify(next.mission.objectives || []) };
  }
  if (Number(state.mission.number) !== 1 || state.mission.status !== 'complete' || Number(state.campaign.turn) < 2) {
    throw new Error('Mission #2 becomes available after Mission #1 has been adjudicated.');
  }

  const next = structuredClone(state);
  Object.assign(next.campaign, missionTwo.campaign);
  next.mission = structuredClone(missionTwo.mission);
  next.planning = structuredClone(missionTwo.planning);
  next.tactical = structuredClone(missionTwo.tactical);
  next.unityBattle ||= {};
  next.unityBattle.pendingRequestId = null;
  delete next.unityBattle.requestPath;
  delete next.unityBattle.launchedAt;

  const hill = next.locations.find(location => location.id === 'hill402');
  if (hill) hill.intel = 'BLUE withdrew without contact or signs of enemy alert; thermal activity remains intermittent.';
  const relay = next.locations.find(location => location.id === 'radio');
  if (relay) relay.intel = 'Emitter remains active. Antenna type, guard routine, and reaction route are still unresolved.';

  const reports = [
    { id: 'm2-i1', grade: 'A2', title: 'Silent Lantern debrief', detail: 'The reconnaissance element returned intact and reports no indication that RED detected the approach or withdrawal. The absence of collection leaves the relay protection requirement open.', age: '16 hours', confidence: 'High' },
    { id: 'm2-i2', grade: 'B2', title: 'Relay emission window', detail: 'Passive monitoring predicts a short high-power Grebņeva Relay transmission window between 2200L and 2315L. Equipment and operators may be observable while the emitter is active.', age: '2 hours', confidence: 'High' }
  ];
  next.intel = [...reports, ...(next.intel || []).filter(report => !reports.some(item => item.id === report.id))];

  for (const casualty of next.casualties || []) {
    if (casualty.category !== 'WIA-L' || casualty.returnTurn == null || Number(casualty.returnTurn) > Number(next.campaign.turn)) continue;
    casualty.category = 'RTD';
    casualty.note = `${casualty.note || ''} Returned to duty before Mission 2.`.trim();
    const force = next.forces.find(item => item.name === casualty.unit);
    if (force && force.current < force.authorized) {
      force.current += 1;
      force.readiness = Math.round(force.current / Math.max(1, force.authorized) * 100);
      if (force.current >= force.authorized) force.status = 'Available';
    }
  }

  next.campaign.lastUpdated = new Date().toISOString();
  return { state: next, activated: true, upgraded: true };
}

module.exports = { missionTwo, activateMissionTwo };
