const crypto = require('crypto');

const CONTRACT_VERSION = 1;

function createBattleRequest(state, options = {}) {
  if (!state?.campaign || !state?.mission || !state?.tactical?.units) throw new Error('Campaign tactical state is unavailable.');
  const requestId = options.requestId || crypto.randomUUID();
  const createdAt = options.createdAt || new Date().toISOString();
  const seed = Number.isInteger(options.seed) ? options.seed : crypto.randomInt(1, 2147483646);
  return {
    contractVersion: CONTRACT_VERSION,
    requestId,
    createdAt,
    rulesVersion: '1.4.2',
    campaign: {
      name: state.campaign.name,
      slug: state.campaign.slug,
      turn: state.campaign.turn,
      date: state.campaign.date
    },
    mission: {
      number: state.mission.number,
      title: state.mission.title,
      type: state.mission.type,
      durationTurns: Number.parseInt(state.mission.duration, 10) || 8,
      situation: state.mission.situation,
      intent: state.mission.intent
    },
    board: {
      mapPath: options.mapPath || '',
      widthInches: 64,
      heightInches: 42.6667,
      pixelsPerInch: 24
    },
    settings: { mode: 'hotseat', seed, autosave: true },
    objectives: state.mission.objectives.map(objective => ({
      id: objective.id, text: objective.text, points: objective.points, complete: Boolean(objective.complete)
    })),
    units: state.tactical.units.map(unit => ({
      id: unit.id,
      side: unit.side,
      name: unit.name,
      role: unit.role,
      forceId: unit.forceId || '',
      kind: unit.kind || 'troop',
      x: Number(unit.x), y: Number(unit.y),
      move: Number(unit.move), skill: Number(unit.skill),
      medicalSkill: Number(unit.medicalSkill || 0),
      defense: Number(unit.defense || 0),
      status: unit.status || 'healthy',
      radio: Boolean(unit.radio), flying: Boolean(unit.flying), ew: Boolean(unit.ew),
      weapons: (unit.weapons || []).map(weapon => ({
        id: weapon.id, name: weapon.name, range: Number(weapon.range), difficulty: Number(weapon.difficulty),
        damageSides: Number(weapon.damage?.sides || 0), damageModifier: Number(weapon.damage?.modifier || 0),
        fan: Number(weapon.fan || 1), radius: Number(weapon.radius || 0), ammunition: weapon.ammunition == null ? -1 : Number(weapon.ammunition)
      }))
    }))
  };
}

function validateBattleResult(result, pendingRequestId) {
  if (!result || result.contractVersion !== CONTRACT_VERSION) throw new Error('Unsupported Unity battle result contract.');
  if (!result.requestId || result.requestId !== pendingRequestId) throw new Error('Unity battle result does not match the pending battle request.');
  if (!result.resultId || !Array.isArray(result.units) || !Array.isArray(result.objectives)) throw new Error('Unity battle result is incomplete.');
}

function applyBattleResult(state, result) {
  const pending = state?.unityBattle?.pendingRequestId;
  validateBattleResult(result, pending);
  if ((state.unityBattle.importedResultIds || []).includes(result.resultId)) return { state: structuredClone(state), alreadyImported: true };
  const next = structuredClone(state);
  next.unityBattle ||= {};
  next.unityBattle.importedResultIds ||= [];
  next.unityBattle.importedResultIds.push(result.resultId);
  next.unityBattle.lastResultId = result.resultId;
  next.unityBattle.lastResultAt = result.completedAt;
  next.unityBattle.pendingRequestId = null;

  const sourceUnits = new Map((next.tactical?.units || []).map(unit => [unit.id, unit]));
  for (const unitResult of result.units) {
    const unit = sourceUnits.get(unitResult.id);
    if (!unit) continue;
    unit.x = Number(unitResult.x); unit.y = Number(unitResult.y); unit.status = unitResult.status;
  }
  for (const objectiveResult of result.objectives) {
    const objective = next.mission.objectives.find(item => item.id === objectiveResult.id);
    if (objective) objective.complete = Boolean(objectiveResult.complete);
  }
  for (const casualty of result.casualties || []) {
    const unit = sourceUnits.get(casualty.unitId);
    if (!unit || unit.campaignCasualtyCommitted) continue;
    const force = next.forces.find(item => item.id === unit.forceId);
    if (force) force.current = Math.max(0, force.current - 1);
    next.casualties.push({
      id: `unity-${result.resultId}-${unit.id}`,
      name: unit.name,
      unit: force?.name || unit.role,
      category: casualty.category,
      returnTurn: casualty.category === 'KIA' ? null : next.campaign.turn + 2,
      note: `Unity tactical casualty, Mission ${next.mission.number}: ${next.mission.title}`
    });
    unit.campaignCasualtyCommitted = true;
  }
  const blue = [...sourceUnits.values()].filter(unit => unit.side === 'blue');
  const effective = blue.filter(unit => !['downed','dead'].includes(unit.status)).length;
  next.mission.status = 'awaiting-aar';
  if (next.tactical) { next.tactical.committed = true; next.tactical.completed = true; }
  next.mission.tacticalSummary = {
    source: 'Unity', rounds: Number(result.rounds || 0), alarm: Boolean(result.alarm),
    observationTurns: Number(result.observationTurns || 0),
    kia: (result.casualties || []).filter(item => item.category === 'KIA').length,
    serious: (result.casualties || []).filter(item => item.category !== 'KIA').length,
    effective, starting: blue.length,
    log: (result.events || []).map(event => `R${event.round}: ${event.text}`).join('\n')
  };
  return { state: next, alreadyImported: false };
}

module.exports = { CONTRACT_VERSION, createBattleRequest, validateBattleResult, applyBattleResult };
