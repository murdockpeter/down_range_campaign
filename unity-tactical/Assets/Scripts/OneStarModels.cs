using System;

namespace DownRange.Tactical
{
    [Serializable] public class OneStarDefinition
    {
        public string id; public string title; public string rulesVersion; public float widthInches; public float depthInches;
        public OneStarMode[] modes; public OneStarLocation[] locations; public OneStarEvent[] timeline; public OneStarForce[] forces;
    }

    [Serializable] public class OneStarMode
    {
        public string id; public string name; public int finalRound; public string objective;
    }

    [Serializable] public class OneStarLocation
    {
        public string id; public string name; public string grid; public string kind; public string description;
        public float x; public float z; public float width; public float depth; public int floors; public bool discoverable;
    }

    [Serializable] public class OneStarEvent
    {
        public int round; public string title; public string summary;
    }

    [Serializable] public class OneStarForce
    {
        public string id; public string side; public string name; public string role; public string kind;
        public float x; public float z; public float move; public int arrivalRound; public bool hidden;
    }

    [Serializable] public class OneStarSavedUnit
    {
        public string id; public float x; public float z; public float yaw; public string status;
    }

    [Serializable] public class OneStarSave
    {
        public int version = 1; public int round = 1; public string mode = "narrative"; public string selectedId;
        public OneStarSavedUnit[] units;
    }
}
