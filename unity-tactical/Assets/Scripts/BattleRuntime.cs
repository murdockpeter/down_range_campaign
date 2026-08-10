using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DownRange.Tactical
{
    public sealed class BattleRuntime : MonoBehaviour
    {
        BattleRequest request;
        BattleState state;
        DeterministicDice dice;
        Texture2D mapTexture;
        Texture2D pixel;
        Texture2D infantrySprite;
        Texture2D medicSprite;
        Texture2D uasSprite;
        TacticalAudio audio;
        ProceduralBattleTerrain terrain;
        CampaignMiniatureSet miniatures;
        bool showHelp;
        bool losTool;
        bool losStartSet;
        bool losEndSet;
        Vector2 losStart;
        Vector2 losEnd;
        BattleLosResult measuredLos;
        string requestPath;
        string statePath;
        string resultPath;
        Vector2 rosterScroll;
        Vector2 logScroll;
        Vector2 inspectorScroll;
        string hoveredActionHelp;
        string notice = "Select an active miniature, then click the terrain to move or an opposing miniature to target.";
        GUIStyle titleStyle, smallStyle, panelStyle, tokenStyle, selectedTokenStyle, logStyle, guideStyle, tooltipStyle, objectiveStyle, actionButtonStyle, unavailableActionStyle;

        sealed class ActionDescriptor
        {
            public string title; public string summary; public string cost; public string requirements; public string effect;
            public bool available; public string unavailable;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<BattleRuntime>() != null || FindFirstObjectByType<OneStarRuntime>() != null) return;
            var oneStar = Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--one-star", StringComparison.OrdinalIgnoreCase));
            var host = new GameObject(oneStar ? "One Star 3D Runtime" : "Down Range Tactical Runtime");
            if (oneStar) host.AddComponent<OneStarRuntime>(); else host.AddComponent<BattleRuntime>();
        }

        void Awake()
        {
            Application.runInBackground = true;
            pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
            audio = new TacticalAudio(gameObject);
            LoadSprites();
            requestPath = FindArgument("--battle-request");
            if (string.IsNullOrWhiteSpace(requestPath)) requestPath = Path.Combine(Application.streamingAssetsPath, "sample-battle-request.json");
            try { LoadBattle(); }
            catch (Exception error) { notice = "Unable to load battle request: " + error.Message; Debug.LogException(error); }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; audio.Play(SoundCue.Click); }
            if (showHelp && Input.GetKeyDown(KeyCode.Escape)) { showHelp = false; audio.Play(SoundCue.Click); }
            if (state != null && !showHelp) terrain?.UpdateInput();
            if (state != null && miniatures != null) miniatures.Sync(state.units, state.selectedId, state.targetId);
            if (state == null || state.completed || showHelp) return;
            if (Input.GetKeyDown(KeyCode.L)) ToggleLosTool();
            if (Input.GetKeyDown(KeyCode.Space)) EndTurn();
        }

        void ToggleLosTool()
        {
            losTool = !losTool;
            losStartSet = false;
            losEndSet = false;
            measuredLos = null;
            notice = losTool ? "LOS tool active: click the first point, then the second. Right-click to reset." : "LOS tool closed.";
            audio.Play(SoundCue.Click);
        }

        void LoadSprites()
        {
            var folder = Path.Combine(Application.streamingAssetsPath, "Sprites");
            infantrySprite = LoadTexture(Path.Combine(folder, "infantry.png"));
            medicSprite = LoadTexture(Path.Combine(folder, "medic.png"));
            uasSprite = LoadTexture(Path.Combine(folder, "uas.png"));
        }

        Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = Path.GetFileNameWithoutExtension(path);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }

        string FindArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return Path.GetFullPath(args[i + 1]);
            return string.Empty;
        }

        void LoadBattle()
        {
            if (!File.Exists(requestPath)) throw new FileNotFoundException("Battle request not found.", requestPath);
            request = JsonUtility.FromJson<BattleRequest>(File.ReadAllText(requestPath));
            if (request == null || request.contractVersion != 1 || string.IsNullOrEmpty(request.requestId)) throw new InvalidDataException("Unsupported or incomplete battle request.");
            var exchange = Path.GetDirectoryName(requestPath) ?? Application.persistentDataPath;
            statePath = Path.Combine(exchange, "battle-state.json"); resultPath = Path.Combine(exchange, "battle-result.json");
            if (File.Exists(statePath))
            {
                var restored = JsonUtility.FromJson<BattleState>(File.ReadAllText(statePath));
                if (restored != null && restored.requestId == request.requestId) state = restored;
            }
            if (state == null)
            {
                state = new BattleState { requestId = request.requestId, units = request.units, objectives = request.objectives, events = new BattleEvent[0] };
                state.selectedId = state.units.FirstOrDefault(unit => unit.side == "blue")?.id;
                state.targetId = state.units.FirstOrDefault(unit => unit.side == "red")?.id;
                dice = new DeterministicDice(request.settings.seed);
                RollInitiative();
                AddEvent(string.Format("Initiative: BLUE {0}, RED {1}. {2} acts first.", state.blueInitiative, state.redInitiative, state.activeSide.ToUpperInvariant()), "system");
                Save();
            }
            foreach (var unit in state.units ?? new UnitData[0]) if (unit.moved && unit.movesMade == 0) unit.movesMade = 1;
            dice = new DeterministicDice(request.settings.seed, state.rollCount);
            LoadMap();
            terrain = new ProceduralBattleTerrain(request.board);
            miniatures = new CampaignMiniatureSet(terrain, state.units);
            miniatures.Sync(state.units, state.selectedId, state.targetId);
        }

        void LoadMap()
        {
            if (request.board == null || string.IsNullOrWhiteSpace(request.board.mapPath) || !File.Exists(request.board.mapPath)) return;
            mapTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!mapTexture.LoadImage(File.ReadAllBytes(request.board.mapPath))) mapTexture = null;
        }

        void RollInitiative()
        {
            do { state.blueInitiative = dice.Roll(6); state.redInitiative = dice.Roll(6); } while (state.blueInitiative == state.redInitiative);
            state.activeSide = state.blueInitiative > state.redInitiative ? "blue" : "red";
            state.firstSide = state.activeSide; state.firstSideFinished = false;
        }

        void BuildStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.88f, .92f, .88f) } };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true, normal = { textColor = new Color(.58f, .64f, .60f) } };
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 9, 9), normal = { background = MakeTexture(new Color(.055f, .078f, .068f, .98f)) } };
            tokenStyle = new GUIStyle(GUI.skin.button) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { background = pixel, textColor = Color.white }, hover = { background = pixel, textColor = Color.white } };
            selectedTokenStyle = new GUIStyle(tokenStyle) { fontSize = 11 };
            logStyle = new GUIStyle(smallStyle) { fontSize = 9 };
            guideStyle = new GUIStyle(smallStyle) { fontSize = 11, richText = true, normal = { textColor = new Color(.78f, .84f, .79f) } };
            tooltipStyle = new GUIStyle(GUI.skin.box) { fontSize = 11, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(9, 9, 7, 7), normal = { background = MakeTexture(new Color(.035f, .052f, .045f, .98f)), textColor = new Color(.92f, .95f, .91f) } };
            objectiveStyle = new GUIStyle(GUI.skin.box) { fontSize = 9, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 4, 4) };
            actionButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 10, richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(9, 7, 4, 4) };
            unavailableActionStyle = new GUIStyle(actionButtonStyle); unavailableActionStyle.normal.textColor = new Color(.46f, .50f, .47f); unavailableActionStyle.hover.textColor = new Color(.72f, .66f, .54f);
        }

        Texture2D MakeTexture(Color color) { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }
        UnitData Unit(string id) { return state?.units?.FirstOrDefault(unit => unit.id == id); }
        bool Effective(UnitData unit) { return unit != null && unit.status != "downed" && unit.status != "dead"; }
        bool CanAct(UnitData unit) { return Effective(unit) && ((unit.side == state.activeSide && !unit.actionUsed) || unit.reaction); }
        void ConsumeAction(UnitData unit) { if (unit.reaction) unit.reaction = false; else unit.actionUsed = true; }
        string RollText(DieRoll roll) { return roll.second > 0 ? string.Format("[{0},{1}]→{2}", roll.first, roll.second, roll.result) : roll.result.ToString(CultureInfo.InvariantCulture); }

        void OnGUI()
        {
            BuildStyles();
            GUI.backgroundColor = new Color(.12f, .16f, .14f); GUI.contentColor = new Color(.9f, .93f, .9f);
            var width = Screen.width; var height = Screen.height; const float header = 66f; const float footer = 132f; const float left = 210f; const float right = 286f;
            GUI.Box(new Rect(0, 0, width, header), GUIContent.none, panelStyle);
            GUI.Label(new Rect(16, 9, width * .45f, 23), request == null ? "DOWN RANGE TACTICAL" : request.mission.title.ToUpperInvariant(), titleStyle);
            if (state != null)
            {
                GUI.Label(new Rect(17, 34, 650, 20), string.Format("ROUND {0}  ·  {1} TURN  ·  {2}  ·  INIT B{3}/R{4}  ·  RULES {5}", state.round, state.activeSide.ToUpperInvariant(), request.mission.locationName, state.blueInitiative, state.redInitiative, request.rulesVersion), smallStyle);
                if (GUI.Button(new Rect(width - 492, 16, 68, 34), new GUIContent(audio.Enabled ? "SOUND" : "MUTED", "Toggle all tactical sound cues. This preference is saved."))) { audio.Enabled = !audio.Enabled; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 416, 16, 82, 34), new GUIContent("TURN HELP", "Open the turn sequence and action reference. Shortcut: F1."))) { showHelp = true; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 326, 16, 112, 34), new GUIContent("END TURN", "Finish this side's turn. Units that did not act will automatically hold a reaction. Shortcut: Space."))) EndTurn();
                if (GUI.Button(new Rect(width - 206, 16, 190, 34), new GUIContent(state.completed ? "QUIT TO TRACKER" : "END MISSION", state.completed ? "Close the tactical game and return to Campaign Command." : "Score objectives and export the battle result to the campaign tracker."))) { audio.Play(SoundCue.Click); if (state.completed) Application.Quit(); else FinishBattle(); }
            }
            if (request == null || state == null) { GUI.Label(new Rect(30, 100, width - 60, 80), notice, titleStyle); return; }

            var rosterRect = new Rect(0, header, left, height - header - footer);
            var inspectorRect = new Rect(width - right, header, right, height - header - footer);
            var stageRect = new Rect(left, header, width - left - right, height - header - footer);
            if (showHelp) { DrawHelpOverlay(); return; }
            DrawRoster(rosterRect); DrawBoard(stageRect); DrawInspector(inspectorRect); DrawFooter(new Rect(0, height - footer, width, footer));
            DrawTooltip();
        }

        void DrawRoster(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16));
            GUILayout.Label("ORDER OF BATTLE", smallStyle); rosterScroll = GUILayout.BeginScrollView(rosterScroll);
            foreach (var side in new[] { "blue", "red" })
            {
                var old = GUI.contentColor; GUI.contentColor = side == "blue" ? new Color(.48f, .78f, .86f) : new Color(.9f, .48f, .41f);
                GUILayout.Label(side.ToUpperInvariant(), titleStyle); GUI.contentColor = old;
                foreach (var unit in state.units.Where(item => item.side == side))
                {
                    var marker = unit.id == state.selectedId ? "▶ " : "  ";
                    var acted = unit.actionUsed ? " · acted" : unit.reaction ? " · reaction" : "";
                    if (GUILayout.Button(new GUIContent(marker + unit.name + "\n    " + unit.role + " · " + unit.status + acted, UnitTooltip(unit)), GUILayout.Height(43))) { state.selectedId = unit.id; audio.Play(SoundCue.Click); }
                }
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawBoard(Rect stage)
        {
            if (terrain == null || !terrain.Ready) GUI.Box(stage, GUIContent.none, panelStyle);
            var available = new Rect(stage.x + 9, stage.y + 9, stage.width - 18, stage.height - 30);
            var board = FitAspect(available, request.board.widthInches / request.board.heightInches);
            terrain?.SetViewport(board);
            if (terrain != null && terrain.Ready) { }
            else if (mapTexture != null) GUI.DrawTexture(board, mapTexture, ScaleMode.StretchToFill, false);
            else { GUI.color = new Color(.16f, .2f, .15f); GUI.DrawTexture(board, pixel); GUI.color = Color.white; GUI.Label(new Rect(board.x + 20, board.y + 20, 300, 40), "MAP ASSET UNAVAILABLE\nGameplay remains functional.", smallStyle); }
            if (terrain == null || !terrain.Ready) { GUI.color = new Color(1f, .78f, .3f, .75f); GUI.Box(new Rect(board.x + board.width * .62f, board.y + board.height * .07f, board.width * .25f, board.height * .31f), "RELAY ZONE"); GUI.color = Color.white; }

            if (losTool) { HandleLosTool(board); DrawLosTool(board); }

            var selected = Unit(state.selectedId); var target = Unit(state.targetId);
            if (!losTool && selected != null && target != null)
            {
                var selectedAssessment = SelectedLos(selected, target); state.cover = selectedAssessment.classification;
                var sightColor = state.cover == "blocked" ? new Color(.94f, .26f, .19f) : state.cover == "partial" ? new Color(1f, .68f, .18f) : new Color(.28f, .92f, .56f);
                var a = Point(board, selected); var b = Point(board, target); DrawLine(a, b, sightColor, 1.8f);
                GUI.Label(new Rect((a.x + b.x) / 2f, (a.y + b.y) / 2f, 110, 34), TacticalRules.Distance(selected, target, request.board).ToString("0.0") + "\" · " + state.cover.ToUpperInvariant(), smallStyle);
            }
            foreach (var unit in state.units) DrawToken(board, unit);

            var current = Event.current;
            if (!losTool && current.type == EventType.MouseUp && board.Contains(current.mousePosition))
            {
                var tokenHit = state.units.Any(unit => TokenRect(board, unit).Contains(current.mousePosition));
                if (!tokenHit && TryMoveSelected(board, current.mousePosition)) current.Use();
            }
            var boardHint = losTool ? "LOS TOOL · left-click two points · right-click resets · L closes" : "IMPORTED 3D MINIATURES · middle-drag rotates/skews · wheel zooms · select a miniature, then click its destination";
            GUI.Label(new Rect(stage.x + 12, stage.yMax - 20, stage.width - 24, 18), boardHint, smallStyle);
        }

        void HandleLosTool(Rect board)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown || !board.Contains(current.mousePosition)) return;
            if (current.button == 1)
            {
                losStartSet = false; losEndSet = false; measuredLos = null; notice = "LOS measurement reset. Click the first point."; audio.Play(SoundCue.Click); current.Use(); return;
            }
            if (current.button != 0) return;
            Vector2 point;
            if (terrain != null && terrain.Ready)
            {
                float x, y; if (!terrain.TryPercent(current.mousePosition, out x, out y)) return; point = new Vector2(x / 100f, y / 100f);
            }
            else point = new Vector2(Mathf.Clamp01((current.mousePosition.x - board.x) / board.width), Mathf.Clamp01((current.mousePosition.y - board.y) / board.height));
            if (!losStartSet || losEndSet)
            {
                losStart = point; losStartSet = true; losEndSet = false; measuredLos = null; notice = "LOS start set. Click the second point.";
            }
            else
            {
                losEnd = point; losEndSet = true;
                measuredLos = MeasureLos(losStart, losEnd); state.cover = measuredLos.classification;
                notice = LosNotice(measuredLos, LosDistance(losStart, losEnd));
            }
            audio.Play(SoundCue.Click); current.Use();
        }

        void DrawLosTool(Rect board)
        {
            if (!losStartSet) return;
            var start = LosPoint(board, losStart);
            var hasPreview = board.Contains(Event.current.mousePosition);
            if (!losEndSet && !hasPreview) { DrawLosEndpoint(start); return; }
            Vector2 endPercent;
            if (losEndSet) endPercent = losEnd;
            else if (terrain != null && terrain.Ready)
            {
                float x, y; if (!terrain.TryPercent(Event.current.mousePosition, out x, out y)) { DrawLosEndpoint(start); return; } endPercent = new Vector2(x / 100f, y / 100f);
            }
            else endPercent = new Vector2(Mathf.Clamp01((Event.current.mousePosition.x - board.x) / board.width), Mathf.Clamp01((Event.current.mousePosition.y - board.y) / board.height));
            var end = LosPoint(board, endPercent);
            var assessment = losEndSet && measuredLos != null ? measuredLos : MeasureLos(losStart, endPercent);
            var color = assessment.classification == "blocked" ? new Color(.94f, .26f, .19f) : assessment.classification == "partial" ? new Color(1f, .68f, .18f) : new Color(.28f, .92f, .56f);
            DrawLine(start, end, color, 3f); DrawLosEndpoint(start); DrawLosEndpoint(end);
            var blocker = string.IsNullOrWhiteSpace(assessment.blocker) ? "" : "\n" + assessment.blocker;
            var label = string.Format("LOS {0}\n{1:0.0}\"{2}", assessment.classification.ToUpperInvariant(), LosDistance(losStart, endPercent), blocker);
            var midpoint = (start + end) * .5f; var labelRect = new Rect(midpoint.x - 64f, midpoint.y - 52f, 128f, string.IsNullOrWhiteSpace(assessment.blocker) ? 38f : 52f);
            labelRect.x = Mathf.Clamp(labelRect.x, board.x + 4f, board.xMax - labelRect.width - 4f); labelRect.y = Mathf.Clamp(labelRect.y, board.y + 4f, board.yMax - labelRect.height - 4f);
            GUI.Box(labelRect, label, tooltipStyle);
        }

        Vector2 LosPoint(Rect board, Vector2 percent) { return terrain != null && terrain.Ready ? terrain.GuiPoint(percent.x * 100f, percent.y * 100f) : new Vector2(board.x + board.width * percent.x, board.y + board.height * percent.y); }
        float LosDistance(Vector2 a, Vector2 b)
        {
            var dx = (b.x - a.x) * request.board.widthInches; var dy = (b.y - a.y) * request.board.heightInches;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
        BattleLosResult MeasureLos(Vector2 start, Vector2 end)
        {
            if (terrain == null || !terrain.Ready) return new BattleLosResult { classification = state.cover };
            var result = terrain.EvaluateLineOfSight(start.x * 100f, start.y * 100f, end.x * 100f, end.y * 100f, UnitIdAt(start), UnitIdAt(end));
            return result;
        }
        string UnitIdAt(Vector2 percent)
        {
            var nearest = state.units.Select(unit => new { unit.id, distance = TacticalRules.Distance(percent.x * 100f, percent.y * 100f, unit.x, unit.y, request.board) }).OrderBy(item => item.distance).FirstOrDefault();
            return nearest != null && nearest.distance <= 1f ? nearest.id : "";
        }
        BattleLosResult SelectedLos(UnitData origin, UnitData target)
        {
            return terrain != null && terrain.Ready && origin != null && target != null ? terrain.EvaluateLineOfSight(origin, target) : new BattleLosResult { classification = state.cover };
        }
        string LosNotice(BattleLosResult result, float distance)
        {
            var blocker = string.IsNullOrWhiteSpace(result.blocker) ? "" : " — " + result.blocker;
            return string.Format("LOS {0}: {1:0.0}\"{2}.", result.classification.ToUpperInvariant(), distance, blocker);
        }
        void DrawLosEndpoint(Vector2 point)
        {
            var previous = GUI.color; GUI.color = Color.white; GUI.DrawTexture(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), pixel); GUI.color = previous;
        }

        Rect FitAspect(Rect outer, float aspect)
        {
            var width = outer.width; var height = width / aspect;
            if (height > outer.height) { height = outer.height; width = height * aspect; }
            return new Rect(outer.x + (outer.width - width) / 2f, outer.y + (outer.height - height) / 2f, width, height);
        }
        Vector2 Point(Rect board, UnitData unit) { return terrain != null && terrain.Ready ? terrain.GuiPoint(unit.x, unit.y) : new Vector2(board.x + board.width * unit.x / 100f, board.y + board.height * unit.y / 100f); }
        Rect TokenRect(Rect board, UnitData unit) { var point = Point(board, unit); var size = unit.id == state.selectedId ? 48f : 40f; return new Rect(point.x - size / 2f, point.y - size / 2f, size, size); }
        void DrawToken(Rect board, UnitData unit)
        {
            var rect = TokenRect(board, unit); var previous = GUI.color;
            if (terrain != null && terrain.Ready)
            {
                if (GUI.Button(rect, new GUIContent(string.Empty, UnitTooltip(unit)), GUIStyle.none)) SelectToken(unit);
                var badge3d = unit.status == "downed" ? "DOWN" : unit.status == "dead" ? "KIA" : unit.suppressed ? "SUP" : unit.reaction ? "REACT" : "";
                if (!string.IsNullOrEmpty(badge3d)) GUI.Label(new Rect(rect.x - 4, rect.y - 12, rect.width + 8, 14), badge3d, new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } });
                GUI.Label(new Rect(rect.x - 50, rect.yMax - 2, 140, 17), unit.name, new GUIStyle(smallStyle) { alignment = TextAnchor.UpperCenter, normal = { textColor = unit.side == "blue" ? new Color(.40f, .82f, 1f) : new Color(1f, .43f, .34f) } });
                return;
            }
            GUI.color = unit.status == "dead" ? Color.gray : unit.side == "blue" ? new Color(.18f, .68f, .82f) : new Color(.82f, .24f, .18f);
            if (GUI.Button(rect, new GUIContent(string.Empty, UnitTooltip(unit)), unit.id == state.selectedId ? selectedTokenStyle : tokenStyle)) SelectToken(unit);
            GUI.color = previous;
            var sprite = SpriteFor(unit);
            if (sprite != null)
            {
                var inset = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                if (unit.status == "downed")
                {
                    var matrix = GUI.matrix; GUIUtility.RotateAroundPivot(90f, inset.center); GUI.DrawTexture(inset, sprite, ScaleMode.ScaleToFit, true); GUI.matrix = matrix;
                }
                else GUI.DrawTexture(inset, sprite, ScaleMode.ScaleToFit, true);
            }
            var badge = unit.status == "downed" ? "DOWN" : unit.suppressed ? "SUP" : unit.reaction ? "REACT" : unit.side.ToUpperInvariant();
            GUI.Label(new Rect(rect.x - 2, rect.y - 11, rect.width + 4, 14), badge, new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } });
            GUI.Label(new Rect(rect.x - 45, rect.yMax + 1, 120, 17), unit.name, new GUIStyle(smallStyle) { alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } });
        }
        Texture2D SpriteFor(UnitData unit)
        {
            if (unit.kind == "vehicle") return uasSprite ?? infantrySprite;
            if (unit.medicalSkill > 0 || (!string.IsNullOrEmpty(unit.role) && unit.role.ToLowerInvariant().Contains("medic"))) return medicSprite ?? infantrySprite;
            return infantrySprite;
        }
        string UnitTooltip(UnitData unit)
        {
            var allowance = TacticalRules.MovementAllowance(unit, state.impairedMovement);
            var weapon = unit.weapons != null && unit.weapons.Length > 0 ? unit.weapons[0].name : "Unarmed";
            var availability = unit.side != state.activeSide ? "Waiting for its side's turn" : !Effective(unit) ? "Cannot act while " + unit.status : unit.actionUsed ? "Action already used" : "Ready to act";
            return string.Format("{0} — {1}\n{2} | Move {3:0.#}\" | Skill d{4} | Defense {5}\nWeapon: {6}\n{7}", unit.name, unit.role, unit.status.ToUpperInvariant(), allowance, unit.skill, unit.defense, weapon, availability);
        }
        void SelectToken(UnitData clicked)
        {
            var selected = Unit(state.selectedId);
            if (selected != null && clicked.id != selected.id && (clicked.side != selected.side || clicked.status == "downed")) state.targetId = clicked.id;
            else state.selectedId = clicked.id;
            audio.Play(SoundCue.Click);
        }

        bool TryMoveSelected(Rect board, Vector2 mouse)
        {
            var unit = Unit(state.selectedId);
            var reactionMove = unit != null && unit.reactionMove;
            var movesAllowed = unit != null && unit.sprint ? 2 : 1;
            if (unit == null || !Effective(unit) || unit.focused || (!reactionMove && (unit.side != state.activeSide || unit.movesMade >= movesAllowed))) { notice = "That unit cannot move now."; audio.Play(SoundCue.Error); return false; }
            float x, y;
            if (terrain != null && terrain.Ready) { if (!terrain.TryPercent(mouse, out x, out y)) { notice = "Choose a point on the tabletop."; return false; } }
            else { x = Mathf.Clamp((mouse.x - board.x) / board.width * 100f, 0f, 100f); y = Mathf.Clamp((mouse.y - board.y) / board.height * 100f, 0f, 100f); }
            var distance = TacticalRules.Distance(unit.x, unit.y, x, y, request.board); var allowance = TacticalRules.MovementAllowance(unit, state.impairedMovement) / (unit.sprint ? 2f : 1f);
            if (distance > allowance + .05f) { notice = string.Format("Move is {0:0.0}\"; allowance is {1:0.0}\".", distance, allowance); audio.Play(SoundCue.Error); return false; }
            var oldX = unit.x; var oldY = unit.y;
            miniatures?.FaceMovement(unit, oldX, oldY, x, y);
            unit.x = x; unit.y = y; unit.moved = true; unit.movesMade++; unit.reactionMove = false; AddEvent(string.Format("{0} moves {1:0.0}\"{2}.", unit.name, distance, state.impairedMovement ? " through impaired terrain" : ""), "move"); audio.Play(SoundCue.Move); Save(); return true;
        }

        void DrawInspector(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 9, rect.width - 20, rect.height - 18));
            inspectorScroll = GUILayout.BeginScrollView(inspectorScroll, false, true);
            GUILayout.Label("UNIT CONTROL", smallStyle); var unit = Unit(state.selectedId); var target = Unit(state.targetId);
            if (unit == null) { GUILayout.Label("Select a unit.", titleStyle); GUILayout.EndScrollView(); GUILayout.EndArea(); return; }
            GUILayout.Label(unit.name, titleStyle); GUILayout.Label(unit.role + " · " + unit.status, smallStyle);
            GUILayout.Label(string.Format("MOVE {0:0.#}\"     SKILL d{1}     DEF {2}", TacticalRules.MovementAllowance(unit, state.impairedMovement), unit.skill, unit.defense), smallStyle);
            GUILayout.Space(8); GUILayout.Label(target == null ? "TARGET: none" : string.Format("TARGET: {0} · {1:0.0}\"", target.name, TacticalRules.Distance(unit, target, request.board)), smallStyle);
            GUILayout.Label("LINE OF SIGHT / COVER", smallStyle);
            if (terrain != null && terrain.Ready && target != null)
            {
                var automaticLos = SelectedLos(unit, target); state.cover = automaticLos.classification;
                var detail = string.IsNullOrWhiteSpace(automaticLos.blocker) ? "Uninterrupted eye-height line." : automaticLos.blocker;
                GUILayout.Box("AUTO LOS · " + state.cover.ToUpperInvariant() + "\n" + detail, guideStyle);
            }
            else
            {
                var covers = new[] { new GUIContent("Open", "Clear line of sight: no defense modifier."), new GUIContent("Partial", "Target is partly concealed: harder to hit."), new GUIContent("Blocked", "No line of sight: attacks cannot be made.") }; var coverIndex = state.cover == "partial" ? 1 : state.cover == "blocked" ? 2 : 0;
                coverIndex = GUILayout.SelectionGrid(coverIndex, covers, 3); state.cover = coverIndex == 1 ? "partial" : coverIndex == 2 ? "blocked" : "open";
            }
            if (ActionButton(losTool ? "LOS TOOL · ON" : "LOS TOOL · OFF", "Measure sight lines and tabletop distance between any two points. Shortcut: L.", 27)) ToggleLosTool();
            state.impairedMovement = GUILayout.Toggle(state.impairedMovement, new GUIContent("Impaired movement", "Use for mud, climbing, crawling, or other terrain that reduces movement."));
            GUILayout.Space(8); DrawActionMenu(unit, target);
            GUILayout.Space(8); DrawTurnGuide(unit, target);
            GUILayout.Space(5); GUILayout.Label(notice, smallStyle); GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawActionMenu(UnitData unit, UnitData target)
        {
            hoveredActionHelp = string.Empty;
            GUILayout.Label("UNIT ACTIONS", smallStyle);
            var generalReason = ActionStateReason(unit);
            var canAct = string.IsNullOrEmpty(generalReason);
            var weapon = unit.weapons?.FirstOrDefault();
            var opposingTarget = target != null && target.side != unit.side;
            var targetDistance = target == null ? 0f : TacticalRules.Distance(unit, target, request.board);
            var selectedLos = target == null ? new BattleLosResult { classification = "open" } : SelectedLos(unit, target);
            var movesAllowed = unit.sprint ? 2 : 1;
            var moveReason = !Effective(unit) ? "This unit cannot move while " + unit.status + "." :
                unit.focused ? "This unit is Focusing and must remain stationary." :
                !unit.reactionMove && unit.side != state.activeSide ? "Movement is only available during the " + unit.side.ToUpperInvariant() + " turn." :
                !unit.reactionMove && unit.movesMade >= movesAllowed ? "This unit has used all of its movement for the turn." : string.Empty;

            var move = Describe("MOVE", "Reposition on the tabletop.", "Movement", "Active-side unit with unused movement.",
                "Click open terrain up to " + (TacticalRules.MovementAllowance(unit, state.impairedMovement) / (unit.sprint ? 2f : 1f)).ToString("0.#") + "\" away. Facing follows movement.", string.IsNullOrEmpty(moveReason), moveReason);
            if (ActionMenuButton(move)) { notice = "MOVE ready: click an open point on the tabletop within the unit's allowance."; audio.Play(SoundCue.Click); }

            var attackReason = generalReason;
            if (string.IsNullOrEmpty(attackReason) && weapon == null) attackReason = "This unit has no ranged weapon.";
            else if (string.IsNullOrEmpty(attackReason) && target == null) attackReason = "Select an opposing miniature as the target.";
            else if (string.IsNullOrEmpty(attackReason) && !opposingTarget) attackReason = "Fire requires an opposing target.";
            else if (string.IsNullOrEmpty(attackReason) && selectedLos.classification == "blocked") attackReason = "Terrain or a miniature blocks line of sight.";
            else if (string.IsNullOrEmpty(attackReason) && targetDistance > weapon.range) attackReason = string.Format("Target is {0:0.0}\" away; {1} range is {2:0.#}\".", targetDistance, weapon.name, weapon.range);
            var weaponName = weapon == null ? "unarmed" : weapon.name;
            var weaponRange = weapon == null ? "No weapon equipped." : string.Format("Opposing target within {0:0.#}\" and an open or partial LOS.", weapon.range);
            if (ActionMenuButton(Describe("FIRE · " + weaponName, "Attack to cause a casualty.", "1 action", weaponRange,
                "Roll the weapon attack against the target's defense and current cover.", string.IsNullOrEmpty(attackReason), attackReason))) Fire(false);
            var suppressReason = generalReason;
            if (string.IsNullOrEmpty(suppressReason) && weapon == null) suppressReason = "This unit has no ranged weapon.";
            else if (string.IsNullOrEmpty(suppressReason) && target == null) suppressReason = "Select an opposing miniature as the target.";
            else if (string.IsNullOrEmpty(suppressReason) && !opposingTarget) suppressReason = "Suppression requires an opposing target.";
            else if (string.IsNullOrEmpty(suppressReason) && targetDistance > weapon.range) suppressReason = string.Format("Target is {0:0.0}\" away; {1} range is {2:0.#}\".", targetDistance, weapon.name, weapon.range);
            else if (string.IsNullOrEmpty(suppressReason) && !CanAimSuppression(selectedLos)) suppressReason = "The first visible aim point is more than 6\" from the concealed target.";
            if (ActionMenuButton(Describe("SUPPRESS", "Pin an enemy instead of wounding it.", "1 action", "Target in the weapon's cone or radius; direct LOS is not required if the attacker can aim within 6\".",
                "Roll Skill only; success gives the target Disadvantage until this side's next turn.", string.IsNullOrEmpty(suppressReason), suppressReason))) Fire(true);

            var reactionReason = canAct && !unit.reaction ? string.Empty : unit.reaction ? "This unit is already holding a reaction." : generalReason;
            if (ActionMenuButton(Describe("HOLD REACTION", "Reserve an attack for the enemy turn.", "1 action", "Effective unit with an unused action.",
                "The unit may Fire during the opposing side's turn.", string.IsNullOrEmpty(reactionReason), reactionReason)))
            { unit.actionUsed = true; unit.reaction = true; AddEvent(unit.name + " holds a reaction.", "action"); audio.Play(SoundCue.Click); Save(); }

            var sprintReason = !canAct ? generalReason : unit.kind != "troop" ? "Only troop units may sprint." : unit.sprint ? "This unit is already sprinting." : string.Empty;
            if (ActionMenuButton(Describe("SPRINT", unit.reaction ? "Move once during the opposing turn." : "Take a second Move this turn.", "1 action", "Troop unit with an unused action or saved Reaction.",
                unit.reaction ? "Converts the saved Reaction into one Move at normal speed." : "Allows two separate Moves this turn; each uses the normal Move rating.", string.IsNullOrEmpty(sprintReason), sprintReason)))
            {
                if (unit.reaction) { unit.reaction = false; unit.reactionMove = true; unit.actionUsed = true; AddEvent(unit.name + " uses its reaction to sprint; choose a destination.", "move"); }
                else { unit.actionUsed = true; unit.sprint = true; AddEvent(unit.name + " sacrifices its action for a second Move.", "move"); }
                audio.Play(SoundCue.Move); Save();
            }

            var radioReason = !canAct ? generalReason : !unit.radio ? "This unit is not equipped with a radio." :
                target == null ? "Select an opposing miniature as the target." : !opposingTarget ? "Observation requires an opposing target." :
                selectedLos.classification == "blocked" ? "The observer must have line of sight to the target." : string.Empty;
            if (ActionMenuButton(Describe("RADIO FIRES OBSERVATION", "Mark a visible enemy for friendly fires.", "1 action", "Radio-equipped unit with LOS to a selected opposing target.",
                "Gives friendly attacks against the target Advantage until this observer's next turn.", string.IsNullOrEmpty(radioReason), radioReason)))
            { ConsumeAction(unit); target.observedBy = unit.side; target.observedRound = state.round; AddEvent(unit.name + " observes " + target.name + " for friendly fires.", "signal"); audio.Play(SoundCue.Objective); Save(); }

            var treatReason = !canAct ? generalReason : unit.medicalSkill <= 0 ? "This unit lacks the required medical training and equipment." :
                unit.moved ? "Focused medical care requires the unit to remain stationary for the entire turn." : target == null ? "Select a downed friendly casualty." :
                target.side != unit.side || target.status != "downed" ? "The target must be a downed friendly." :
                targetDistance > 1.5f ? string.Format("Move adjacent first; the casualty is {0:0.0}\" away.", targetDistance) : string.Empty;
            if (ActionMenuButton(Describe("TREAT CASUALTY · FOCUS", "Attempt to revive a downed friendly.", "Entire turn (Focus)", "Medically equipped unit; bases touching (within 1.5\"); no prior movement or action.",
                "Roll medical Skill on the full Rules Table 2-2; the result determines the casualty's new status.", string.IsNullOrEmpty(treatReason), treatReason))) Treat(unit, target);

            var relayDistance = TacticalRules.Distance(unit.x, unit.y, 73f, 22f, request.board);
            var relayReason = !canAct ? generalReason : unit.side != "blue" ? "Only BLUE units can complete this mission objective." :
                relayDistance > 18f ? string.Format("Move within 18\" of the relay; currently {0:0.0}\" away.", relayDistance) : string.Empty;
            if (ActionMenuButton(Describe("OBSERVE RELAY", "Advance the relay observation objective.", "1 action", "BLUE unit within 18\" of the relay.",
                "Adds one uninterrupted observation turn toward the mission objective.", string.IsNullOrEmpty(relayReason), relayReason))) ObserveRelay(unit);

            GUILayout.Space(5);
            GUILayout.Label("MOUSE-OVER ACTION HELP", smallStyle);
            GUILayout.Box(string.IsNullOrEmpty(hoveredActionHelp) ? "Point at any action above to see its cost, requirements, effect, and current availability." : hoveredActionHelp, guideStyle);
        }

        ActionDescriptor Describe(string title, string summary, string cost, string requirements, string effect, bool available, string unavailable)
        {
            return new ActionDescriptor { title = title, summary = summary, cost = cost, requirements = requirements, effect = effect, available = available, unavailable = unavailable };
        }

        bool ActionMenuButton(ActionDescriptor action)
        {
            var status = action.available ? "<color=#91d6be>READY</color>" : "<color=#9aa39c>UNAVAILABLE</color>";
            var visible = "<b>" + action.title + "</b>  " + status + "\n" + action.summary;
            var availability = action.available ? "<color=#91d6be>AVAILABLE NOW</color>" : "<color=#e1b275>UNAVAILABLE:</color> " + action.unavailable;
            var detail = "<b>" + action.title + "</b>\n<b>COST:</b> " + action.cost + "\n<b>REQUIRES:</b> " + action.requirements + "\n<b>EFFECT:</b> " + action.effect + "\n" + availability;
            var clicked = GUILayout.Button(new GUIContent(visible, detail), action.available ? actionButtonStyle : unavailableActionStyle, GUILayout.Height(43f));
            if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoveredActionHelp = detail;
            if (!clicked) return false;
            if (action.available) return true;
            notice = action.unavailable; audio.Play(SoundCue.Error); return false;
        }

        string ActionStateReason(UnitData unit)
        {
            if (!Effective(unit)) return "This unit cannot act while " + unit.status + ".";
            if (unit.reaction) return string.Empty;
            if (unit.side != state.activeSide) return "Wait for the " + unit.side.ToUpperInvariant() + " turn.";
            if (unit.actionUsed) return "This unit has already used its action.";
            return string.Empty;
        }

        bool CanAimSuppression(BattleLosResult los)
        {
            if (los == null) return false;
            if (los.classification != "blocked") return true;
            return los.blockerDistance >= 0f && los.distance - los.blockerDistance <= 6.05f;
        }

        bool ActionButton(string label, string tooltip, float height = 23f)
        {
            return GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Height(height));
        }

        void DrawTurnGuide(UnitData unit, UnitData target)
        {
            GUILayout.Label("WHAT CAN THIS UNIT DO?", smallStyle);
            if (losTool)
            {
                var step = !losStartSet ? "1. Click the LOS starting point." : !losEndSet ? "2. Click the LOS endpoint." : "Measurement complete. Click again to start a new line.";
                GUILayout.Box("<color=#91d6be>LOS TOOL ACTIVE</color>\n" + step + "\nOpen = green · Partial = amber · Blocked = red\nRight-click resets · L closes the tool.", guideStyle);
                return;
            }
            string move;
            if (!Effective(unit)) move = "<color=#e18a75>× Cannot move while " + unit.status + ".</color>";
            else if (unit.focused) move = "<color=#9aa39c>✓ Focus requires remaining stationary.</color>";
            else if (!unit.reactionMove && unit.side != state.activeSide) move = "<color=#9aa39c>○ Wait for the " + unit.side.ToUpperInvariant() + " turn.</color>";
            else if (!unit.reactionMove && unit.movesMade >= (unit.sprint ? 2 : 1)) move = "<color=#9aa39c>✓ Movement used.</color>";
            else move = "<color=#91d6be>1. MOVE: click the map, up to " + (TacticalRules.MovementAllowance(unit, state.impairedMovement) / (unit.sprint ? 2f : 1f)).ToString("0.#") + "\".</color>";
            string action;
            if (!Effective(unit)) action = "<color=#e18a75>× Cannot take actions.</color>";
            else if (unit.side != state.activeSide && !unit.reaction) action = "<color=#9aa39c>○ No action during this side's turn.</color>";
            else if (unit.reaction) action = "<color=#f1c66b>2. REACTION READY: select an enemy and Fire.</color>";
            else if (unit.actionUsed) action = "<color=#9aa39c>✓ Action used.</color>";
            else if (target == null) action = "<color=#91d6be>2. ACTION: select a target or choose a utility action.</color>";
            else action = "<color=#91d6be>2. ACTION: target " + target.name + " is selected; choose an action.</color>";
            GUILayout.Box(move + "\n" + action + "\n3. END TURN when all units are finished (Space).", guideStyle);
            GUILayout.Label("Hover any miniature, roster entry, or button for details · F1 opens the full guide", smallStyle);
        }

        void Fire(bool suppress)
        {
            var attacker = Unit(state.selectedId); var target = Unit(state.targetId); var weapon = attacker?.weapons?.FirstOrDefault();
            BattleLosResult los = null;
            if (terrain != null && terrain.Ready && attacker != null && target != null) { los = SelectedLos(attacker, target); state.cover = los.classification; }
            var result = TacticalRules.Attack(attacker, target, weapon, request.board, state.round, state.cover, suppress, dice, CanAimSuppression(los));
            if (!result.valid) { notice = result.reason; audio.Play(SoundCue.Error); return; }
            audio.Play(suppress ? SoundCue.Suppress : SoundCue.Fire);
            ConsumeAction(attacker); state.alarm = true;
            if (!result.hit) AddEvent(string.Format("{0} misses {1} (skill {2} vs {3}).", attacker.name, target.name, RollText(result.skill), weapon.difficulty), "miss");
            else if (suppress) { target.suppressed = true; target.suppressedBySide = attacker.side; AddEvent(attacker.name + " suppresses " + target.name + ".", "suppress"); }
            else if (result.casualty) { target.status = "downed"; target.reaction = false; AddEvent(string.Format("{0} downs {1} (damage {2} vs defense {3}).", attacker.name, target.name, RollText(result.damage), result.defense), "hit"); audio.Play(SoundCue.Hit); }
            else AddEvent(string.Format("{0} hits {1}, but causes no casualty (damage {2} vs defense {3}).", attacker.name, target.name, RollText(result.damage), result.defense), "hit");
            Save();
        }

        void Treat(UnitData medic, UnitData target)
        {
            if (medic == null || medic.medicalSkill <= 0) { notice = "This unit lacks the required medical training and equipment."; audio.Play(SoundCue.Error); return; }
            if (medic.moved) { notice = "Medical treatment requires Focus; the treating unit must not move this turn."; audio.Play(SoundCue.Error); return; }
            var range = TacticalRules.Distance(medic, target, request.board); if (range > 1.5f) { notice = string.Format("Move adjacent first ({0:0.0}\" away).", range); audio.Play(SoundCue.Error); return; }
            ConsumeAction(medic); medic.focused = true; medic.moved = true; medic.movesMade = 1; DieRoll roll; target.status = TacticalRules.Medicine(medic, dice, out roll);
            AddEvent(string.Format("{0} treats {1}: {2} — {3}.", medic.name, target.name, RollText(roll), target.status), "medical"); audio.Play(SoundCue.Medical); Save();
        }
        void ObserveRelay(UnitData unit)
        {
            var range = TacticalRules.Distance(unit.x, unit.y, 73f, 22f, request.board); if (range > 18f) { notice = string.Format("Move within 18\" of the relay ({0:0.0}\" now).", range); audio.Play(SoundCue.Error); return; }
            ConsumeAction(unit); state.observationTurns = Mathf.Clamp(state.observationTurns + 1, 0, 2); AddEvent(string.Format("{0} completes relay observation ({1}/2).", unit.name, state.observationTurns), "objective"); audio.Play(SoundCue.Objective); Save();
        }

        void EndTurn()
        {
            var side = state.activeSide;
            foreach (var unit in state.units.Where(item => item.side == side && Effective(item))) if (!unit.actionUsed) unit.reaction = true;
            if (!state.firstSideFinished) { state.firstSideFinished = true; StartSide(side == "blue" ? "red" : "blue"); }
            else { state.round++; RollInitiative(); StartSide(state.activeSide); AddEvent(string.Format("Initiative: BLUE {0}, RED {1}. {2} acts first.", state.blueInitiative, state.redInitiative, state.activeSide.ToUpperInvariant()), "system"); }
            audio.Play(SoundCue.Turn); Save();
        }
        void StartSide(string side)
        {
            state.activeSide = side;
            foreach (var unit in state.units.Where(item => item.side == side)) { unit.actionUsed = false; unit.moved = false; unit.movesMade = 0; unit.focused = false; unit.sprint = false; unit.reaction = false; unit.reactionMove = false; }
            foreach (var unit in state.units.Where(item => item.suppressedBySide == side)) { unit.suppressed = false; unit.suppressedBySide = ""; }
            foreach (var unit in state.units.Where(item => item.observedBy == side)) { unit.observedBy = ""; unit.observedRound = 0; }
            AddEvent(side.ToUpperInvariant() + " turn begins.", "system");
        }

        void FinishBattle()
        {
            SetObjective("o1", state.observationTurns >= 2); SetObjective("o2", state.observationTurns >= 2);
            var blue = state.units.Where(unit => unit.side == "blue").ToArray(); var effective = blue.Count(Effective);
            SetObjective("o3", blue.Length > 0 && (float)effective / blue.Length >= .75f); SetObjective("o4", !state.alarm);
            var scoreAvailable = state.objectives.Sum(objective => objective.points); var scoreEarned = state.objectives.Where(objective => objective.complete).Sum(objective => objective.points);
            var outcome = scoreAvailable <= 0 ? "Mission complete" : scoreEarned == scoreAvailable ? "Decisive success" : scoreEarned * 2 >= scoreAvailable ? "Partial success" : "Mission setback";
            var result = new BattleResult
            {
                requestId = request.requestId, resultId = Guid.NewGuid().ToString("N"), completedAt = DateTime.UtcNow.ToString("O"), missionNumber = request.mission.number,
                rounds = state.round, alarm = state.alarm, observationTurns = state.observationTurns, events = state.events,
                scoreEarned = scoreEarned, scoreAvailable = scoreAvailable, outcome = outcome, terrainLocationId = request.mission.locationId,
                units = state.units.Select(unit => new UnitResult { id = unit.id, x = unit.x, y = unit.y, facing = unit.facing, status = unit.status }).ToArray(),
                objectives = state.objectives.Select(objective => new ObjectiveResult { id = objective.id, complete = objective.complete }).ToArray(),
                casualties = blue.Where(unit => unit.status == "downed" || unit.status == "dead").Select(unit => new CasualtyResult { unitId = unit.id, category = unit.status == "dead" ? "KIA" : "WIA-S" }).ToArray()
            };
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true)); state.completed = true; AddEvent(string.Format("Battle result exported: {0}, {1}/{2} objective points.", outcome, scoreEarned, scoreAvailable), "objective"); audio.Play(SoundCue.Objective); Save(); notice = "Result exported and ready for automatic campaign import. You may return to Campaign Command.";
        }
        void SetObjective(string id, bool complete) { var objective = state.objectives.FirstOrDefault(item => item.id == id); if (objective != null) objective.complete = complete; }
        void AddEvent(string text, string kind)
        {
            var events = new List<BattleEvent>(state.events ?? new BattleEvent[0]) { new BattleEvent { round = state.round, text = text, kind = kind } }; state.events = events.ToArray(); notice = text;
        }
        void Save() { if (state == null || string.IsNullOrEmpty(statePath)) return; state.rollCount = dice?.RollCount ?? state.rollCount; File.WriteAllText(statePath, JsonUtility.ToJson(state, true)); }

        void DrawFooter(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); var left = new Rect(rect.x + 10, rect.y + 8, rect.width * .48f, rect.height - 16); var right = new Rect(rect.x + rect.width * .49f, rect.y + 8, rect.width * .5f, rect.height - 16);
            GUILayout.BeginArea(left); GUILayout.Label("MISSION OBJECTIVES", smallStyle); GUILayout.BeginHorizontal(); var objectiveWidth = Mathf.Max(70f, (left.width - 8f) / Mathf.Max(1, state.objectives.Length)); foreach (var objective in state.objectives) GUILayout.Box((objective.complete ? "✓ " : "○ ") + objective.text, objectiveStyle, GUILayout.Width(objectiveWidth), GUILayout.Height(50)); GUILayout.EndHorizontal(); GUILayout.EndArea();
            GUILayout.BeginArea(right); GUILayout.Label("COMBAT LOG", smallStyle); logScroll = GUILayout.BeginScrollView(logScroll); foreach (var entry in state.events.Reverse().Take(8)) GUILayout.Label("R" + entry.round + "  " + entry.text, logStyle); GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawTooltip()
        {
            if (string.IsNullOrWhiteSpace(GUI.tooltip)) return;
            var width = Mathf.Min(330f, Screen.width - 24f);
            var height = tooltipStyle.CalcHeight(new GUIContent(GUI.tooltip), width);
            var point = Event.current.mousePosition + new Vector2(16f, 18f);
            if (point.x + width > Screen.width - 8f) point.x = Screen.width - width - 8f;
            if (point.y + height > Screen.height - 8f) point.y -= height + 24f;
            GUI.depth = -100; GUI.Box(new Rect(point.x, point.y, width, height), GUI.tooltip, tooltipStyle);
        }

        void DrawHelpOverlay()
        {
            GUI.depth = -200;
            var shade = GUI.color; GUI.color = new Color(0f, 0f, 0f, .72f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), pixel); GUI.color = shade;
            var width = Mathf.Min(670f, Screen.width - 40f); var height = Mathf.Min(600f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 20, rect.width - 48, rect.height - 40));
            GUILayout.BeginHorizontal(); GUILayout.Label("HOW A TURN WORKS", titleStyle); GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("CLOSE  ×", "Close this guide. Shortcuts: F1 or Escape."), GUILayout.Width(95), GUILayout.Height(30))) { showHelp = false; audio.Play(SoundCue.Click); }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("1  SELECT", titleStyle);
            GUILayout.Label("Choose a unit on the map or in the roster. Only the active side can move and take normal actions. Clicking an enemy makes it the target; clicking a downed friendly lets a medic target that casualty.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("2  MOVE", titleStyle);
            GUILayout.Label("Click an empty map position within the selected unit's allowance. A unit normally moves once per turn. Toggle Impaired movement before moving through mud, climbing, or crawling.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("3  ACT", titleStyle);
            GUILayout.Label("Use the scrollable UNIT ACTIONS menu in the right panel. Every action remains visible with a short description and READY or UNAVAILABLE state. Point at a button for its cost, requirements, full effect, and the exact reason it cannot currently be used. Unity checks eye-height LOS before Fire. Suppress can instead use a visible aim point within 6\" of a concealed target, as allowed by the full rules.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("4  REACT OR END", titleStyle);
            GUILayout.Label("Hold Reaction deliberately, or simply end the turn: every effective unit that has not acted automatically holds a reaction. Reaction fire is available during the opposing side's turn.", guideStyle);
            GUILayout.Space(10); GUILayout.Box("CURRENT SIDE: " + state.activeSide.ToUpperInvariant() + "    ·    ROUND " + state.round + "    ·    L: LOS tool    ·    Space: end turn    ·    F1: help", guideStyle);
            GUILayout.Space(8); GUILayout.Label("Tip: action details appear both beneath the menu and beside the mouse pointer. Unavailable buttons can also be clicked to place their blocking reason in STATUS.", smallStyle);
            GUILayout.FlexibleSpace();
            var enabled = audio.Enabled; var next = GUILayout.Toggle(enabled, new GUIContent(" Tactical sound", "Enable or mute the procedural offline sound cues.")); if (next != enabled) { audio.Enabled = next; audio.Play(SoundCue.Click); }
            GUILayout.EndArea();
        }

        void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var matrix = GUI.matrix; var angle = Vector3.Angle(end - start, Vector2.right); if (start.y > end.y) angle = -angle;
            GUI.color = color; GUIUtility.RotateAroundPivot(angle, start); GUI.DrawTexture(new Rect(start.x, start.y, (end - start).magnitude, width), pixel); GUI.matrix = matrix; GUI.color = Color.white;
        }
    }
}
