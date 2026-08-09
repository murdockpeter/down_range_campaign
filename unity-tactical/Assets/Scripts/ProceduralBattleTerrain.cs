using System;
using System.Collections.Generic;
using UnityEngine;

namespace DownRange.Tactical
{
    public sealed class ProceduralBattleTerrain
    {
        readonly BoardInfo board;
        readonly TerrainInfo profile;
        readonly System.Random random;
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly Transform root;
        readonly float noiseX;
        readonly float noiseZ;
        readonly Dictionary<int, TerrainCellData> authoredCells = new Dictionary<int, TerrainCellData>();
        Camera camera;
        Vector3 focus;
        float yaw;
        float pitch = 64f;
        float distance = 47f;

        public bool Ready { get { return camera != null; } }

        public ProceduralBattleTerrain(BoardInfo boardInfo)
        {
            board = boardInfo;
            profile = board?.terrain ?? new TerrainInfo { archetype = "farmland", seed = 1, elevation = .2f, treeDensity = .2f, buildingDensity = .1f, roadPattern = "farm-lanes" };
            random = new System.Random(profile.seed == 0 ? 1 : profile.seed);
            noiseX = (float)random.NextDouble() * 80f;
            noiseZ = (float)random.NextDouble() * 80f;
            if (profile.cells != null) foreach (var cell in profile.cells) authoredCells[cell.y * 10000 + cell.x] = cell;
            root = new GameObject("Generated terrain · " + (profile.locationName ?? profile.archetype)).transform;
            BuildLighting();
            BuildGround();
            BuildRoadNetwork();
            BuildWater();
            BuildStructures();
            BuildVegetation();
            BuildSignatureFeature();
            BuildCamera();
        }

