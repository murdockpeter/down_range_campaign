(function (root, factory) {
  const rules = factory();
  if (typeof module === 'object' && module.exports) module.exports = rules;
  else root.TacticalRules = rules;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
  function rollDie(sides, rng = Math.random) { return Math.floor(rng() * sides) + 1; }
  function rollPool({ count = 1, sides = 6, modifier = 0 }, rng = Math.random) {
    const rolls = Array.from({ length: Math.max(1, count) }, () => rollDie(sides, rng));
    return { rolls, total: rolls.reduce((sum, value) => sum + value, 0) + modifier };
  }
  function rollSkill(sides, advantage = 0, modifier = 0, rng = Math.random) {
    const first = rollDie(sides, rng);
    if (!advantage) return { rolls: [first], result: first + modifier, mode: 'normal' };
    const second = rollDie(sides, rng);
    return { rolls: [first, second], result: (advantage > 0 ? Math.max(first, second) : Math.min(first, second)) + modifier, mode: advantage > 0 ? 'advantage' : 'disadvantage' };
  }
  function distanceInches(a, b, boardWidthPx = 1536, boardWidthInches = 64) {
    const dx = (Number(a.x) - Number(b.x)) / 100 * boardWidthPx;
    const dy = (Number(a.y) - Number(b.y)) / 100 * (boardWidthPx / 1.5);
    return Math.hypot(dx, dy) / (boardWidthPx / boardWidthInches);
  }
  function canDamage(weapon, target) {
    if (!target.defenseDice) return true;
    return Number(weapon.damage?.sides || 0) >= Number(target.defenseDice.sides || 0);
  }
  function resolveAttack({ attacker, target, weapon, range, advantage = 0, cover = 'open', suppress = false, suppressionAimPossible = false }, rng = Math.random) {
    if (!attacker || !target || !weapon) return { ok: false, reason: 'Select an attacker, target, and weapon.' };
    if (target.status === 'dead') return { ok: false, reason: 'That target is already dead.' };
    if (range > weapon.range) return { ok: false, reason: `Target is ${range.toFixed(1)}\" away; ${weapon.name} range is ${weapon.range}\".` };
    if (cover === 'blocked' && (!suppress || !suppressionAimPossible)) return { ok: false, reason: suppress ? 'The attacker cannot aim within 6" of the concealed target.' : 'Total cover or concealment blocks line of sight.' };
    if (!canDamage(weapon, target)) return { ok: false, reason: `${weapon.name} cannot penetrate this target's defense die.` };
    const hasAdvantage = advantage > 0 || target.moved === false || target.ambushed;
    const hasDisadvantage = advantage < 0 || cover === 'partial' || attacker.status === 'injured' || attacker.suppressed;
    const net = hasAdvantage === hasDisadvantage ? 0 : hasAdvantage ? 1 : -1;
    const skill = rollSkill(attacker.skill, net, Number(weapon.skillModifier || 0), rng);
    const hit = skill.result >= weapon.difficulty;
    const base = { ok: true, hit, suppress, range, skill, difficulty: weapon.difficulty, advantage: net };
    if (!hit) return base;
    if (suppress) return { ...base, suppressed: true };
    const damage = rollSkill(weapon.damage.sides, cover === 'partial' && weapon.radius ? -1 : 0, weapon.damage.modifier || 0, rng);
    const defense = target.defenseDice ? rollPool(target.defenseDice, rng) : { rolls: [], total: Number(target.defense || 0) };
    return { ...base, damage, defense, casualty: damage.result >= defense.total };
  }
  function movementAllowance(unit, { sprint = false, impaired = false } = {}) {
    let allowance = Number(unit.move || 0);
    if (unit.status === 'injured') allowance /= 2;
    if (sprint && unit.kind === 'troop') allowance *= 2;
    if (impaired) allowance /= 2;
    return allowance;
  }
  function resolveMedicine(skillSides, rng = Math.random) {
    const roll = rollSkill(skillSides, 0, 0, rng);
    if (roll.result <= 2) return { roll, result: 'dead' };
    if (roll.result <= 4) return { roll, result: 'no-effect' };
    if (roll.result <= 7) return { roll, result: 'injured' };
    return { roll, result: 'healthy' };
  }
  function rollInitiative(blueBonus = 0, redBonus = 0, rng = Math.random) {
    let blue; let red;
    do { blue = rollDie(6, rng) + Number(blueBonus || 0); red = rollDie(6, rng) + Number(redBonus || 0); } while (blue === red);
    return { blue, red, first: blue > red ? 'blue' : 'red' };
  }
  return { clamp, rollDie, rollPool, rollSkill, distanceInches, canDamage, resolveAttack, movementAllowance, resolveMedicine, rollInitiative };
}));
