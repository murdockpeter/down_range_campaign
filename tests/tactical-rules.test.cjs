const test = require('node:test');
const assert = require('node:assert/strict');
const rules = require('../src/renderer/tactical-rules.js');

function sequence(values) {
  let index = 0;
  return () => values[Math.min(index++, values.length - 1)];
}

const rifle = { name:'M4', range:36, difficulty:3, damage:{sides:6} };
const troop = (overrides={}) => ({ skill:6, defense:4, move:8, kind:'troop', status:'healthy', moved:true, ...overrides });

test('advantage keeps the higher skill die and resolves damage against static defense', () => {
  const result = rules.resolveAttack({ attacker:troop(), target:troop({moved:false}), weapon:rifle, range:12 }, sequence([0.1,0.8,0.7]));
  assert.equal(result.skill.mode, 'advantage');
  assert.equal(result.skill.result, 5);
  assert.equal(result.casualty, true);
});

test('partial cover imposes disadvantage and total cover blocks an attack', () => {
  const partial = rules.resolveAttack({ attacker:troop(), target:troop(), weapon:rifle, range:12, cover:'partial' }, sequence([0.9,0.1]));
  assert.equal(partial.skill.mode, 'disadvantage');
  assert.equal(partial.hit, false);
  assert.equal(rules.resolveAttack({ attacker:troop(), target:troop(), weapon:rifle, range:12, cover:'blocked' }).ok, false);
});

test('range and armor die size are enforced', () => {
  assert.equal(rules.resolveAttack({ attacker:troop(), target:troop(), weapon:rifle, range:40 }).ok, false);
  assert.equal(rules.canDamage(rifle, troop({defenseDice:{count:1,sides:8}})), false);
});

test('injury, sprint, and impaired terrain modify movement', () => {
  assert.equal(rules.movementAllowance(troop(), {sprint:true}), 16);
  assert.equal(rules.movementAllowance(troop({status:'injured'}), {sprint:true,impaired:true}), 4);
});

test('medical table returns downed troops injured on a 5 through 7', () => {
  assert.equal(rules.resolveMedicine(6, sequence([0.8])).result, 'injured');
  assert.equal(rules.resolveMedicine(6, sequence([0.99])).result, 'injured');
  assert.equal(rules.resolveMedicine(8, sequence([0.99])).result, 'healthy');
});

test('initiative rerolls ties', () => {
  const result = rules.rollInitiative(0,0,sequence([0.2,0.2,0.8,0.1]));
  assert.deepEqual(result,{blue:5,red:1,first:'blue'});
});