        void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.43f, .49f, .52f);
            RenderSettings.ambientEquatorColor = new Color(.26f, .30f, .27f);
            RenderSettings.ambientGroundColor = new Color(.10f, .12f, .10f);
            var sun = new GameObject("Terrain sun").AddComponent<Light>();
            sun.transform.SetParent(root); sun.type = LightType.Directional; sun.intensity = 1.1f;
            sun.color = new Color(1f, .91f, .77f); sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
        }

        void BuildCamera()
        {
            var clearCamera = new GameObject("Tactical frame clear camera").AddComponent<Camera>();
            clearCamera.depth = -10f; clearCamera.cullingMask = 0; clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(.018f, .027f, .023f); clearCamera.rect = new Rect(0f, 0f, 1f, 1f);
            camera = Camera.main;
            if (camera == null)
            {
                var host = new GameObject("Procedural tactical camera"); host.tag = "MainCamera"; camera = host.AddComponent<Camera>();
            }
            if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
            camera.orthographic = true; camera.orthographicSize = 25f; camera.nearClipPlane = .05f; camera.farClipPlane = 300f;
            camera.depth = 0f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .075f, .07f);
            camera.allowHDR = true;
            const float header = 66f, footer = 132f, left = 210f, right = 286f;
            var available = new Rect(left + 9f, header + 9f, Mathf.Max(200f, Screen.width - left - right - 18f), Mathf.Max(160f, Screen.height - header - footer - 30f));
            var aspect = board.widthInches / board.heightInches; var width = available.width; var height = width / aspect;
            if (height > available.height) { height = available.height; width = height * aspect; }
            SetViewport(new Rect(available.x + (available.width - width) * .5f, available.y + (available.height - height) * .5f, width, height));
            camera.orthographicSize = distance * .43f; UpdateCameraTransform();
        }

        void BuildGround()
        {
            var cellSize = profile.gridCellSize <= 0f ? 1f : profile.gridCellSize;
            var columns = Mathf.Max(1, Mathf.RoundToInt(board.widthInches / cellSize));
            var rows = Mathf.Max(1, Mathf.RoundToInt(board.heightInches / cellSize));
            var heights = new float[columns + 1, rows + 1];
            for (var x = 0; x <= columns; x++) for (var z = 0; z <= rows; z++)
            {
                var worldX = -board.widthInches * .5f + board.widthInches * x / columns;
                var worldZ = -board.heightInches * .5f + board.heightInches * z / rows;
                heights[x, z] = HeightAt(worldX, worldZ);
                TerrainCellData cell; if (authoredCells.TryGetValue(Mathf.Min(rows - 1, z) * 10000 + Mathf.Min(columns - 1, x), out cell)) heights[x, z] += cell.elevation;
            }
            for (var pass = 0; pass < Mathf.Clamp(profile.smoothingPasses, 0, 8); pass++)
            {
                var smoothed = (float[,])heights.Clone();
                for (var x = 1; x < columns; x++) for (var z = 1; z < rows; z++)
                    smoothed[x, z] = heights[x, z] * .5f + (heights[x - 1, z] + heights[x + 1, z] + heights[x, z - 1] + heights[x, z + 1]) * .125f;
                heights = smoothed;
            }
            var vertices = new Vector3[(columns + 1) * (rows + 1)]; var colors = new Color[vertices.Length];
            var triangles = new int[columns * rows * 6];
            for (var z = 0; z <= rows; z++) for (var x = 0; x <= columns; x++)
            {
                var index = z * (columns + 1) + x;
                var worldX = -board.widthInches * .5f + board.widthInches * x / columns;
                var worldZ = -board.heightInches * .5f + board.heightInches * z / rows;
                vertices[index] = new Vector3(worldX, heights[x, z], worldZ);
                colors[index] = GroundColorAt(x, z, columns, rows);
            }
            var triangle = 0;
            for (var z = 0; z < rows; z++) for (var x = 0; x < columns; x++)
            {
                var a = z * (columns + 1) + x; var b = a + 1; var c = a + columns + 1; var d = c + 1;
                triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
                triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
            }
            var mesh = new Mesh { name = "one-inch smoothed terrain grid" }; mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var terrainObject = new GameObject("one-inch terrain mesh"); terrainObject.transform.SetParent(root);
            terrainObject.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = terrainObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = TerrainMaterial(); renderer.receiveShadows = true;
            CreateBox("table edge", new Vector3(0f, -.48f, 0f), new Vector3(board.widthInches + 1.2f, .72f, board.heightInches + 1.2f), Material("table", new Color(.20f, .15f, .11f)));
        }

        Color GroundColorAt(int x, int z, int columns, int rows)
        {
            TerrainCellData authored;
            if (authoredCells.TryGetValue(Mathf.Min(rows - 1, z) * 10000 + Mathf.Min(columns - 1, x), out authored))
            {
                switch ((authored.type ?? "").ToLowerInvariant())
                {
                    case "woods": return new Color(.16f, .31f, .17f);
                    case "water": return new Color(.12f, .31f, .36f);
                    case "road": return new Color(.22f, .23f, .21f);
                    case "wet": return new Color(.25f, .32f, .25f);
                    case "built": return new Color(.43f, .42f, .36f);
                }
            }
            var broad = Mathf.PerlinNoise(noiseX + x * .11f, noiseZ + z * .11f);
            var field = ((x / 8) + (z / 6)) % 2 == 0;
            var open = field ? new Color(.35f, .43f, .29f) : new Color(.30f, .39f, .27f);
            var wooded = new Color(.19f, .34f, .20f);
            var wet = new Color(.25f, .32f, .25f);
            var color = Color.Lerp(open, wooded, Mathf.SmoothStep(.48f, .78f, broad) * profile.treeDensity);
            return Color.Lerp(color, wet, profile.wetGround * Mathf.SmoothStep(.12f, .34f, 1f - broad));
        }

        public float HeightAt(float x, float z)
        {
            var scale = .055f;
            var broad = Mathf.PerlinNoise(noiseX + x * scale, noiseZ + z * scale);
            var detail = Mathf.PerlinNoise(noiseZ + x * .12f, noiseX + z * .12f);
            return .06f + profile.elevation * (broad * 1.45f + detail * .28f);
        }

        void BuildRoadNetwork()
        {
            var asphalt = Material("asphalt", new Color(.20f, .23f, .22f));
            var dirt = Material("dirt road", new Color(.42f, .36f, .25f));
            var roadMaterial = profile.roadPattern == "farm-lanes" || profile.roadPattern == "trail" ? dirt : asphalt;
            if (profile.roadPattern == "street-grid" || profile.roadPattern == "junction" || profile.roadPattern == "base-loop")
            {
                Road(new Vector3(0f, 0f, 0f), new Vector3(board.widthInches, .12f, 5f), roadMaterial);
                Road(new Vector3(8f, 0f, 0f), new Vector3(5f, .12f, board.heightInches), roadMaterial);
                if (profile.roadPattern == "street-grid") Road(new Vector3(-14f, 0f, 0f), new Vector3(4f, .12f, board.heightInches), roadMaterial);
            }
            else if (profile.roadPattern == "rail-yard")
            {
                Road(new Vector3(0f, 0f, -3f), new Vector3(board.widthInches, .10f, 4.5f), dirt);
                for (var line = -1; line <= 1; line++) Rail(-7f + line * 4.2f);
            }
            else if (profile.roadPattern == "causeway") Road(Vector3.zero, new Vector3(board.widthInches, .28f, 5.5f), asphalt);
            else
            {
                Road(new Vector3(0f, 0f, 3f), new Vector3(board.widthInches, .10f, profile.roadPattern == "service-road" ? 4.2f : 2.6f), roadMaterial);
                Road(new Vector3(14f, 0f, 0f), new Vector3(2.8f, .10f, board.heightInches), roadMaterial);
            }
        }

        void Road(Vector3 center, Vector3 size, Material material)
        {
            center.y = HeightAt(center.x, center.z) + .10f; CreateBox("road", center, size, material);
        }

        void Rail(float z)
        {
            var steel = Material("rail", new Color(.16f, .17f, .16f));
            CreateBox("rail", new Vector3(0f, HeightAt(0f, z) + .18f, z - .45f), new Vector3(board.widthInches, .12f, .16f), steel);
            CreateBox("rail", new Vector3(0f, HeightAt(0f, z) + .18f, z + .45f), new Vector3(board.widthInches, .12f, .16f), steel);
        }

        void BuildWater()
        {
            if (profile.water < .08f) return;
            var water = Material("water", new Color(.12f, .31f, .36f));
            var crossing = profile.archetype == "dam-crossing";
            var size = crossing ? new Vector3(board.widthInches, .16f, board.heightInches * .34f) : new Vector3(board.widthInches * Mathf.Lerp(.16f, .34f, profile.water), .14f, board.heightInches * .22f);
            var center = crossing ? new Vector3(0f, .18f, -board.heightInches * .28f) : new Vector3(-board.widthInches * .32f, .16f, board.heightInches * .30f);
            CreateBox("water", center, size, water);
        }

        void BuildStructures()
        {
            var count = Mathf.Clamp(Mathf.RoundToInt(profile.buildingDensity * 34f), 1, 26);
            var walls = Material("walls", profile.archetype == "forward-base" ? new Color(.33f, .39f, .31f) : new Color(.58f, .51f, .39f));
            var roof = Material("roofs", new Color(.20f, .23f, .22f));
            for (var i = 0; i < count; i++)
            {
                var x = Range(-board.widthInches * .39f, board.widthInches * .39f); var z = Range(-board.heightInches * .38f, board.heightInches * .38f);
                if (Mathf.Abs(z - 3f) < 4f) z += z < 3f ? -5f : 5f;
                var width = Range(2.4f, profile.archetype == "small-town" ? 5.8f : 4.7f); var depth = Range(2.2f, 4.5f); var height = Range(1.5f, profile.archetype == "small-town" ? 4.8f : 3.2f);
                var ground = HeightAt(x, z);
                CreateBox("building", new Vector3(x, ground + height * .5f, z), new Vector3(width, height, depth), walls);
                CreateBox("roof", new Vector3(x, ground + height + .12f, z), new Vector3(width + .18f, .24f, depth + .18f), roof);
            }
        }

        void BuildVegetation()
        {
            var count = Mathf.Clamp(Mathf.RoundToInt(profile.treeDensity * 78f), 0, 64);
            var trunk = Material("trunks", new Color(.23f, .14f, .08f)); var foliage = Material("foliage", new Color(.17f, .34f, .19f));
            for (var i = 0; i < count; i++)
            {
                var x = Range(-board.widthInches * .47f, board.widthInches * .47f); var z = Range(-board.heightInches * .45f, board.heightInches * .45f);
                if (Mathf.Abs(z - 3f) < 2.3f) continue;
                var ground = HeightAt(x, z); var height = Range(1.7f, 3.4f);
                var trunkObject = CreatePrimitive(PrimitiveType.Cylinder, "tree trunk", new Vector3(x, ground + height * .28f, z), new Vector3(.22f, height * .28f, .22f), trunk);
                trunkObject.transform.localScale = new Vector3(.22f, height * .28f, .22f);
                CreatePrimitive(PrimitiveType.Sphere, "tree crown", new Vector3(x, ground + height * .78f, z), new Vector3(1.15f, height * .38f, 1.15f), foliage);
            }
        }

        void BuildSignatureFeature()
        {
            if (profile.archetype == "relay-compound" || profile.archetype == "wooded-ridge")
            {
                var steel = Material("antenna", new Color(.18f, .20f, .19f));
                var x = 17f; var z = -7f; var ground = HeightAt(x, z);
                CreatePrimitive(PrimitiveType.Cylinder, "relay mast", new Vector3(x, ground + 4.5f, z), new Vector3(.18f, 4.5f, .18f), steel);
                CreateBox("relay hardstand", new Vector3(x, ground + .08f, z), new Vector3(7f, .16f, 7f), Material("hardstand", new Color(.38f, .39f, .36f)));
            }
            if (profile.archetype == "dam-crossing")
                CreateBox("dam wall", new Vector3(0f, 1.1f, -4f), new Vector3(board.widthInches, 2.2f, 2.4f), Material("dam concrete", new Color(.42f, .44f, .42f)));
            var marker = Material("objective", new Color(.83f, .58f, .15f));
            CreatePrimitive(PrimitiveType.Cylinder, "objective marker", new Vector3(17f, HeightAt(17f, -7f) + .18f, -7f), new Vector3(2.2f, .12f, 2.2f), marker);
        }

        public void UpdateInput()
        {
            if (Input.GetMouseButton(2))
            {
                yaw += Input.GetAxis("Mouse X") * 3.2f;
                pitch += Input.GetAxis("Mouse Y") * 2.8f;
            }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 2.2f, 12f, 105f);
            camera.orthographicSize = Mathf.Clamp(distance * .43f, 5.2f, 47f);
            UpdateCameraTransform();
        }

        void UpdateCameraTransform()
        {
            if (camera == null) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0f); camera.transform.position = focus + rotation * new Vector3(0f, 0f, -distance); camera.transform.LookAt(focus + Vector3.up * .7f);
        }

        public void SetViewport(Rect guiRect)
        {
            if (camera == null) return;
            camera.rect = new Rect(guiRect.x / Screen.width, (Screen.height - guiRect.yMax) / Screen.height, guiRect.width / Screen.width, guiRect.height / Screen.height);
        }

        public Vector2 GuiPoint(float xPercent, float yPercent)
        {
            var world = WorldPoint(xPercent, yPercent); world.y += 1.05f;
            var screen = camera.WorldToScreenPoint(world); return new Vector2(screen.x, Screen.height - screen.y);
        }

        public bool TryPercent(Vector2 guiPoint, out float xPercent, out float yPercent)
        {
            var ray = camera.ScreenPointToRay(new Vector3(guiPoint.x, Screen.height - guiPoint.y));
            var plane = new Plane(Vector3.up, Vector3.zero); float distanceToPlane;
            if (!plane.Raycast(ray, out distanceToPlane)) { xPercent = yPercent = 0f; return false; }
            var point = ray.GetPoint(distanceToPlane);
            xPercent = (point.x / board.widthInches + .5f) * 100f;
            yPercent = (.5f - point.z / board.heightInches) * 100f;
            return xPercent >= 0f && xPercent <= 100f && yPercent >= 0f && yPercent <= 100f;
        }

        Vector3 WorldPoint(float xPercent, float yPercent)
        {
            var x = (xPercent / 100f - .5f) * board.widthInches; var z = (.5f - yPercent / 100f) * board.heightInches;
            return new Vector3(x, HeightAt(x, z), z);
        }

        GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material) { return CreatePrimitive(PrimitiveType.Cube, name, position, scale, material); }
        GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type); item.name = name; item.transform.SetParent(root); item.transform.position = position; item.transform.localScale = scale;
            var renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; renderer.receiveShadows = true;
            var collider = item.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.Destroy(collider); return item;
        }

        Material Material(string key, Color color)
        {
            Material found; if (materials.TryGetValue(key, out found)) return found;
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse"); found = new Material(shader) { name = key, color = color }; found.SetFloat("_Glossiness", .06f); materials[key] = found; return found;
        }

        Material TerrainMaterial()
        {
            Material found; if (materials.TryGetValue("terrain grid", out found)) return found;
            var shader = Resources.Load<Shader>("Shaders/DownRangeTerrainGrid") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            found = new Material(shader) { name = "one-inch blended terrain" }; materials["terrain grid"] = found; return found;
        }

        float Range(float minimum, float maximum) { return Mathf.Lerp(minimum, maximum, (float)random.NextDouble()); }
    }
}
