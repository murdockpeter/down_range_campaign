using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DownRange.Tactical
{
    public sealed class OneStarRuntime : MonoBehaviour
    {
        OneStarDefinition definition;
        readonly Dictionary<string, OneStarUnitMarker> markers = new Dictionary<string, OneStarUnitMarker>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        Camera tacticalCamera;
        TacticalAudio audio;
        Transform worldRoot;
        OneStarUnitMarker selected;
        LineRenderer losLine;
        Vector3 cameraFocus = new Vector3(36f, 0f, 30f);
        float cameraYaw;
        float cameraPitch = 62f;
        float cameraDistance = 72f;
        const float CameraMinimumDistance = 5f;
        const float CameraMaximumDistance = 115f;
        const float BuildingPadHeight = .04f;
        int round = 1;
        int modeIndex;
        bool facilitatorView;
        bool showHelp = true;
        bool losMode;
        bool losStartSet;
        Vector3 losStart;
        string losResult = "LOS tool ready.";
        string notice = "One Star 3D vertical slice loaded.";
        string savePath;
        Vector2 rosterScroll;
        GUIStyle panelStyle, titleStyle, headingStyle, smallStyle, bodyStyle, selectedButtonStyle, eventStyle;
        Texture2D pixel;

        void Awake()
        {
            Application.runInBackground = true;
            var commandLine = Environment.GetCommandLineArgs();
            var paintCheckBlue = commandLine.Any(argument => string.Equals(argument, "--paint-check-blue", StringComparison.OrdinalIgnoreCase));
            var paintCheckRed = commandLine.Any(argument => string.Equals(argument, "--paint-check-red", StringComparison.OrdinalIgnoreCase));
            showHelp = !commandLine.Any(argument => string.Equals(argument, "--no-help", StringComparison.OrdinalIgnoreCase)) && !paintCheckBlue && !paintCheckRed;
            cameraFocus = new Vector3(36f, 0f, 30f); cameraYaw = 0f; cameraPitch = 62f; cameraDistance = 72f;
            audio = new TacticalAudio(gameObject);
            pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
            savePath = Path.Combine(Application.persistentDataPath, "one-star-state-v1.json");
            LoadDefinition();
            LoadSavedHeader();
            if (paintCheckBlue)
            {
                round = 1; cameraFocus = new Vector3(7.2f, 0f, 53.5f); cameraYaw = 180f; cameraPitch = 24f; cameraDistance = 6f;
            }
            else if (paintCheckRed)
            {
                round = 12; cameraFocus = new Vector3(62f, 0f, 7f); cameraYaw = 0f; cameraPitch = 55f; cameraDistance = 13f;
            }
            BuildWorld();
            SpawnForces();
            RestoreUnitPositions();
            if (paintCheckBlue)
            {
                OneStarUnitMarker headquarters;
                if (markers.TryGetValue("us-hq", out headquarters))
                {
                    cameraFocus = headquarters.transform.position;
                    cameraYaw = headquarters.transform.eulerAngles.y + 180f;
                    cameraPitch = 24f;
                    cameraDistance = 5.5f;
                }
            }
            RefreshForceVisibility();
            UpdateCameraTransform();
        }

        void LoadDefinition()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "one-star-scenario.json");
            if (!File.Exists(path)) throw new FileNotFoundException("One Star scenario data is missing.", path);
            definition = JsonUtility.FromJson<OneStarDefinition>(File.ReadAllText(path));
            if (definition == null || definition.locations == null || definition.timeline == null) throw new InvalidDataException("One Star scenario data is invalid.");
        }

        OneStarSave ReadSave()
        {
            try { return File.Exists(savePath) ? JsonUtility.FromJson<OneStarSave>(File.ReadAllText(savePath)) : null; }
            catch (Exception error) { Debug.LogWarning("Unable to read One Star save: " + error.Message); return null; }
        }

        void LoadSavedHeader()
        {
            var saved = ReadSave(); if (saved == null || saved.version != 1) return;
            round = Mathf.Clamp(saved.round, 1, 12);
            var found = Array.FindIndex(definition.modes, mode => mode.id == saved.mode); if (found >= 0) modeIndex = found;
        }

        void RestoreUnitPositions()
        {
            var saved = ReadSave(); if (saved?.units == null) return;
            foreach (var item in saved.units)
            {
                OneStarUnitMarker marker;
                if (!markers.TryGetValue(item.id, out marker)) continue;
                marker.transform.position = new Vector3(item.x, 0f, item.z); marker.transform.rotation = Quaternion.Euler(0f, item.yaw, 0f); marker.status = string.IsNullOrEmpty(item.status) ? "ready" : item.status;
            }
            if (!string.IsNullOrEmpty(saved.selectedId) && markers.TryGetValue(saved.selectedId, out selected)) selected.SetSelected(true);
        }

        void Save()
        {
            var data = new OneStarSave
            {
                round = round, mode = definition.modes[modeIndex].id, selectedId = selected == null ? "" : selected.data.id,
                units = markers.Values.Select(marker => new OneStarSavedUnit { id = marker.data.id, x = marker.transform.position.x, z = marker.transform.position.z, yaw = marker.transform.eulerAngles.y, status = marker.status }).ToArray()
            };
            File.WriteAllText(savePath, JsonUtility.ToJson(data, true));
        }

        void BuildWorld()
        {
            worldRoot = new GameObject("Calloni 3D Tabletop").transform;
            RenderSettings.ambientLight = new Color(.48f, .50f, .45f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(.28f, .32f, .29f); RenderSettings.fogDensity = .006f;
            tacticalCamera = new GameObject("One Star Tactical Camera").AddComponent<Camera>();
            tacticalCamera.tag = "MainCamera"; tacticalCamera.clearFlags = CameraClearFlags.SolidColor; tacticalCamera.backgroundColor = new Color(.055f, .075f, .068f); tacticalCamera.orthographic = true; tacticalCamera.farClipPlane = 400f;
            var sun = new GameObject("Calloni Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.color = new Color(1f, .91f, .75f); sun.intensity = 1.15f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48f, -36f, 0f);
            var fill = new GameObject("Sky Fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.color = new Color(.43f, .58f, .68f); fill.intensity = .28f; fill.transform.rotation = Quaternion.Euler(65f, 145f, 0f);

            CreateBox("Calloni board", new Vector3(36f, -.35f, 30f), new Vector3(72f, .7f, 60f), MaterialFor("ground", new Color(.31f, .34f, .23f)), worldRoot);
            var wood = MaterialFor("wood", new Color(.17f, .10f, .055f));
            CreateBox("South table edge", new Vector3(36f, -.25f, -.65f), new Vector3(74f, 1.2f, 1.3f), wood, worldRoot);
            CreateBox("North table edge", new Vector3(36f, -.25f, 60.65f), new Vector3(74f, 1.2f, 1.3f), wood, worldRoot);
            CreateBox("West table edge", new Vector3(-.65f, -.25f, 30f), new Vector3(1.3f, 1.2f, 60f), wood, worldRoot);
            CreateBox("East table edge", new Vector3(72.65f, -.25f, 30f), new Vector3(1.3f, 1.2f, 60f), wood, worldRoot);
            BuildRoads(); BuildGrid(); BuildScenery();
            foreach (var location in definition.locations) BuildLocation(location);
            losLine = new GameObject("3D LOS line").AddComponent<LineRenderer>(); losLine.positionCount = 2; losLine.startWidth = .22f; losLine.endWidth = .22f; losLine.material = MaterialFor("los", new Color(.25f, 1f, .55f), true); losLine.enabled = false;
        }

        void BuildRoads()
        {
            var asphalt = MaterialFor("asphalt", new Color(.19f, .20f, .18f));
            foreach (var x in new[] { 27f, 51f, 63f }) CreateBox("North-south road", new Vector3(x, .025f, 30f), new Vector3(4.1f, .05f, 60f), asphalt, worldRoot, false);
            foreach (var z in new[] { 15f, 36f, 47f }) CreateBox("East-west road", new Vector3(36f, .03f, z), new Vector3(72f, .055f, 3.7f), asphalt, worldRoot, false);
            var roadLine = MaterialFor("road-line", new Color(.62f, .55f, .31f), true);
            foreach (var x in new[] { 27f, 51f, 63f }) CreateBox("Road centerline", new Vector3(x, .065f, 30f), new Vector3(.07f, .025f, 60f), roadLine, worldRoot, false);
        }

        void BuildGrid()
        {
            var grid = MaterialFor("grid", new Color(.58f, .61f, .45f, .24f), true);
            for (var x = 0f; x <= 72f; x += 6f) CreateWorldLine("Grid X", new Vector3(x, .075f, 0f), new Vector3(x, .075f, 60f), .035f, grid);
            for (var z = 0f; z <= 60f; z += 5f) CreateWorldLine("Grid Z", new Vector3(0f, .075f, z), new Vector3(72f, .075f, z), .035f, grid);
        }

        void BuildScenery()
        {
            var trunk = MaterialFor("trunk", new Color(.24f, .16f, .08f)); var leaves = MaterialFor("leaves", new Color(.13f, .25f, .12f));
            var random = new System.Random(1942);
            for (int attempt = 0, placed = 0; attempt < 240 && placed < 42; attempt++)
            {
                var x = (float)random.NextDouble() * 72f; var z = (float)random.NextDouble() * 60f;
                if (x > 4f && x < 68f && z > 4f && z < 56f && (attempt % 3 != 0)) continue;
                if (PositionReservedForRoadOrLocation(x, z, .7f)) continue;
                var root = new GameObject("Roadside vegetation").transform; root.SetParent(worldRoot); root.position = new Vector3(x, 0f, z);
                var stem = CreatePrimitive(PrimitiveType.Cylinder, "Tree trunk", new Vector3(0f, .75f, 0f), new Vector3(.28f, .75f, .28f), trunk, root, true); stem.AddComponent<OneStarObstacle>().label = "tree trunk";
                CreatePrimitive(PrimitiveType.Sphere, "Tree crown", new Vector3(0f, 2.1f, 0f), new Vector3(1.15f, .85f, 1.15f), leaves, root, false);
                placed++;
            }
        }

        bool PositionReservedForRoadOrLocation(float x, float z, float margin)
        {
            foreach (var roadX in new[] { 27f, 51f, 63f }) if (Mathf.Abs(x - roadX) <= 2.05f + margin) return true;
            foreach (var roadZ in new[] { 15f, 36f, 47f }) if (Mathf.Abs(z - roadZ) <= 1.85f + margin) return true;
            foreach (var location in definition.locations)
                if (Mathf.Abs(x - location.x) <= location.width / 2f + margin && Mathf.Abs(z - location.z) <= location.depth / 2f + margin) return true;
            return false;
        }

        void BuildLocation(OneStarLocation location)
        {
            if (location.kind == "rubble") { BuildRubble(location); return; }
            if (location.kind == "market") { BuildMarket(location); return; }
            CreateBuildingPad(location);
            if (location.id == "barrows") { BuildCompound(location, 6); return; }
            if (location.id == "warehouses") { BuildCompound(location, 3); return; }
            BuildBuilding(location, location.x, location.z, location.width * .76f, location.depth * .76f, location.floors);
        }

        void CreateBuildingPad(OneStarLocation location)
        {
            var pavement = MaterialFor("building-pavement", new Color(.39f, .40f, .37f));
            CreateBox(location.name + " paved lot", new Vector3(location.x, BuildingPadHeight / 2f, location.z), new Vector3(location.width, BuildingPadHeight, location.depth), pavement, worldRoot, false);
        }

        void BuildCompound(OneStarLocation location, int count)
        {
            var columns = count == 6 ? 3 : 1; var rows = Mathf.CeilToInt((float)count / columns);
            var pieceWidth = location.width / columns * .72f; var pieceDepth = location.depth / rows * .68f;
            for (var i = 0; i < count; i++)
            {
                var column = i % columns; var row = i / columns;
                var x = location.x - location.width / 2f + (column + .5f) * location.width / columns;
                var z = location.z - location.depth / 2f + (row + .5f) * location.depth / rows;
                BuildBuilding(location, x, z, pieceWidth, pieceDepth, location.floors);
            }
        }

        void BuildMarket(OneStarLocation location)
        {
            var pad = CreateBox(location.name + " terrain", new Vector3(location.x, BuildingPadHeight / 2f, location.z), new Vector3(location.width, BuildingPadHeight, location.depth), MaterialFor("market-ground", new Color(.37f, .31f, .21f)), worldRoot, false);
            for (var i = 0; i < 4; i++)
            {
                var x = location.x - location.width * .32f + (i % 2) * location.width * .64f; var z = location.z - location.depth * .22f + (i / 2) * location.depth * .44f;
                BuildBuilding(location, x, z, location.width * .24f, location.depth * .3f, 1);
            }
            CreateLabel(location.name + "  [" + location.grid + "]", new Vector3(location.x, 3.4f, location.z), new Color(.96f, .84f, .55f), pad.transform);
        }

        void BuildRubble(OneStarLocation location)
        {
            var root = CreateBox(location.name, new Vector3(location.x, .09f, location.z), new Vector3(location.width, .14f, location.depth), MaterialFor("rubble-ground", new Color(.28f, .25f, .20f)), worldRoot, false);
            var rubble = MaterialFor("rubble", new Color(.32f, .31f, .28f)); var random = new System.Random(8012);
            for (var i = 0; i < 28; i++)
            {
                var x = location.x + ((float)random.NextDouble() - .5f) * location.width * .85f; var z = location.z + ((float)random.NextDouble() - .5f) * location.depth * .85f;
                var chunk = CreateBox("Rubble", new Vector3(x, .25f, z), new Vector3(.5f + (float)random.NextDouble(), .25f + (float)random.NextDouble() * .4f, .5f + (float)random.NextDouble()), rubble, worldRoot);
                chunk.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, (float)random.NextDouble() * 18f); chunk.AddComponent<OneStarObstacle>().label = location.name + " rubble";
            }
            CreateLabel(location.name + "  [" + location.grid + "]", new Vector3(location.x, 2.1f, location.z), new Color(.96f, .78f, .48f), root.transform);
        }

        void BuildBuilding(OneStarLocation location, float x, float z, float width, float depth, int floors)
        {
            var root = new GameObject(location.name).transform; root.SetParent(worldRoot); root.position = new Vector3(x, BuildingPadHeight, z);
            var wallColor = location.id == "hotel" ? new Color(.68f, .60f, .43f) : location.id == "clinic" ? new Color(.65f, .69f, .61f) : location.kind == "warehouse" ? new Color(.43f, .47f, .44f) : new Color(.57f, .49f, .37f);
            var walls = MaterialFor("walls-" + location.id, wallColor); var trim = MaterialFor("trim", new Color(.18f, .20f, .18f));
            const float floorHeight = 1.9f;
            var importedFloorCount = Mathf.Max(1, floors);
            if (BuildImportedModularBuilding(location, root, width * .78f, depth * .78f, importedFloorCount, floorHeight, walls, trim))
            {
                var importedHeight = importedFloorCount * floorHeight;
                CreateLabel(location.name + "  [" + location.grid + "]", new Vector3(0f, importedHeight + 1.2f, 0f), location.id == "hotel" ? new Color(1f, .77f, .28f) : Color.white, root);
                return;
            }
            for (var floor = 0; floor < Mathf.Max(1, floors); floor++)
            {
                var slab = CreateBox(location.name + " floor " + (floor + 1), new Vector3(0f, floor * floorHeight + floorHeight / 2f, 0f), new Vector3(width, floorHeight - .08f, depth), walls, root);
                slab.AddComponent<OneStarObstacle>().label = location.name + " — floor " + (floor + 1);
                CreateBox("Floor trim", new Vector3(0f, floor * floorHeight + .13f, -depth / 2f - .025f), new Vector3(width + .08f, .16f, .08f), trim, root, false);
                var windowCount = Mathf.Clamp(Mathf.RoundToInt(width / 2.2f), 2, 6);
                for (var window = 0; window < windowCount; window++)
                {
                    var wx = -width * .38f + window * (width * .76f / Mathf.Max(1, windowCount - 1));
                    CreateBox("Window", new Vector3(wx, floor * floorHeight + 1.45f, -depth / 2f - .045f), new Vector3(.65f, .72f, .08f), MaterialFor("window", new Color(.08f, .14f, .15f)), root, false);
                }
            }
            var totalHeight = Mathf.Max(1, floors) * floorHeight;
            CreateBox("Roof", new Vector3(0f, totalHeight + .12f, 0f), new Vector3(width + .25f, .24f, depth + .25f), trim, root);
            CreateLabel(location.name + "  [" + location.grid + "]", new Vector3(0f, totalHeight + 1.2f, 0f), location.id == "hotel" ? new Color(1f, .77f, .28f) : Color.white, root);
        }

        bool BuildImportedModularBuilding(OneStarLocation location, Transform root, float width, float depth, int floors, float floorHeight, Material walls, Material trim)
        {
            var size = width >= 6.5f ? "Medium" : "Small";
            var resourceBase = "Models/OneStar/MOUT " + size + " ";
            var ground = Resources.Load<GameObject>(resourceBase + "Ground Floor");
            var upper = Resources.Load<GameObject>(resourceBase + "Upper Floor");
            var roof = Resources.Load<GameObject>(resourceBase + "Roof");
            if (ground == null || upper == null || roof == null) return false;

            for (var floor = 0; floor < floors; floor++)
            {
                var prefab = floor == 0 ? ground : upper;
                var section = Instantiate(prefab, root); section.name = location.name + " modular floor " + (floor + 1);
                FitImportedBuildingSection(section, width, floorHeight, depth, floor * floorHeight, walls);
                section.AddComponent<OneStarObstacle>().label = location.name + " — floor " + (floor + 1);
            }
            var roofSection = Instantiate(roof, root); roofSection.name = location.name + " modular roof";
            FitImportedBuildingSection(roofSection, width, .38f, depth, floors * floorHeight, trim);
            roofSection.AddComponent<OneStarObstacle>().label = location.name + " roof";
            return true;
        }

        void FitImportedBuildingSection(GameObject section, float width, float height, float depth, float baseHeight, Material material)
        {
            section.transform.localPosition = new Vector3(0f, baseHeight, 0f); section.transform.localRotation = Quaternion.identity; section.transform.localScale = Vector3.one;
            var renderers = section.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var index = 0; index < renderers.Length; index++) { bounds.Encapsulate(renderers[index].bounds); renderers[index].sharedMaterial = material; }
            section.transform.localScale = new Vector3(width / Mathf.Max(.01f, bounds.size.x), height / Mathf.Max(.01f, bounds.size.y), depth / Mathf.Max(.01f, bounds.size.z));
            AddImportedModelCollider(section);
        }

        void SpawnForces()
        {
            foreach (var force in definition.forces)
            {
                var root = new GameObject(force.name); root.transform.SetParent(worldRoot); root.transform.position = new Vector3(force.x, 0f, force.z);
                var marker = root.AddComponent<OneStarUnitMarker>(); marker.data = force; marker.status = "ready";
                var color = force.side == "blue" ? new Color(.10f, .56f, .78f) : force.side == "red" ? new Color(.76f, .16f, .12f) : new Color(.75f, .68f, .30f);
                var sideMaterial = MaterialFor("unit-" + force.side, color); var dark = MaterialFor("unit-dark", new Color(.09f, .11f, .095f));
                var imported = CreateImportedForceModels(force, root.transform);
                if (!imported) CreatePrimitive(PrimitiveType.Cylinder, "Fallback unit base", new Vector3(0f, .10f, 0f), new Vector3(.82f, .09f, .82f), dark, root.transform);
                if (!imported && force.kind == "vehicle")
                {
                    CreatePrimitive(PrimitiveType.Cube, "Vehicle hull", new Vector3(0f, .68f, 0f), new Vector3(1.65f, .52f, 2.35f), sideMaterial, root.transform);
                    CreatePrimitive(PrimitiveType.Cube, "Vehicle turret", new Vector3(0f, 1.12f, -.1f), new Vector3(1.05f, .36f, 1.0f), dark, root.transform);
                }
                else if (!imported && force.kind == "uas")
                {
                    CreatePrimitive(PrimitiveType.Sphere, "UAS body", new Vector3(0f, 1.5f, 0f), new Vector3(.7f, .22f, .7f), sideMaterial, root.transform);
                    CreateBox("UAS arms", new Vector3(0f, 1.5f, 0f), new Vector3(2.1f, .08f, .14f), dark, root.transform); CreateBox("UAS arms", new Vector3(0f, 1.5f, 0f), new Vector3(.14f, .08f, 2.1f), dark, root.transform);
                }
                else if (!imported)
                {
                    CreatePrimitive(PrimitiveType.Capsule, "Unit miniature", new Vector3(0f, 1.02f, 0f), new Vector3(.55f, .78f, .55f), sideMaterial, root.transform);
                }
                var footprint = SelectionFootprint(force);
                var ringHeight = force.kind == "vehicle" ? .045f : .145f;
                marker.selectionRing = MiniatureMarkerGeometry.CreateRing("Selection ring", root.transform, ringHeight, footprint + .018f, footprint + .07f, .018f, MaterialFor("selection", new Color(1f, .82f, .20f), true)); marker.selectionRing.SetActive(false);
                CreateLabel(force.name, new Vector3(0f, 2.55f, 0f), color, root.transform);
                markers[force.id] = marker;
            }
        }

        bool CreateImportedForceModels(OneStarForce force, Transform parent)
        {
            string[] modelNames;
            switch (force.id)
            {
                case "us-hq": modelNames = new[] { "USMC Officer", "USMC EW Operator" }; break;
                case "us-1": modelNames = new[] { "USMC Rifleman", "USMC Rifleman", "USMC Rifleman" }; break;
                case "us-2": modelNames = new[] { "USMC Rifleman", "USMC M249 Gunner", "USMC Rifleman" }; break;
                case "us-3": modelNames = new[] { "USMC Rifleman", "USMC Corpsman", "USMC Rifleman" }; break;
                case "us-weapons": modelNames = new[] { "USMC MAAWS Gunner", "USMC M249 Gunner" }; break;
                case "unknown-drones": modelNames = new[] { "Generic Quadcopter" }; break;
                case "lpm-light": modelNames = new[] { "LPM Officer", "LPM Rifleman", "LPM Automatic Rifleman" }; break;
                case "lpm-mortars": modelNames = new[] { "LPM Mortar Team" }; break;
                case "lpm-uas": modelNames = new[] { "Generic Armed Quadcopter" }; break;
                case "lpm-mech-a": case "lpm-mech-b": case "lpm-reinforcement": modelNames = new[] { "PLANMC ZBL-09" }; break;
                default: modelNames = force.side == "blue" ? new[] { "USMC Rifleman" } : force.side == "red" ? new[] { "PLANMC Rifleman" } : new string[0]; break;
            }
            return ImportedMiniatureFactory.CreateModels(modelNames, force.side, force.kind, parent);
        }

        float SelectionFootprint(OneStarForce force)
        {
            if (force.kind == "vehicle") return 1.25f;
            if (force.kind == "uas") return .58f;
            switch (force.id)
            {
                case "us-1": case "us-2": case "us-3": case "lpm-light": return 1.02f;
                case "us-hq": case "us-weapons": return .78f;
                default: return .46f;
            }
        }

        void AddImportedModelCollider(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds; for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            var collider = model.AddComponent<BoxCollider>(); collider.center = model.transform.InverseTransformPoint(bounds.center);
            var localSize = model.transform.InverseTransformVector(bounds.size); collider.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        }

        Material MaterialFor(string id, Color color, bool unlit = false)
        {
            Material material; if (materials.TryGetValue(id, out material)) return material;
            var shader = Shader.Find(unlit ? "Unlit/Color" : "Standard") ?? Shader.Find("Legacy Shaders/Diffuse"); material = new Material(shader) { color = color, name = id };
            if (!unlit && material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .18f);
            materials[id] = material; return material;
        }

        GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material, Transform parent, bool collider = true)
        {
            return CreatePrimitive(PrimitiveType.Cube, name, position, scale, material, parent, collider);
        }

        GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Transform parent, bool collider = true)
        {
            var item = GameObject.CreatePrimitive(type); item.name = name; item.transform.SetParent(parent); item.transform.localPosition = position; item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) { var collision = item.GetComponent<Collider>(); if (collision != null) Destroy(collision); }
            return item;
        }

        void CreateWorldLine(string name, Vector3 start, Vector3 end, float width, Material material)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>(); line.transform.SetParent(worldRoot); line.positionCount = 2; line.SetPosition(0, start); line.SetPosition(1, end); line.startWidth = width; line.endWidth = width; line.sharedMaterial = material;
        }

        void CreateLabel(string text, Vector3 localPosition, Color color, Transform parent)
        {
            var label = new GameObject(text + " label"); label.transform.SetParent(parent); label.transform.localPosition = localPosition;
            var mesh = label.AddComponent<TextMesh>(); mesh.text = text; mesh.fontSize = 32; mesh.characterSize = .085f; mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = color;
            label.AddComponent<OneStarBillboard>();
        }

        void Update()
        {
            if (definition == null) return;
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; audio.Play(SoundCue.Click); }
            if (showHelp) { if (Input.GetKeyDown(KeyCode.Escape)) showHelp = false; return; }
            if (Input.GetKeyDown(KeyCode.L)) ToggleLos();
            if (Input.GetKeyDown(KeyCode.Escape) && losMode) ToggleLos();
            UpdateCameraInput();
            if (!PointerOverUi()) HandleWorldInput();
        }

        bool PointerOverUi()
        {
            var point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return point.y < 66f || point.x < 282f || point.x > Screen.width - 310f;
        }

        void UpdateCameraInput()
        {
            var speed = 18f * Time.unscaledDeltaTime * Mathf.Lerp(.15f, 1.6f, Mathf.InverseLerp(CameraMinimumDistance, CameraMaximumDistance, cameraDistance));
            var horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            var vertical = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            cameraFocus += new Vector3(horizontal, 0f, vertical) * speed;
            if (Input.GetKey(KeyCode.Q)) cameraYaw -= 52f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.E)) cameraYaw += 52f * Time.unscaledDeltaTime;
            if (Input.GetMouseButton(2))
            {
                cameraYaw += Input.GetAxis("Mouse X") * 3.2f;
                cameraPitch += Input.GetAxis("Mouse Y") * 2.8f;
            }
            var zoomStep = Mathf.Lerp(.65f, 6f, Mathf.InverseLerp(CameraMinimumDistance, CameraMaximumDistance, cameraDistance));
            cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y * zoomStep, CameraMinimumDistance, CameraMaximumDistance);
            cameraFocus.x = Mathf.Clamp(cameraFocus.x, 0f, 72f); cameraFocus.z = Mathf.Clamp(cameraFocus.z, 0f, 60f);
            UpdateCameraTransform();
        }

        void UpdateCameraTransform()
        {
            if (tacticalCamera == null) return;
            var viewportLeft = 282f / Mathf.Max(1f, Screen.width); var viewportBottom = 28f / Mathf.Max(1f, Screen.height);
            var viewportWidth = Mathf.Max(200f, Screen.width - 592f) / Mathf.Max(1f, Screen.width); var viewportHeight = Mathf.Max(200f, Screen.height - 94f) / Mathf.Max(1f, Screen.height);
            tacticalCamera.rect = new Rect(viewportLeft, viewportBottom, viewportWidth, viewportHeight);
            tacticalCamera.orthographicSize = Mathf.Clamp(cameraDistance * .49f, 2.45f, 58f);
            var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f); tacticalCamera.transform.position = cameraFocus + rotation * new Vector3(0f, 0f, -cameraDistance); tacticalCamera.transform.LookAt(cameraFocus + Vector3.up * 1.2f);
        }

        void HandleWorldInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                RaycastHit hit; if (!Physics.Raycast(tacticalCamera.ScreenPointToRay(Input.mousePosition), out hit, 300f)) return;
                if (losMode) { CaptureLosPoint(hit); return; }
                SelectMarker(hit.collider.GetComponentInParent<OneStarUnitMarker>());
            }
            if (Input.GetMouseButtonDown(1) && selected != null && !losMode)
            {
                RaycastHit hit; if (!Physics.Raycast(tacticalCamera.ScreenPointToRay(Input.mousePosition), out hit, 300f)) return;
                var obstacle = hit.collider.GetComponentInParent<OneStarObstacle>(); if (obstacle != null) { notice = "Movement into " + obstacle.label + " requires the future interior/floor movement pass."; audio.Play(SoundCue.Error); return; }
                var destination = hit.point; destination.y = 0f; var distance = HorizontalDistance(selected.transform.position, destination);
                if (distance > selected.data.move + .05f) { notice = string.Format("Move is {0:0.0}\"; {1} can move {2:0.#}\".", distance, selected.data.name, selected.data.move); audio.Play(SoundCue.Error); return; }
                var movement = destination - selected.transform.position; movement.y = 0f;
                if (movement.sqrMagnitude > .0001f) selected.transform.rotation = Quaternion.LookRotation(movement.normalized, Vector3.up);
                selected.transform.position = destination; notice = string.Format("{0} moves {1:0.0}\".", selected.data.name, distance); audio.Play(SoundCue.Move); Save();
            }
        }

        void SelectMarker(OneStarUnitMarker marker)
        {
            if (selected != null) selected.SetSelected(false); selected = marker;
            if (selected != null) { selected.SetSelected(true); notice = selected.data.name + " selected. Right-click the tabletop to move."; audio.Play(SoundCue.Click); }
            Save();
        }

        void ToggleLos()
        {
            losMode = !losMode; losStartSet = false; losLine.enabled = false; losResult = losMode ? "Click the LOS origin, then the target." : "LOS tool closed."; audio.Play(SoundCue.Click);
        }

        void CaptureLosPoint(RaycastHit hit)
        {
            var marker = hit.collider.GetComponentInParent<OneStarUnitMarker>(); var point = marker == null ? hit.point + Vector3.up * 1.55f : marker.transform.position + Vector3.up * 1.55f;
            if (!losStartSet) { losStart = point; losStartSet = true; losResult = "LOS origin set. Click the target."; audio.Play(SoundCue.Click); return; }
            ResolveLos(losStart, point, marker); losStartSet = false; audio.Play(SoundCue.Click);
        }

        void ResolveLos(Vector3 start, Vector3 end, OneStarUnitMarker endMarker)
        {
            var direction = end - start; var distance = direction.magnitude; var hits = Physics.RaycastAll(start, direction.normalized, distance).OrderBy(hit => hit.distance);
            string blocker = null;
            foreach (var hit in hits)
            {
                var marker = hit.collider.GetComponentInParent<OneStarUnitMarker>(); if (marker != null && (marker == selected || marker == endMarker)) continue;
                var obstacle = hit.collider.GetComponentInParent<OneStarObstacle>();
                if (obstacle != null) { blocker = obstacle.label; break; }
                if (marker != null) { blocker = "intervening unit " + marker.data.name; break; }
            }
            var clear = blocker == null; losLine.enabled = true; losLine.SetPosition(0, start); losLine.SetPosition(1, end); losLine.sharedMaterial = MaterialFor(clear ? "los" : "los-blocked", clear ? new Color(.25f, 1f, .55f) : new Color(1f, .23f, .14f), true);
            losResult = clear ? string.Format("CLEAR · {0:0.0}\"", HorizontalDistance(start, end)) : string.Format("BLOCKED · {0}\nRange {1:0.0}\"", blocker, HorizontalDistance(start, end));
        }

        float HorizontalDistance(Vector3 a, Vector3 b) { var dx = b.x - a.x; var dz = b.z - a.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        void AdvanceRound(int delta)
        {
            var finalRound = definition.modes[modeIndex].finalRound; round = Mathf.Clamp(round + delta, 1, finalRound); RefreshForceVisibility(); notice = CurrentEvent().title + ": " + CurrentEvent().summary; audio.Play(SoundCue.Turn); Save();
        }

        void RefreshForceVisibility()
        {
            foreach (var marker in markers.Values)
            {
                var arrived = marker.data.arrivalRound <= round; marker.gameObject.SetActive(arrived && (!marker.data.hidden || facilitatorView));
            }
        }

        OneStarEvent CurrentEvent() { return definition.timeline.FirstOrDefault(item => item.round == round) ?? definition.timeline.Last(); }

        void ResetScenario()
        {
            round = 1; modeIndex = 0; facilitatorView = false; SelectMarker(null);
            foreach (var marker in markers.Values) { marker.transform.position = new Vector3(marker.data.x, 0f, marker.data.z); marker.status = "ready"; }
            RefreshForceVisibility(); notice = "One Star reset to Round 1."; Save();
        }

        void BuildStyles()
        {
            if (panelStyle != null) return;
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10), normal = { background = Solid(new Color(.035f, .055f, .047f, .96f)) } };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.88f, .92f, .85f) } };
            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(.89f, .72f, .34f) } };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, wordWrap = true, normal = { textColor = new Color(.53f, .62f, .56f) } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(.78f, .83f, .78f) } };
            selectedButtonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, .82f, .34f) } };
            eventStyle = new GUIStyle(GUI.skin.box) { fontSize = 11, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(9, 9, 8, 8), normal = { background = Solid(new Color(.12f, .14f, .10f, .94f)), textColor = new Color(.88f, .88f, .77f) } };
        }

        Texture2D Solid(Color color) { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }

        void OnGUI()
        {
            BuildStyles(); if (definition == null) return;
            GUI.Box(new Rect(0, 0, Screen.width, 66), GUIContent.none, panelStyle);
            GUI.Label(new Rect(14, 8, 560, 25), "ONE STAR · BATTLE FOR CALLONI", titleStyle);
            GUI.Label(new Rect(16, 36, 700, 18), string.Format("3D TABLETOP PREVIEW · ROUND {0} · {1} · DOWN RANGE {2}", round, definition.modes[modeIndex].name.ToUpperInvariant(), definition.rulesVersion), smallStyle);
            if (GUI.Button(new Rect(Screen.width - 390, 16, 90, 34), audio.Enabled ? "SOUND" : "MUTED")) { audio.Enabled = !audio.Enabled; audio.Play(SoundCue.Click); }
            if (GUI.Button(new Rect(Screen.width - 292, 16, 92, 34), "HELP · F1")) { showHelp = true; audio.Play(SoundCue.Click); }
            if (GUI.Button(new Rect(Screen.width - 192, 16, 176, 34), "QUIT TO TRACKER")) Application.Quit();
            if (showHelp) { DrawHelp(); return; }
            DrawScenarioPanel(new Rect(0, 66, 282, Screen.height - 66));
            DrawControlPanel(new Rect(Screen.width - 310, 66, 310, Screen.height - 66));
            GUI.Label(new Rect(292, Screen.height - 28, Screen.width - 620, 20), "ONE STAR © NICHOLAS ROYER · CC BY-NC-SA 4.0 · PRIVATE NONCOMMERCIAL ADAPTATION", smallStyle);
        }

        void DrawScenarioPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16));
            GUILayout.Label("SCENARIO MODE", smallStyle);
            var names = definition.modes.Select(mode => mode.name).ToArray(); var nextMode = GUILayout.SelectionGrid(modeIndex, names, 1);
            if (nextMode != modeIndex) { modeIndex = nextMode; round = Mathf.Min(round, definition.modes[modeIndex].finalRound); notice = definition.modes[modeIndex].objective; Save(); }
            GUILayout.Space(7); GUILayout.Label("MISSION", headingStyle); GUILayout.Label(definition.modes[modeIndex].objective, bodyStyle);
            GUILayout.Space(8); GUILayout.Label("ROUND " + round + " · " + CurrentEvent().title.ToUpperInvariant(), headingStyle); GUILayout.Box(CurrentEvent().summary, eventStyle, GUILayout.MinHeight(78));
            GUILayout.BeginHorizontal(); GUI.enabled = round > 1; if (GUILayout.Button("← PREVIOUS")) AdvanceRound(-1); GUI.enabled = round < definition.modes[modeIndex].finalRound; if (GUILayout.Button("NEXT ROUND →")) AdvanceRound(1); GUI.enabled = true; GUILayout.EndHorizontal();
            GUILayout.Space(8); GUILayout.Label("FORCES IN PLAY", smallStyle); rosterScroll = GUILayout.BeginScrollView(rosterScroll);
            foreach (var marker in markers.Values.Where(item => item.data.arrivalRound <= round && (!item.data.hidden || facilitatorView)).OrderBy(item => item.data.side).ThenBy(item => item.data.name))
            {
                var label = marker.data.name + "\n" + marker.data.role; if (GUILayout.Button(label, marker == selected ? selectedButtonStyle : GUI.skin.button, GUILayout.Height(42))) SelectMarker(marker);
            }
            GUILayout.EndScrollView(); GUILayout.Space(5); if (GUILayout.Button("RESET ONE STAR")) ResetScenario(); GUILayout.EndArea();
        }

        void DrawControlPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 11, rect.y + 9, rect.width - 22, rect.height - 18));
            GUILayout.Label("3D TACTICAL CONTROL", smallStyle);
            if (selected == null) { GUILayout.Label("No unit selected", titleStyle); GUILayout.Label("Click a visible miniature or select one from the force roster.", bodyStyle); }
            else
            {
                GUILayout.Label(selected.data.name, titleStyle); GUILayout.Label(selected.data.side.ToUpperInvariant() + " · " + selected.data.role, smallStyle);
                GUILayout.Box(string.Format("MOVE {0:0.#}\"\nPOSITION {1:0.0}, {2:0.0}\nSTATUS {3}", selected.data.move, selected.transform.position.x, selected.transform.position.z, selected.status.ToUpperInvariant()), eventStyle);
                GUILayout.Label("Right-click terrain within movement allowance to reposition this marker.", bodyStyle);
            }
            GUILayout.Space(10); GUILayout.Label("LINE OF SIGHT", headingStyle);
            if (GUILayout.Button(losMode ? "LOS TOOL · ON" : "LOS TOOL · OFF", GUILayout.Height(30))) ToggleLos();
            GUILayout.Box(losResult, eventStyle, GUILayout.MinHeight(48));
            GUILayout.Space(8); GUILayout.Label("NARRATIVE INFORMATION", headingStyle);
            var nextFacilitator = GUILayout.Toggle(facilitatorView, " Facilitator view — reveal due hidden forces"); if (nextFacilitator != facilitatorView) { facilitatorView = nextFacilitator; RefreshForceVisibility(); audio.Play(SoundCue.Click); }
            GUILayout.Label(facilitatorView ? "Hidden contacts due this round are visible for adjudication." : "Hidden scenario contacts remain off the tabletop until revealed.", smallStyle);
            GUILayout.Space(10); GUILayout.Label("CAMERA", headingStyle); GUILayout.Label("WASD / arrows · fixed compass pan\nQ / E · rotate\nMiddle-drag sideways · rotate\nMiddle-drag vertically · unrestricted tilt / skew\nMouse wheel · tabletop-to-miniature zoom", bodyStyle);
            GUILayout.Label(string.Format("Focus {0:0.0}, {1:0.0} · Zoom {2:0} · Tilt {3:0}°", cameraFocus.x, cameraFocus.z, cameraDistance, cameraPitch), smallStyle);
            if (GUILayout.Button("CENTER ON CALLONI")) { cameraFocus = new Vector3(36f, 0f, 30f); cameraYaw = 0f; cameraPitch = 62f; cameraDistance = 72f; }
            GUILayout.Space(10); GUILayout.Label("STATUS", headingStyle); GUILayout.Label(notice, bodyStyle); GUILayout.FlexibleSpace();
            GUILayout.Label("Procedural scenery and licensed Down Range playtest miniatures establish the playable 3D architecture. Modular building art, interiors, combat actions, AI, and discovery resolution are subsequent implementation passes.", smallStyle);
            GUILayout.EndArea();
        }

        void DrawHelp()
        {
            var old = GUI.color; GUI.color = new Color(0f, 0f, 0f, .74f); GUI.DrawTexture(new Rect(0, 66, Screen.width, Screen.height - 66), pixel); GUI.color = old;
            var width = Mathf.Min(720f, Screen.width - 50f); var height = Mathf.Min(650f, Screen.height - 100f); var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f + 20f, width, height);
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 25, rect.y + 20, rect.width - 50, rect.height - 40));
            GUILayout.BeginHorizontal(); GUILayout.Label("ONE STAR · 3D TABLETOP", titleStyle); GUILayout.FlexibleSpace(); if (GUILayout.Button("CLOSE ×", GUILayout.Width(100), GUILayout.Height(30))) { showHelp = false; audio.Play(SoundCue.Click); } GUILayout.EndHorizontal();
            GUILayout.Space(10); GUILayout.Label("This is the first playable 3D foundation for the complete One Star module. The Calloni board is built at one Unity unit per tabletop inch, so movement, weapon range, blast radii, and LOS retain the printed scenario scale.", bodyStyle);
            GUILayout.Space(9); GUILayout.Label("SELECT AND MOVE", headingStyle); GUILayout.Label("Left-click a miniature to select it. Right-click the tabletop within its movement rating to reposition it. Buildings currently block outdoor movement; enterable floors and stairs are the next building-system pass.", bodyStyle);
            GUILayout.Space(9); GUILayout.Label("3D LINE OF SIGHT", headingStyle); GUILayout.Label("Press L or use the LOS button, then click an origin and target. Unity raycasts against building floors, rubble, tree trunks, vehicles, and intervening units. A green line is clear; a red line names the first blocker.", bodyStyle);
            GUILayout.Space(9); GUILayout.Label("SCENARIO DIRECTOR", headingStyle); GUILayout.Label("Choose narrative or force-on-force objectives on the left. Advancing rounds presents the official twelve-round narrative structure and introduces scheduled forces. Facilitator view reveals contacts that would normally remain hidden.", bodyStyle);
            GUILayout.Space(9); GUILayout.Label("CAMERA", headingStyle); GUILayout.Label("WASD or arrow keys pan by fixed map direction: W north, S south, A west, and D east, regardless of camera rotation. Q/E rotates horizontally. Hold the middle mouse button and drag sideways to rotate or vertically for unrestricted tilt/skew, including through the horizon and up toward the sky. The wheel zooms from a full-table view down to a close miniature view; close-up panning automatically slows for precise framing. F1 toggles this guide.", bodyStyle);
            GUILayout.FlexibleSpace(); GUILayout.Label("One Star and imported Down Range models © Nicholas Royer · Adapted under CC BY-NC-SA 4.0 for private, noncommercial use.", smallStyle); GUILayout.EndArea();
        }
    }

    public sealed class OneStarObstacle : MonoBehaviour { public string label; }

    public sealed class OneStarUnitMarker : MonoBehaviour
    {
        public OneStarForce data; public string status; public GameObject selectionRing;
        public void SetSelected(bool value) { if (selectionRing != null) selectionRing.SetActive(value); }
    }

    public sealed class OneStarBillboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var camera = Camera.main; if (camera == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
        }
    }
}
