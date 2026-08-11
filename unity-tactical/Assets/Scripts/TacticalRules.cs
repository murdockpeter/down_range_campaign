using System;
using System.Collections.Generic;

namespace DownRange.Tactical
{
    public sealed class DeterministicDice
    {
        readonly Random random;
        readonly Queue<int> manual = new Queue<int>();
        public int RollCount { get; private set; }
        public int ManualRemaining { get { return manual.Count; } }
        public DeterministicDice(int seed, int rollsToSkip = 0)
        {
            random = new Random(seed);
            for (var i = 0; i < rollsToSkip; i++) Roll(6);
        }
        public void QueueManual(IEnumerable<int> values) { if (values == null) return; foreach (var value in values) manual.Enqueue(value); }
        public int Roll(int sides) { RollCount++; return manual.Count > 0 ? Math.Max(1, Math.Min(sides, manual.Dequeue())) : random.Next(1, Math.Max(2, sides) + 1); }
        public DieRoll Skill(int sides, int advantage, int modifier = 0)
        {
            var roll = new DieRoll { first = Roll(sides), second = 0, mode = Math.Sign(advantage) };
            if (roll.mode == 0) roll.result = roll.first + modifier;
            else { roll.second = Roll(sides); roll.result = (roll.mode > 0 ? Math.Max(roll.first, roll.second) : Math.Min(roll.first, roll.second)) + modifier; }
            return roll;
        }
    }

    public static class TacticalRules
    {
        public static float Distance(UnitData a, UnitData b, BoardInfo board) { return Distance(a.x, a.y, b.x, b.y, board); }
        public static float Distance(float ax, float ay, float bx, float by, BoardInfo board)
        {
            var dx = (ax - bx) / 100f * board.widthInches;
            var dy = (ay - by) / 100f * board.heightInches;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
        public static float MovementAllowance(UnitData unit, bool impaired)
        {
            var amount = unit.move;
            if (unit.status == "injured") amount *= .5f;
            if (unit.kind == "vehicle" && unit.mobilitySystem == "degraded") amount *= .5f;
            if (unit.kind == "vehicle" && unit.mobilitySystem == "disabled") amount = 0f;
            if (unit.sprint && unit.kind == "troop") amount *= 2f;
            if (impaired) amount *= .5f;
            return amount;
        }
        public static bool InExtractionZone(UnitData unit, ObjectiveData objective)
        {
            if (unit == null || objective == null) return false;
            var depth = Math.Max(1f, Math.Min(49f, objective.depth > 0f ? objective.depth : 15f));
            if (objective.edge == "north") return unit.y <= depth;
            if (objective.edge == "west") return unit.x <= depth;
            if (objective.edge == "east") return unit.x >= 100f - depth;
            return unit.y >= 100f - depth;
        }
        public static DetectionResult Detection(UnitData observer, string concealment, int difficulty, DeterministicDice dice)
        {
            var result = new DetectionResult();
            if (observer == null || concealment == "blocked") return result;
            result.attempted = true;
            if (concealment == "open") { result.detected = true; return result; }
            result.skill = dice.Skill(observer.skill, -1); result.detected = result.skill.result >= Math.Max(1, difficulty); return result;
        }
        public static AttackResult Attack(UnitData attacker, UnitData target, WeaponData weapon, BoardInfo board, int round, string cover, bool suppress, DeterministicDice dice, bool suppressionAimPossible = false, int skillModifier = 0, int skillSides = 0, string targetedSystem = "")
        {
            var result = new AttackResult { valid = false, reason = "Invalid attack." };
            if (attacker == null || target == null || weapon == null) { result.reason = "Select an attacker, target, and weapon."; return result; }
            if (target.status == "downed" || target.status == "dead") { result.reason = "That target is already out of the fight."; return result; }
            result.range = Distance(attacker, target, board);
            if (result.range > weapon.range) { result.reason = string.Format("Target is {0:0.0}\" away; range is {1:0.0}\".", result.range, weapon.range); return result; }
            if (target.defenseSides > 0 && weapon.damageSides < target.defenseSides && string.IsNullOrEmpty(targetedSystem)) { result.reason = string.Format("Damage d{0} cannot affect Defense d{1} armor without a targeted visible-system attack.", weapon.damageSides, target.defenseSides); return result; }
            if (cover == "blocked" && (!suppress || !suppressionAimPossible)) { result.reason = suppress ? "The attacker cannot aim within 6\" of the concealed target." : "Total cover or concealment blocks line of sight."; return result; }
            var hasAdvantage = !target.moved || target.observedBy == attacker.side || attacker.mainEffort;
            var hasDisadvantage = cover == "partial" || attacker.status == "injured" || attacker.suppressed || !string.IsNullOrEmpty(targetedSystem) || (attacker.kind == "vehicle" && (attacker.firepowerSystem == "degraded" || attacker.controlSystem == "disabled")) || (target.flying && !weapon.antiAir);
            var advantage = hasAdvantage == hasDisadvantage ? 0 : hasAdvantage ? 1 : -1;
            if (target.flying && !weapon.antiAir) advantage = -1;
            result.valid = true;
            result.skill = dice.Skill(skillSides > 0 ? skillSides : attacker.skill, advantage, skillModifier);
            var naturalSkill = result.skill.second == 0 ? result.skill.first : result.skill.mode > 0 ? Math.Max(result.skill.first, result.skill.second) : Math.Min(result.skill.first, result.skill.second);
            result.hit = naturalSkill != 1 && result.skill.result >= weapon.difficulty;
            if (!result.hit) return result;
            if (suppress) { result.suppressed = true; return result; }
            if (weapon.explosiveVariant == "smoke" || weapon.explosiveVariant == "illumination") return result;
            var damageDisadvantage = cover == "partial" && weapon.radius > 0 ? -1 : 0;
            result.damage = dice.Skill(weapon.damageSides, damageDisadvantage, weapon.damageModifier);
            result.defense = target.defenseSides > 0 ? target.defenseModifier : target.defense;
            for (var index = 0; index < Math.Max(1, target.defenseDiceCount) && target.defenseSides > 0; index++) result.defense += dice.Roll(target.defenseSides);
            result.casualty = result.damage.result >= result.defense;
            return result;
        }

        public static bool WithinFanCone(UnitData attacker, UnitData primary, UnitData candidate, BoardInfo board)
        {
            if (attacker == null || primary == null || candidate == null) return false;
            var ax = (primary.x - attacker.x) * board.widthInches / 100f; var ay = (primary.y - attacker.y) * board.heightInches / 100f;
            var bx = (candidate.x - attacker.x) * board.widthInches / 100f; var by = (candidate.y - attacker.y) * board.heightInches / 100f;
            var amag = Math.Sqrt(ax * ax + ay * ay); var bmag = Math.Sqrt(bx * bx + by * by); if (amag <= .001 || bmag <= .001) return true;
            var angle = Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by) / (amag * bmag)))) * 180.0 / Math.PI;
            return angle <= 22.5;
        }
        public static string Medicine(UnitData medic, DeterministicDice dice, out DieRoll roll)
        {
            roll = dice.Skill(medic.medicalSkill > 0 ? medic.medicalSkill : medic.skill, medic.suppressed || medic.status == "injured" ? -1 : 0);
            if (roll.result <= 2) return "dead";
            if (roll.result <= 4) return "downed";
            if (roll.result <= 7) return "injured";
            return "healthy";
        }
    }
}
