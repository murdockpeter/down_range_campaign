const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { clamp, resolveAar } = require('../src/campaign-engine.cjs');
const { missionTwo, activateMissionTwo } = require('../src/campaign-missions.cjs');

const root = path.resolve(__dirname, '..');
const seed = JSON.parse(fs.readFileSync(path.join(root, 'data', 'campaign-seed.json'), 'utf8'));

test('seed contains a playable operational picture', () => {
  assert.equal(seed.schemaVersion, 6);
  assert.ok(seed.locations.length >= 8);
  assert.ok(seed.forces.length >= 6);
  assert.ok(seed.mission.objectives.length >= 3);
  assert.equal(seed.campaign.momentum, 0);
  assert.ok(Number.isFinite(seed.campaign.map.center.lat));
  assert.ok(seed.locations.every((location) => Number.isFinite(location.lat) && Number.isFinite(location.lng)));
  assert.match(seed.campaign.theater, /Latvia/);
  assert.ok(seed.locations.every((location) => location.lat > 56 && location.lat < 58));
  assert.ok(seed.locations.every((location) => location.lng > 26 && location.lng < 29));
  assert.equal(seed.planning.missionNumber, seed.mission.number);
});

test('every supplied library PDF is packaged', () => {
  assert.equal(seed.library.length, 6);
  assert.ok(seed.library.every((document) => document.file.startsWith('DownRangeLatest/')));
  assert.ok(seed.library.every((document) => /Authoritative/.test(document.title)));
  for (const document of seed.library) {
    assert.ok(fs.existsSync(path.join(root, 'docs', 'official', document.file)), document.file);
  }
});

test('AAR resolution advances turn and clamps momentum', () => {
  const copy = structuredClone(seed);
  copy.campaign.momentum = 3;
  const result = resolveAar(copy, { objectiveScore: 3, kia: 0, serious: 0, assetsLost: 0, outcome: 'Success', summary: 'Complete' });
  assert.equal(result.state.campaign.turn, 2);
  assert.equal(result.state.campaign.momentum, 3);
  assert.equal(result.state.mission.status, 'complete');
  assert.equal(clamp(-8, -3, 3), -3);
});

test('Mission 2 activates from an adjudicated Mission 1 without discarding campaign state', () => {
  const completed = structuredClone(seed);
  completed.campaign.turn = 2;
  completed.campaign.momentum = 1;
  completed.mission.status = 'complete';
  completed.history.unshift({ id:'mission-one', turn:1, mission:'Silent Lantern', outcome:'Operational success', summary:'Returned undetected.', momentumDelta:1, timestamp:'2031-09-14T06:00:00Z' });
  completed.casualties.push({ id:'returning', name:'PFC Test', unit:'3rd Squad', category:'WIA-L', returnTurn:2, note:'Recovering' });
  completed.forces.find(force => force.id === 'sq3').current = 8;

  const result = activateMissionTwo(completed);
  assert.equal(result.activated, true);
  assert.equal(result.state.mission.number, 2);
  assert.equal(result.state.mission.title, missionTwo.mission.title);
  assert.equal(result.state.mission.locationId, 'radio');
  assert.equal(result.state.planning.missionNumber, 2);
  assert.equal(result.state.tactical.scenario, 'Ghost Frequency');
  assert.equal(result.state.campaign.momentum, 1);
  assert.equal(result.state.history[0].id, 'mission-one');
  assert.equal(result.state.casualties.find(item => item.id === 'returning').category, 'RTD');
  assert.equal(result.state.forces.find(force => force.id === 'sq3').current, 9);
  assert.equal(result.state.intel[0].id, 'm2-i1');
});

test('Mission 2 cannot bypass Mission 1 adjudication and activation is idempotent', () => {
  assert.throws(() => activateMissionTwo(seed), /after Mission #1 has been adjudicated/);
  const completed = structuredClone(seed);
  completed.campaign.turn = 2;
  completed.mission.status = 'complete';
  const first = activateMissionTwo(completed);
  const second = activateMissionTwo(first.state);
  assert.equal(second.activated, false);
  assert.equal(second.state.mission.number, 2);
});

test('Silent Lantern TTS play assets are present', () => {
  assert.ok(fs.existsSync(path.join(root, 'assets', 'maps', 'silent-lantern-tts-map-v1.png')));
  assert.ok(fs.existsSync(path.join(root, 'scenarios', 'mission-01-silent-lantern-tts.md')));
});

test('local MCPP references are packaged inputs', () => {
  assert.ok(fs.existsSync(path.join(root, 'docs', 'MCPP-References.pdf')));
  assert.ok(fs.existsSync(path.join(root, 'docs', 'MCWP 5-10 (1) (SECURED).pdf')));
});

test('MCPP wizard provides a third COA migration', () => {
  const renderer = fs.readFileSync(path.join(root, 'src', 'renderer', 'app.js'), 'utf8');
  assert.match(renderer, /COA 3 — Offset UAS/);
  assert.match(renderer, /while\(state\.planning\.coas\.length<3\)/);
});
