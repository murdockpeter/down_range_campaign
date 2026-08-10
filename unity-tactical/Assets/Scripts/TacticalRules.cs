using System;

namespace DownRange.Tactical
{
    public sealed class DeterministicDice
    {
        readonly Random random;
        public int RollCount { get; private set; }
        public DeterministicDice(int seed, int rollsToSkip = 0)
        {
            random = new Random(seed);
            for (var i = 0; i < rollsToSkip; i++) Roll(6);
        }
        public int Roll(int sides) { RollCount++; return random.Next(1, Math.Max(2, sides) + 1); }
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
            if (unit.sprint && unit.kind == "troop") amount *= 2f;
            if (impaired) amount *= .5f;
            return amount;
        }
        public static AttackResult Attack(UnitData attacker, UnitData target, WeaponData weapon, BoardInfo board, int round, string cover, bool suppress, DeterministicDice dice, bool suppressionAimPossible = false)
        {
            var result = new AttackResult { valid = false, reason = "Invalid attack." };
            if (attacker == null || target == null || weapon == null) { result.reason = "Select an attacker, target, and weapon."; return result; }
            result.range = Distance(attacker, target, board);
            if (result.range > weapon.range) { result.reason = string.Format("Target is {0:0.0}\" away; range is {1:0.0}\".", result.range, weapon.range); return result; }
            if (cover == "blocked" && (!suppress || !suppressionAimPossible)) { result.reason = suppress ? "The attacker cannot aim within 6\" of the concealed target." : "Total cover or concealment blocks line of sight."; return result; }
            var hasAdvantage = !target.moved || target.observedBy == attacker.side;
            var hasDisadvantage = cover == "partial" || attacker.status == "injured" || attacker.suppressed;
            var advantage = hasAdvantage == hasDisadvantage ? 0 : hasAdvantage ? 1 : -1;
            result.valid = true;
            result.skill = dice.Skill(attacker.skill, advantage);
            result.hit = result.skill.result >= weapon.difficulty;
            if (!result.hit) return result;
            if (suppress) { result.suppressed = true; return result; }
            var damageDisadvantage = cover == "partial" && weapon.radius > 0 ? -1 : 0;
            result.damage = dice.Skill(weapon.damageSides, damageDisadvantage, weapon.damageModifier);
            result.defense = target.defense;
            result.casualty = result.damage.result >= result.defense;
            return result;
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
