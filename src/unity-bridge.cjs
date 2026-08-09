const crypto = require('crypto');

const CONTRACT_VERSION = 1;

const TERRAIN_PROFILES = {
  hill402: { archetype:'wooded-ridge', elevation:0.9, treeDensity:0.72, buildingDensity:0.05, water:0.28, wetGround:0.42, roadPattern:'trail', features:['low ridges','dense treelines','lake margin','relay overlook'] },
  radio: { archetype:'relay-compound', elevation:0.42, treeDensity:0.38, buildingDensity:0.32, water:0.02, wetGround:0.2, roadPattern:'service-road', features:['fenced relay compound','antenna hardstand','border road','cleared fields of fire'] },
  farm: { archetype:'farmland', elevation:0.22, treeDensity:0.24, buildingDensity:0.13, water:0.12, wetGround:0.58, roadPattern:'farm-lanes', features:['open fields','drainage ditches','farm cluster','woodlot boundaries'] },
  village: { archetype:'small-town', elevation:0.16, treeDensity:0.16, buildingDensity:0.68, water:0.01, wetGround:0.15, roadPattern:'street-grid', features:['dense village blocks','rail approach','main road','courtyards'] },
  mine: { archetype:'railhead', elevation:0.18, treeDensity:0.12, buildingDensity:0.42, water:0.03, wetGround:0.24, roadPattern:'rail-yard', features:['parallel rail lines','freight sheds','loading yard','scattered industrial buildings'] },
  highway: { archetype:'highway-junction', elevation:0.12, treeDensity:0.12, buildingDensity:0.22, water:0.01, wetGround:0.12, roadPattern:'junction', features:['divided highway','checkpoint','village edge','open fields'] },
  crossing: { archetype:'dam-crossing', elevation:0.3, treeDensity:0.22, buildingDensity:0.18, water:0.62, wetGround:0.72, roadPattern:'causeway', features:['reservoir edge','dam wall','wet approaches','village outbuildings'] },
  fob: { archetype:'forward-base', elevation:0.14, treeDensity:0.1, buildingDensity:0.48, water:0, wetGround:0.18, roadPattern:'base-loop', features:['perimeter berm','operations buildings','motor pool','rural access road'] }
};

function terrainProfileFor(state, seed) {
  const location = (state.locations || []).find(item => item.id === state.mission?.locationId) || state.locations?.[0] || {};
  const source = TERRAIN_PROFILES[location.id] || TERRAIN_PROFILES.farm;
  return {
    locationId: location.id || 'unknown', locationName: location.name || 'Unspecified target area',
    description: location.terrain || 'Mixed rural terrain', archetype: source.archetype,
    seed, gridCellSize: 1, smoothingPasses: 3, cells: [], elevation: source.elevation, treeDensity: source.treeDensity, buildingDensity: source.buildingDensity,
    water: source.water, wetGround: source.wetGround, roadPattern: source.roadPattern,
    features: source.features.slice()
  };
}

function createBattleRequest(state, options = {}) {
  if (!state?.campaign || !state?.mission || !state?.tactical?.units) throw new Error('Campaign tactical state is unavailable.');
  const requestId = options.requestId || crypto.randomUUID();
  const createdAt = options.createdAt || new Date().toISOString();
  const seed = Number.isInteger(options.seed) ? options.seed : crypto.randomInt(1, 2147483646);
  const terrain = terrainProfileFor(state, seed);
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
      locationId: terrain.locationId,
      locationName: terrain.locationName,
      terrain: terrain.description,
      durationTurns: Number.parseInt(state.mission.duration, 10) || 8,
      situation: state.mission.situation,
      intent: state.mission.intent
    },
    board: {
      mapPath: options.mapPath || '',
      widthInches: 64,
      heightInches: 42.6667,
      pixelsPerInch: 24,
      terrain
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
      modelId: unit.modelId || '',
      x: Number(unit.x), y: Number(unit.y),
      facing: Number(unit.facing ?? (unit.side === 'red' ? 180 : 0)), facingSet: true,
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
    unit.x = Number(unitResult.x); unit.y = Number(unitResult.y); unit.facing = Number(unitResult.facing || 0); unit.status = unitResult.status;
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
    scoreEarned: Number(result.scoreEarned ?? result.objectives.filter(item => item.complete).reduce((sum, item) => sum + Number(next.mission.objectives.find(objective => objective.id === item.id)?.points || 0), 0)),
    scoreAvailable: Number(result.scoreAvailable ?? next.mission.objectives.reduce((sum, item) => sum + Number(item.points || 0), 0)),
    outcome: result.outcome || 'Mission complete',
    terrainLocationId: result.terrainLocationId || next.mission.locationId || '',
    kia: (result.casualties || []).filter(item => item.category === 'KIA').length,
    serious: (result.casualties || []).filter(item => item.category !== 'KIA').length,
    effective, starting: blue.length,
    log: (result.events || []).map(event => `R${event.round}: ${event.text}`).join('\n')
  };
  return { state: next, alreadyImported: false };
}

module.exports = { CONTRACT_VERSION, TERRAIN_PROFILES, terrainProfileFor, createBattleRequest, validateBattleResult, applyBattleResult };
