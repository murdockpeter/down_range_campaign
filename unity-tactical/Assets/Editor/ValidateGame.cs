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
            injured.x = 5f; injured.moved = false;
            var canceledModifiers = TacticalRules.Attack(healthy, injured, weapon, board, 1, "partial", false, new DeterministicDice(42));
            Require(canceledModifiers.valid && canceledModifiers.skill.mode == 0, "Advantage and disadvantage must cancel completely.");
            var blockedSuppression = TacticalRules.Attack(healthy, injured, weapon, board, 1, "blocked", true, new DeterministicDice(42));
            Require(!blockedSuppression.valid, "Suppression without a nearby aim point should remain invalid.");
            var aimedSuppression = TacticalRules.Attack(healthy, injured, weapon, board, 1, "blocked", true, new DeterministicDice(42), true);
            Require(aimedSuppression.valid, "Suppression should allow a blocked target when an aim point is within six inches.");
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
            Require(Resources.Load<Shader>("Shaders/DownRangeTerrainGrid") != null, "The one-inch terrain grid shader is missing.");
            Require(ImportedMiniatureFactory.ModelFor(new UnitData { side = "blue", role = "Combat lifesaver", medicalSkill = 8 }) == "USMC Corpsman", "Campaign medic model mapping failed.");
            Require(ImportedMiniatureFactory.ModelFor(new UnitData { side = "blue", role = "Scout team", weapons = new[] { new WeaponData { name = "M249" } } }) == "USMC M249 Gunner", "Campaign automatic rifleman model mapping failed.");
            Require(ImportedMiniatureFactory.ModelFor(new UnitData { side = "red", role = "Relay guard" }) == "LPM Rifleman", "Campaign opposition model mapping failed.");
            ValidateAudioCues();
            ValidateLineOfSight();
            Debug.Log("Down Range tactical validation passed.");
        }

        static void ValidateAudioCues()
        {
            var host = new GameObject("Audio cue validation fixture");
            try
            {
                var audio = new TacticalAudio(host);
                foreach (SoundCue cue in Enum.GetValues(typeof(SoundCue))) Require(audio.HasClip(cue), "Missing procedural audio clip for " + cue + ".");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        static void ValidateLineOfSight()
        {
            var root = new GameObject("LOS validation fixtures");
            try
            {
                var start = new Vector3(1000f, 2f, 0f); var end = new Vector3(1010f, 2f, 0f);
                Require(BattleLineOfSight.Evaluate(start, end).classification == "open", "Clear LOS classification failed.");
                var foliage = GameObject.CreatePrimitive(PrimitiveType.Cube); foliage.transform.SetParent(root.transform); foliage.transform.position = new Vector3(1004f, 2f, 0f);
                var foliageObstacle = foliage.AddComponent<BattleLosObstacle>(); foliageObstacle.label = "test foliage"; foliageObstacle.classification = "partial"; Physics.SyncTransforms();
                var partial = BattleLineOfSight.Evaluate(start, end); Require(partial.classification == "partial" && partial.blocker == "test foliage", "Partial LOS classification failed.");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(root.transform); wall.transform.position = new Vector3(1007f, 2f, 0f);
                var wallObstacle = wall.AddComponent<BattleLosObstacle>(); wallObstacle.label = "test building"; wallObstacle.classification = "blocked"; Physics.SyncTransforms();
                var blocked = BattleLineOfSight.Evaluate(start, end); Require(blocked.classification == "blocked" && blocked.blocker == "test building" && blocked.blockerDistance > 0f, "Blocked LOS classification failed.");
                var unitRoot = new GameObject("Test friendly - 3D campaign miniature"); unitRoot.transform.SetParent(root.transform); unitRoot.transform.position = new Vector3(1004f, 2f, 4f);
                var marker = unitRoot.AddComponent<CampaignMiniatureMarker>(); marker.unitId = "friendly";
                var unitBody = GameObject.CreatePrimitive(PrimitiveType.Cube); unitBody.transform.SetParent(unitRoot.transform); unitBody.transform.localPosition = Vector3.zero; Physics.SyncTransforms();
                var unitBlocked = BattleLineOfSight.Evaluate(new Vector3(1000f, 2f, 4f), new Vector3(1010f, 2f, 4f));
                Require(unitBlocked.classification == "blocked" && unitBlocked.blocker.Contains("intervening unit"), "Intervening miniature LOS classification failed.");
                Require(BattleLineOfSight.Evaluate(new Vector3(1000f, 2f, 5f), new Vector3(1010f, 2f, 5f)).classification == "open", "LOS incorrectly applies an invented one-inch unit corridor.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
