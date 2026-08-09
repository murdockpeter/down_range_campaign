using System;
using System.IO;
using DownRange.Tactical;
using UnityEngine;

namespace DownRange.Editor
{
    public static class ValidateGame
    {
        public static void PerformValidation()
        {
            var board = new BoardInfo { widthInches = 64f, heightInches = 42.6667f };
            var healthy = new UnitData { id = "a", kind = "troop", move = 8f, skill = 6, defense = 4, status = "healthy", x = 0, y = 0 };
            var injured = new UnitData { id = "b", kind = "troop", move = 8f, skill = 6, defense = 4, status = "injured", sprint = true, x = 10, y = 0 };
            Require(Math.Abs(TacticalRules.Distance(healthy, injured, board) - 6.4f) < .001f, "Board distance conversion failed.");
            Require(Math.Abs(TacticalRules.MovementAllowance(injured, true) - 4f) < .001f, "Movement modifiers failed.");
            var weapon = new WeaponData { id = "m4", name = "M4", range = 5f, difficulty = 3, damageSides = 6 };
            var attack = TacticalRules.Attack(healthy, injured, weapon, board, 1, "open", false, new DeterministicDice(42));
            Require(!attack.valid && attack.reason.Contains("range"), "Weapon range validation failed.");
            var samplePath = Path.Combine(Application.dataPath, "StreamingAssets", "sample-battle-request.json");
            var request = JsonUtility.FromJson<BattleRequest>(File.ReadAllText(samplePath));
            Require(request != null && request.contractVersion == 1 && request.units.Length >= 4, "Sample battle contract failed to deserialize.");
            var spritePath = Path.Combine(Application.dataPath, "StreamingAssets", "Sprites");
            foreach (var name in new[] { "infantry.png", "medic.png", "uas.png" })
            {
                var path = Path.Combine(spritePath, name);
                Require(File.Exists(path) && new FileInfo(path).Length > 1000, "Missing or empty runtime sprite: " + name);
            }
            var oneStarPath = Path.Combine(Application.dataPath, "StreamingAssets", "one-star-scenario.json");
            var oneStar = JsonUtility.FromJson<OneStarDefinition>(File.ReadAllText(oneStarPath));
            Require(oneStar != null && oneStar.widthInches == 72f && oneStar.depthInches == 60f, "One Star board definition is invalid.");
            Require(oneStar.locations != null && oneStar.locations.Length >= 11, "One Star landmark data is incomplete.");
            foreach (var location in oneStar.locations)
            {
                foreach (var roadX in new[] { 27f, 51f, 63f })
                    Require(Math.Abs(location.x - roadX) > location.width / 2f + 2.05f, location.name + " overlaps a north-south road corridor.");
                foreach (var roadZ in new[] { 15f, 36f, 47f })
                    Require(Math.Abs(location.z - roadZ) > location.depth / 2f + 1.85f, location.name + " overlaps an east-west road corridor.");
            }
            for (var first = 0; first < oneStar.locations.Length; first++)
                for (var second = first + 1; second < oneStar.locations.Length; second++)
                {
                    var a = oneStar.locations[first]; var b = oneStar.locations[second];
                    var separated = Math.Abs(a.x - b.x) >= (a.width + b.width) / 2f || Math.Abs(a.z - b.z) >= (a.depth + b.depth) / 2f;
                    Require(separated, a.name + " overlaps the parcel for " + b.name + ".");
                }
            Require(oneStar.timeline != null && oneStar.timeline.Length == 12, "One Star narrative timeline must contain all twelve rounds.");
            Require(oneStar.forces != null && oneStar.forces.Length >= 10, "One Star force schedule is incomplete.");
            foreach (var model in new[] { "USMC Rifleman", "USMC Officer", "USMC Corpsman", "PLANMC Rifleman", "PLANMC ZBL-09", "PLANMC Mortar Team", "Generic Quadcopter" })
            {
                Require(Resources.Load<GameObject>("Models/OneStar/" + model) != null, "One Star playtest model failed to import: " + model);
                Require(Resources.Load<Texture2D>("Models/OneStar/" + model + " Texture") != null, "One Star playtest texture failed to import: " + model);
            }
            foreach (var model in new[] { "USMC EW Operator", "LPM Officer", "LPM Rifleman", "LPM Automatic Rifleman", "LPM Mortar Team", "MOUT Small Ground Floor", "MOUT Small Upper Floor", "MOUT Small Roof", "MOUT Medium Ground Floor", "MOUT Medium Upper Floor", "MOUT Medium Roof" })
                Require(Resources.Load<GameObject>("Models/OneStar/" + model) != null, "One Star downloaded model failed to import: " + model);
            Require(Resources.Load<Texture2D>("Models/OneStar/LPM Field Camo Texture") != null, "The generated LPM faction paint is missing.");
            Require(Resources.Load<Texture2D>("Models/OneStar/USMC Field Camo Texture") != null, "The generated USMC faction paint is missing.");
            Require(Resources.Load<Shader>("Shaders/OneStarTriplanarPaint") != null, "The One Star triplanar paint shader is missing.");
            Debug.Log("Down Range tactical validation passed.");
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
