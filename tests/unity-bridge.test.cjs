const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { TERRAIN_PROFILES, terrainProfileFor, createBattleRequest, applyBattleResult } = require('../src/unity-bridge.cjs');
const { activateMissionTwo } = require('../src/campaign-missions.cjs');

const root = path.resolve(__dirname, '..');
const seed = JSON.parse(fs.readFileSync(path.join(root, 'data', 'campaign-seed.json'), 'utf8'));
const tactical = [
  {id:'b1',side:'blue',name:'Scout',role:'Scout',forceId:'sq1',kind:'troop',modelId:'USMC Rifleman',x:10,y:80,move:8,skill:6,defense:4,status:'healthy',weapons:[{id:'m4',name:'M4',range:36,difficulty:3,damage:{sides:6}}]},
  {id:'r1',side:'red',name:'Guard',role:'Guard',kind:'troop',x:70,y:20,move:8,skill:6,defense:4,status:'healthy',weapons:[]}
];

test('battle request serializes campaign, board, units, weapons, and deterministic seed', () => {
  const state={...structuredClone(seed),tactical:{units:tactical}};
  const request=createBattleRequest(state,{requestId:'req-1',createdAt:'2031-09-14T04:30:00Z',seed:42,mapPath:'map.png'});
  assert.equal(request.contractVersion,1);
  assert.equal(request.requestId,'req-1');
  assert.equal(request.settings.seed,42);
  assert.equal(request.board.widthInches,64);
  assert.equal(request.board.terrain.locationId,'hill402');
  assert.equal(request.board.terrain.gridCellSize,1);
  assert.equal(request.board.terrain.smoothingPasses,3);
  assert.equal(request.board.terrain.archetype,'wooded-ridge');
  assert.equal(request.board.terrain.woodland,'heavy');
  assert.equal(request.units[0].modelId,'USMC Rifleman');
  assert.equal(request.units[1].facing,180);
  assert.equal(request.units[1].facingSet,true);
  assert.equal(request.units[0].weapons[0].damageSides,6);
});

test('every campaign target node has a deterministic generative terrain profile', () => {
  for (const location of seed.locations) {
    assert.ok(TERRAIN_PROFILES[location.id]);
    const state={...structuredClone(seed),mission:{...structuredClone(seed.mission),locationId:location.id}};
    const profile=terrainProfileFor(state,77);
    assert.equal(profile.locationId,location.id);
    assert.equal(profile.seed,77);
    assert.equal(profile.gridCellSize,1);
    assert.ok(['heavy','light','sparse'].includes(profile.woodland));
    assert.ok(profile.features.length>=4);
  }
});

test('Mission 2 launches as Ghost Frequency on the generated relay compound', () => {
  const completed = structuredClone(seed);
  completed.campaign.turn = 2;
  completed.mission.status = 'complete';
  const activated = activateMissionTwo(completed).state;
  const request = createBattleRequest(activated, { requestId:'mission-2', createdAt:'2031-09-14T21:45:00Z', seed:84 });
  assert.equal(request.mission.number, 2);
  assert.equal(request.mission.title, 'Ghost Frequency');
  assert.equal(request.mission.locationId, 'radio');
  assert.equal(request.board.terrain.archetype, 'relay-compound');
  assert.equal(request.mission.durationTurns, 10);
  assert.equal(request.units.length, 11);
  assert.equal(request.objectives.length, 4);
  assert.equal(request.objectives[0].type, 'observe-zone');
  assert.equal(request.objectives[0].requiredProgress, 2);
  assert.equal(request.objectives[1].type, 'identify-units');
  assert.deepEqual(request.objectives[1].targetUnitIds, ['r5','r3','r4']);
  assert.equal(request.objectives[2].type, 'extract-force');
  assert.equal(request.objectives[2].edge, 'south');
  assert.equal(request.objectives[2].depth, 18);
  assert.equal(request.objectives[3].type, 'avoid-alarm');
  assert.equal(request.objectives[3].difficulty, 4);
  assert.equal(request.objectives[3].radius, 36);
});

test('battle result imports objectives and casualties once', () => {
  const state={...structuredClone(seed),tactical:{units:structuredClone(tactical)},unityBattle:{pendingRequestId:'req-1',importedResultIds:[]}};
  const result={contractVersion:1,requestId:'req-1',resultId:'result-1',completedAt:'2031-09-14T05:10:00Z',rounds:6,alarm:true,alarmReason:'Guard detected Scout.',endedByDeadline:true,observationTurns:2,scoreEarned:2,scoreAvailable:6,outcome:'Mission setback',terrainLocationId:'hill402',units:[{id:'b1',x:5,y:80,facing:63,status:'downed'},{id:'r1',x:70,y:20,facing:180,status:'healthy'}],objectives:[{id:'o1',complete:true}],casualties:[{unitId:'b1',category:'WIA-S'}],events:[{round:2,text:'Contact'}],calculations:[{sequence:1,round:1,side:'blue',category:'Movement',command:'Move',inputs:'8 inches',computation:'6 + 2 = 8',outcome:'Accepted'}]};
  const imported=applyBattleResult(state,result);
  assert.equal(imported.state.mission.objectives[0].complete,true);
  assert.equal(imported.state.tactical.units[0].facing,63);
  assert.equal(imported.state.forces.find(force=>force.id==='sq1').current,8);
  assert.equal(imported.state.mission.tacticalSummary.source,'Unity');
  assert.equal(imported.state.mission.tacticalSummary.scoreEarned,2);
  assert.equal(imported.state.mission.tacticalSummary.outcome,'Mission setback');
  assert.equal(imported.state.mission.tacticalSummary.terrainLocationId,'hill402');
  assert.equal(imported.state.mission.tacticalSummary.alarmReason,'Guard detected Scout.');
  assert.equal(imported.state.mission.tacticalSummary.endedByDeadline,true);
  assert.equal(imported.state.mission.tacticalSummary.rulesTrace[0].category,'Movement');
  assert.equal(imported.state.mission.tacticalSummary.rulesTrace[0].computation,'6 + 2 = 8');
  assert.equal(imported.state.tactical.committed,true);
  const replayState=structuredClone(imported.state);replayState.unityBattle.pendingRequestId='req-1';
  assert.equal(applyBattleResult(replayState,result).alreadyImported,true);
});

test('mismatched battle result is rejected', () => {
  const state={...structuredClone(seed),tactical:{units:tactical},unityBattle:{pendingRequestId:'req-1'}};
  assert.throws(()=>applyBattleResult(state,{contractVersion:1,requestId:'wrong',resultId:'r',units:[],objectives:[]}),/does not match/);
});
