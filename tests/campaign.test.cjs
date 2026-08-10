const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { clamp, resolveAar } = require('../src/campaign-engine.cjs');

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
