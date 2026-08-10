using System;

namespace DownRange.Tactical
{
    [Serializable] public class CampaignInfo { public string name; public string slug; public int turn; public string date; }
    [Serializable] public class MissionIntent { public string purpose; public string method; public string endState; }
    [Serializable] public class MissionInfo { public int number; public string title; public string type; public string locationId; public string locationName; public string terrain; public int durationTurns; public string situation; public MissionIntent intent; }
    [Serializable] public class TerrainInfo
    {
        public string locationId; public string locationName; public string description; public string archetype; public int seed;
        public float gridCellSize = 1f; public int smoothingPasses = 3; public TerrainCellData[] cells;
        public float elevation; public float treeDensity; public float buildingDensity; public float water; public float wetGround;
        public string roadPattern; public string woodland; public string[] features;
    }
    [Serializable] public class TerrainCellData { public int x; public int y; public string type; public float elevation; }
    [Serializable] public class BoardInfo { public string mapPath; public float widthInches = 64f; public float heightInches = 42.6667f; public float pixelsPerInch = 24f; public TerrainInfo terrain; }
    [Serializable] public class BattleSettings { public string mode = "hotseat"; public int seed = 1; public bool autosave = true; }
    [Serializable] public class WeaponData
    {
        public string id; public string name; public float range; public int difficulty; public int damageSides;
        public int damageModifier; public int fan = 1; public float radius; public int ammunition = -1;
    }
    [Serializable] public class UnitData
    {
        public string id; public string side; public string name; public string role; public string forceId; public string kind = "troop"; public string modelId;
        public float x; public float y; public float move = 8f; public int skill = 6; public int medicalSkill; public int defense = 4;
        public float facing; public bool facingSet;
        public string status = "healthy"; public bool radio; public bool flying; public bool ew; public WeaponData[] weapons;
        public bool actionUsed; public bool moved; public int movesMade; public bool reaction; public bool reactionMove; public bool focused; public bool sprint; public bool suppressed;
        public string suppressedBySide; public string observedBy; public int observedRound;
    }
    [Serializable] public class ObjectiveData { public string id; public string text; public int points; public bool complete; }
    [Serializable] public class BattleRequest
    {
        public int contractVersion; public string requestId; public string createdAt; public string rulesVersion;
        public CampaignInfo campaign; public MissionInfo mission; public BoardInfo board; public BattleSettings settings;
        public ObjectiveData[] objectives; public UnitData[] units;
    }
    [Serializable] public class BattleEvent { public int round; public string text; public string kind; }
    [Serializable] public class BattleState
    {
        public string requestId; public int round = 1; public string activeSide = "blue"; public string firstSide = "blue";
        public bool firstSideFinished; public int blueInitiative; public int redInitiative; public bool alarm;
        public int observationTurns; public bool impairedMovement; public bool completed; public int rollCount;
        public string selectedId; public string targetId; public string cover = "open";
        public UnitData[] units; public ObjectiveData[] objectives; public BattleEvent[] events;
    }
    [Serializable] public class UnitResult { public string id; public float x; public float y; public float facing; public string status; }
    [Serializable] public class ObjectiveResult { public string id; public bool complete; }
    [Serializable] public class CasualtyResult { public string unitId; public string category; }
    [Serializable] public class BattleResult
    {
        public int contractVersion = 1; public string requestId; public string resultId; public string completedAt;
        public int missionNumber; public int rounds; public bool alarm; public int observationTurns;
        public int scoreEarned; public int scoreAvailable; public string outcome; public string terrainLocationId;
        public UnitResult[] units; public ObjectiveResult[] objectives; public CasualtyResult[] casualties; public BattleEvent[] events;
    }
    public struct DieRoll { public int first; public int second; public int result; public int mode; }
    public struct AttackResult
    {
        public bool valid; public string reason; public bool hit; public bool casualty; public bool suppressed;
        public DieRoll skill; public DieRoll damage; public int defense; public float range;
    }
}
