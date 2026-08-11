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
        bool showRulesTrace;
        bool traceNewestFirst = true;
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
        Vector2 traceScroll;
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
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; showRulesTrace = false; audio.Play(SoundCue.Click); }
            if (Input.GetKeyDown(KeyCode.F2)) { showRulesTrace = !showRulesTrace; showHelp = false; audio.Play(SoundCue.Click); }
            if ((showHelp || showRulesTrace) && Input.GetKeyDown(KeyCode.Escape)) { showHelp = false; showRulesTrace = false; audio.Play(SoundCue.Click); }
            if (state != null && !showHelp && !showRulesTrace) terrain?.UpdateInput();
            if (state != null && miniatures != null) miniatures.Sync(state.units, state.selectedId, state.targetId);
            if (state == null || state.completed || showHelp || showRulesTrace) return;
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
            audio.Play(SoundCue.Los);
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
            NormalizeObjectiveDefinitions();
            if (state.calculations == null) state.calculations = new RuleCalculation[0];
            if (state.nextCalculationSequence <= 0) state.nextCalculationSequence = state.calculations.Length == 0 ? 1 : state.calculations.Max(item => item.sequence) + 1;
            foreach (var item in state.calculations.Where(item => item.rulePage <= 0 && string.IsNullOrEmpty(item.ruleSection))) RuleCitation(item.category, item.command, out item.ruleSection, out item.rulePage);
            foreach (var unit in state.units ?? new UnitData[0]) if (unit.moved && unit.movesMade == 0) unit.movesMade = 1;
            dice = new DeterministicDice(request.settings.seed, state.rollCount);
            LoadMap();
            terrain = new ProceduralBattleTerrain(request.board);
            miniatures = new CampaignMiniatureSet(terrain, state.units);
            miniatures.Sync(state.units, state.selectedId, state.targetId);
        }

        void NormalizeObjectiveDefinitions()
        {
            foreach (var objective in state.objectives ?? new ObjectiveData[0])
            {
                var definition = request.objectives?.FirstOrDefault(item => item.id == objective.id);
                if (definition != null && !string.IsNullOrEmpty(definition.type))
                {
                    var complete = objective.complete; var progress = objective.progress; var identified = objective.identifiedUnitIds; var lastRound = objective.lastProgressRound;
                    objective.text = definition.text; objective.points = definition.points; objective.type = definition.type; objective.actionLabel = definition.actionLabel; objective.side = definition.side;
                    objective.x = definition.x; objective.y = definition.y; objective.radius = definition.radius; objective.requiredProgress = definition.requiredProgress; objective.difficulty = definition.difficulty;
                    objective.uninterrupted = definition.uninterrupted; objective.requiresLos = definition.requiresLos; objective.threshold = definition.threshold; objective.edge = definition.edge; objective.depth = definition.depth;
                    objective.targetUnitIds = definition.targetUnitIds; objective.complete = complete; objective.progress = progress; objective.identifiedUnitIds = identified; objective.lastProgressRound = lastRound;
                }
                if (string.IsNullOrEmpty(objective.side)) objective.side = "blue";
                if (objective.requiredProgress <= 0) objective.requiredProgress = 1;
                if (objective.identifiedUnitIds == null) objective.identifiedUnitIds = new string[0];
                if (!string.IsNullOrEmpty(objective.type)) continue;
                if (objective.id == "o1") { objective.type = "observe-zone"; objective.actionLabel = "OBSERVE RELAY"; objective.x = 73f; objective.y = 22f; objective.radius = 18f; objective.requiredProgress = 2; objective.uninterrupted = true; objective.requiresLos = true; }
                else if (objective.id == "o2") { objective.type = "identify-units"; objective.actionLabel = "IDENTIFY RELAY PERSONNEL"; objective.radius = 24f; objective.requiredProgress = 2; objective.difficulty = 4; objective.requiresLos = true; objective.targetUnitIds = new[] { "r5", "r3", "r4" }; }
                else if (objective.id == "o3") { objective.type = "extract-force"; objective.threshold = .75f; objective.edge = "south"; objective.depth = 18f; }
                else if (objective.id == "o4") { objective.type = "avoid-alarm"; objective.radius = 36f; objective.difficulty = 4; objective.requiresLos = true; }
            }
            foreach (var objective in state.objectives ?? new ObjectiveData[0])
                if (objective.type == "extract-force") foreach (var unit in state.units.Where(item => item.side == objective.side && !TacticalRules.InExtractionZone(item, objective))) unit.enteredField = true;
        }

        void LoadMap()
        {
            if (request.board == null || string.IsNullOrWhiteSpace(request.board.mapPath) || !File.Exists(request.board.mapPath)) return;
            mapTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!mapTexture.LoadImage(File.ReadAllBytes(request.board.mapPath))) mapTexture = null;
        }

        void RollInitiative()
        {
            var rolls = new List<string>();
            do { state.blueInitiative = dice.Roll(6); state.redInitiative = dice.Roll(6); rolls.Add(string.Format("BLUE d6={0}, RED d6={1}{2}", state.blueInitiative, state.redInitiative, state.blueInitiative == state.redInitiative ? " (tie: reroll both)" : "")); } while (state.blueInitiative == state.redInitiative);
            state.activeSide = state.blueInitiative > state.redInitiative ? "blue" : "red";
            state.firstSide = state.activeSide; state.firstSideFinished = false;
            Trace("Initiative", "Determine first side", "Each side rolls d6; ties reroll both dice.", string.Join("; ", rolls), state.activeSide.ToUpperInvariant() + " acts first.");
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
        bool CanAct(UnitData unit) { return Effective(unit) && unit.side == state.activeSide && !unit.actionUsed && state.pendingTrigger == null; }
        void ConsumeAction(UnitData unit) { unit.actionUsed = true; }
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
                GUI.Label(new Rect(17, 34, 650, 20), string.Format("ROUND {0}/{1}  ·  {2} TURN  ·  {3}  ·  INIT B{4}/R{5}  ·  RULES {6}", state.round, Mathf.Max(1, request.mission.durationTurns), state.activeSide.ToUpperInvariant(), request.mission.locationName, state.blueInitiative, state.redInitiative, request.rulesVersion), smallStyle);
                if (GUI.Button(new Rect(width - 606, 16, 68, 34), new GUIContent(audio.Enabled ? "SOUND" : "MUTED", "Toggle all tactical sound cues. This preference is saved."))) { audio.Enabled = !audio.Enabled; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 530, 16, 106, 34), new GUIContent("RULES TRACE", "Open the complete rules-computation audit. Shortcut: F2."))) { showRulesTrace = true; showHelp = false; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 416, 16, 82, 34), new GUIContent("TURN HELP", "Open the turn sequence and action reference. Shortcut: F1."))) { showHelp = true; showRulesTrace = false; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 326, 16, 112, 34), new GUIContent("END TURN", "Finish this side's turn. Units that did not act will automatically hold a reaction. Shortcut: Space."))) EndTurn();
                if (GUI.Button(new Rect(width - 206, 16, 190, 34), new GUIContent(state.completed ? "QUIT TO TRACKER" : "END MISSION", state.completed ? "Close the tactical game and return to Campaign Command." : "Score objectives and export the battle result to the campaign tracker."))) { if (state.completed) { audio.Play(SoundCue.Click); Application.Quit(); } else FinishBattle(); }
            }
            if (request == null || state == null) { GUI.Label(new Rect(30, 100, width - 60, 80), notice, titleStyle); return; }

            var rosterRect = new Rect(0, header, left, height - header - footer);
            var inspectorRect = new Rect(width - right, header, right, height - header - footer);
            var stageRect = new Rect(left, header, width - left - right, height - header - footer);
            if (showHelp) { DrawHelpOverlay(); return; }
            if (showRulesTrace) { DrawRulesTraceOverlay(); return; }
            DrawRoster(rosterRect); DrawBoard(stageRect); DrawInspector(inspectorRect); DrawFooter(new Rect(0, height - footer, width, footer));
            if (state.pendingTrigger != null) DrawReactionPrompt();
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

            if (state.showMissionZones) DrawExtractionZone(board);
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
            var reactionMove = !string.IsNullOrEmpty(state.reactionMoveUnitId);
            var reactionPromptBlocksPoint = state.pendingTrigger != null && !reactionMove || (reactionMove && ReactionPromptRect().Contains(current.mousePosition));
            if (!losTool && !reactionPromptBlocksPoint && current.type == EventType.MouseUp && board.Contains(current.mousePosition))
            {
                var tokenHit = state.units.Any(unit => TokenRect(board, unit).Contains(current.mousePosition));
                if (!tokenHit && TryMoveSelected(board, current.mousePosition)) current.Use();
            }
            var boardHint = losTool ? "LOS TOOL · left-click two points · right-click resets · L closes" : "IMPORTED 3D MINIATURES · WASD / arrows pan · middle-drag rotates/skews · wheel zooms · select a miniature, then click its destination";
            GUI.Label(new Rect(stage.x + 12, stage.yMax - 20, stage.width - 24, 18), boardHint, smallStyle);
        }

        void DrawExtractionZone(Rect board)
        {
            foreach (var objective in state.objectives.Where(item => item.type == "extract-force"))
            {
                var depth = Mathf.Clamp(objective.depth > 0f ? objective.depth : 15f, 1f, 49f) / 100f;
                Vector2 first; Vector2 second; Vector2 label;
                if (objective.edge == "north") { first = LosPoint(board, new Vector2(0f, depth)); second = LosPoint(board, new Vector2(1f, depth)); label = LosPoint(board, new Vector2(.5f, depth * .5f)); }
                else if (objective.edge == "west") { first = LosPoint(board, new Vector2(depth, 0f)); second = LosPoint(board, new Vector2(depth, 1f)); label = LosPoint(board, new Vector2(depth * .5f, .5f)); }
                else if (objective.edge == "east") { first = LosPoint(board, new Vector2(1f - depth, 0f)); second = LosPoint(board, new Vector2(1f - depth, 1f)); label = LosPoint(board, new Vector2(1f - depth * .5f, .5f)); }
                else { first = LosPoint(board, new Vector2(0f, 1f - depth)); second = LosPoint(board, new Vector2(1f, 1f - depth)); label = LosPoint(board, new Vector2(.5f, 1f - depth * .5f)); }
                DrawLine(first, second, new Color(.26f, .9f, .72f, .9f), 3f);
                GUI.Box(new Rect(label.x - 70f, label.y - 15f, 140f, 30f), "FRIENDLY EXFIL ZONE", smallStyle);
            }
        }

        void HandleLosTool(Rect board)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown || !board.Contains(current.mousePosition)) return;
            if (current.button == 1)
            {
                losStartSet = false; losEndSet = false; measuredLos = null; notice = "LOS measurement reset. Click the first point."; audio.Play(SoundCue.Los); current.Use(); return;
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
                Trace("Line of sight", "Measure two tabletop points", string.Format("Start {0:0.0}%,{1:0.0}% · End {2:0.0}%,{3:0.0}%", losStart.x * 100f, losStart.y * 100f, losEnd.x * 100f, losEnd.y * 100f), string.Format("Eye-height physics trace · distance {0:0.0}\" · first blocker {1}", LosDistance(losStart, losEnd), string.IsNullOrEmpty(measuredLos.blocker) ? "none" : measuredLos.blocker), measuredLos.classification.ToUpperInvariant());
            }
            audio.Play(SoundCue.Los); current.Use();
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
            var allowance = TacticalRules.MovementAllowance(unit, false);
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
            var interruptMove = !string.IsNullOrEmpty(state.reactionMoveUnitId);
            var unit = Unit(interruptMove ? state.reactionMoveUnitId : state.selectedId);
            var reactionMove = interruptMove || unit != null && unit.reactionMove;
            var movesAllowed = unit != null && unit.sprint ? 2 : 1;
            if (unit == null || !Effective(unit) || unit.focused || (!reactionMove && (unit.side != state.activeSide || unit.movesMade >= movesAllowed))) { var reason = unit == null ? "No unit selected." : !Effective(unit) ? "Unit is " + unit.status + "." : unit.focused ? "Focus requires remaining stationary." : unit.side != state.activeSide ? "It is the opposing side's turn." : "All permitted movement segments were used."; Trace("Movement", "Declare Move", unit?.name ?? "No unit", reason, "REJECTED before path measurement."); notice = "That unit cannot move now."; audio.Play(SoundCue.Error); return false; }
            float x, y;
            if (terrain != null && terrain.Ready) { if (!terrain.TryPercent(mouse, out x, out y)) { Trace("Movement", "Choose Move destination", unit.name, "Pointer ray did not intersect legal tabletop terrain.", "REJECTED."); notice = "Choose a point on the tabletop."; return false; } }
            else { x = Mathf.Clamp((mouse.x - board.x) / board.width * 100f, 0f, 100f); y = Mathf.Clamp((mouse.y - board.y) / board.height * 100f, 0f, 100f); }
            var path = MovementPath(unit, unit.x, unit.y, x, y); var allowance = TacticalRules.MovementAllowance(unit, false) / (unit.sprint && !reactionMove ? 2f : 1f);
            var moveInputs = string.Format("{0} from {1:0.0}%,{2:0.0}% to {3:0.0}%,{4:0.0}% · base Move {5:0.0}\"", unit.name, unit.x, unit.y, x, y, allowance);
            var moveMath = string.Format("Measured {0:0.00}\" + impaired surcharge {1:0.00}\" = effective cost {2:0.00}\"; allowance {3:0.00}\"", path.distance, path.impairedDistance, path.cost, allowance);
            if (!path.valid) { Trace("Movement", "Declare Move", moveInputs, path.reason, "REJECTED: impassable path."); notice = path.reason; audio.Play(SoundCue.Error); return false; }
            if (path.cost > allowance + .05f) { Trace("Movement", "Declare Move", moveInputs, moveMath, "REJECTED: effective cost exceeds allowance."); notice = string.Format("Path costs {0:0.0}\" ({1:0.0}\" measured, {2:0.0}\" impaired); allowance is {3:0.0}\".", path.cost, path.distance, path.impairedDistance, allowance); audio.Play(SoundCue.Error); return false; }
            if (!reactionMove)
            {
                Trace("Movement", "Declare Move", moveInputs, moveMath, "LEGAL declaration; check opposing saved Reactions before resolution.");
                if (BeginReactionWindow(new PendingTriggerData { kind = "move", actorId = unit.id, destinationX = x, destinationY = y })) return true;
            }
            ExecuteMovement(unit, x, y, path, reactionMove);
            if (interruptMove) { state.reactionMoveUnitId = ""; AdvanceReactionQueue(); }
            return true;
        }

        MovementPathResult MovementPath(UnitData unit, float startX, float startY, float endX, float endY)
        {
            if (terrain != null && terrain.Ready) return terrain.EvaluateMovementPath(unit, startX, startY, endX, endY, state.impairedMovement);
            var distance = TacticalRules.Distance(startX, startY, endX, endY, request.board);
            return new MovementPathResult { distance = distance, impairedDistance = state.impairedMovement ? distance : 0f, cost = distance * (state.impairedMovement ? 2f : 1f) };
        }

        void ExecuteMovement(UnitData unit, float x, float y, MovementPathResult path, bool reactionMove)
        {
            var oldX = unit.x; var oldY = unit.y;
            miniatures?.FaceMovement(unit, oldX, oldY, x, y);
            unit.x = x; unit.y = y; unit.moved = true; unit.movesMade++; unit.reactionMove = false;
            foreach (var objective in state.objectives.Where(item => item.type == "extract-force" && item.side == unit.side)) if (!TacticalRules.InExtractionZone(unit, objective)) unit.enteredField = true;
            if (reactionMove) unit.reaction = false;
            var terrainText = path.impairedDistance > .01f ? string.Format("; {0:0.0}\" impaired, cost {1:0.0}\"", path.impairedDistance, path.cost) : "";
            Trace("Movement", reactionMove ? "Resolve Reaction Sprint" : "Resolve Move", string.Format("{0} · measured {1:0.00}\" · impaired {2:0.00}\"", unit.name, path.distance, path.impairedDistance), string.Format("{0:0.00}\" normal + {1:0.00}\" impaired surcharge = {2:0.00}\" cost", path.distance, path.impairedDistance, path.cost), string.Format("ACCEPTED at {0:0.0}%,{1:0.0}%; facing follows movement.", x, y));
            AddEvent(string.Format("{0} moves {1:0.0}\"{2}{3}.", unit.name, path.distance, terrainText, reactionMove ? " as a Reaction before the trigger" : ""), "move"); audio.Play(SoundCue.Move); EvaluateDetection(unit, "movement"); Save();
        }

        bool BeginReactionWindow(PendingTriggerData trigger)
        {
            if (trigger == null || state.pendingTrigger != null) return false;
            var actor = Unit(trigger.actorId); if (!Effective(actor)) return false;
            var candidates = state.units.Where(unit => unit.side != actor.side && unit.reaction && Effective(unit))
                .Where(unit => unit.kind == "troop" || CanReactionFire(unit, actor, out _)).Select(unit => unit.id).ToArray();
            if (candidates.Length == 0) return false;
            state.pendingTrigger = trigger; state.reactionQueue = candidates; state.reactionIndex = 0; state.reactionMoveUnitId = "";
            Trace("Reaction", "Open interrupt window", actor.name + " declares " + TriggerName(trigger), "Eligible saved Reactions: " + string.Join(", ", candidates.Select(id => Unit(id)?.name ?? id)), candidates.Length + " reactor(s) may resolve before the trigger.");
            notice = string.Format("{0} declares {1}. Reactions resolve before it.", actor.name, TriggerName(trigger));
            AddEvent(notice, "reaction"); audio.Play(SoundCue.Reaction); Save(); return true;
        }

        string TriggerName(PendingTriggerData trigger)
        {
            if (trigger == null) return "an action";
            if (trigger.kind == "move") return "a Move";
            if (trigger.kind == "fire") return "Fire";
            if (trigger.kind == "suppress") return "Suppression";
            if (trigger.kind == "objective") return "an objective action";
            if (trigger.kind == "radio") return "a Signal";
            if (trigger.kind == "treat") return "medical treatment";
            return "an action";
        }

        UnitData CurrentReactor()
        {
            if (state.pendingTrigger == null || state.reactionQueue == null || state.reactionIndex < 0 || state.reactionIndex >= state.reactionQueue.Length) return null;
            return Unit(state.reactionQueue[state.reactionIndex]);
        }

        bool CanReactionFire(UnitData reactor, UnitData actor, out string reason)
        {
            reason = ""; var weapon = reactor?.weapons?.FirstOrDefault();
            if (!Effective(reactor) || !reactor.reaction) { reason = "Reaction is unavailable."; return false; }
            if (!Effective(actor)) { reason = "The triggering unit is no longer effective."; return false; }
            if (weapon == null) { reason = "This unit has no ranged weapon."; return false; }
            var distance = TacticalRules.Distance(reactor, actor, request.board);
            if (distance > weapon.range) { reason = string.Format("Triggering unit is {0:0.0}\" away; range is {1:0.0}\".", distance, weapon.range); return false; }
            var los = terrain != null && terrain.Ready ? terrain.EvaluateLineOfSight(reactor, actor) : new BattleLosResult { classification = state.cover };
            if (los.classification == "blocked") { reason = "Line of sight is blocked."; return false; }
            return true;
        }

        Rect ReactionPromptRect() { return new Rect((Screen.width - 470f) * .5f, Mathf.Max(82f, (Screen.height - 230f) * .5f), 470f, 230f); }

        void DrawReactionPrompt()
        {
            var trigger = state.pendingTrigger; var actor = Unit(trigger?.actorId); var reactor = CurrentReactor(); if (trigger == null || reactor == null) return;
            var rect = ReactionPromptRect(); GUI.depth = -180;
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUI.Label(new Rect(rect.x + 18, rect.y + 14, rect.width - 36, 24), "REACTION INTERRUPT", titleStyle);
            if (!string.IsNullOrEmpty(state.reactionMoveUnitId))
            {
                GUI.Label(new Rect(rect.x + 18, rect.y + 48, rect.width - 36, 86), string.Format("{0} is using its saved Reaction to Sprint before {1}'s {2}.\n\nClick a legal destination on the tabletop. Path terrain is measured normally and may cost double.", reactor.name, actor?.name ?? "the enemy", TriggerName(trigger)), guideStyle);
                if (GUI.Button(new Rect(rect.x + 18, rect.yMax - 52, 150, 32), "CANCEL SPRINT")) { Trace("Reaction", "Cancel Reaction Sprint destination", reactor.name, "No movement resolved and the saved Reaction was not spent.", "Return to the interrupt choice."); state.reactionMoveUnitId = ""; state.selectedId = actor?.id; notice = "Reaction Sprint canceled; choose another reaction or pass."; Save(); }
                return;
            }
            GUI.Label(new Rect(rect.x + 18, rect.y + 46, rect.width - 36, 70), string.Format("{0} has declared {1}.\n{2} may spend its saved Reaction now; the Reaction resolves first.", actor?.name ?? "Enemy unit", TriggerName(trigger), reactor.name), guideStyle);
            string fireReason; var canFire = CanReactionFire(reactor, actor, out fireReason);
            var oldEnabled = GUI.enabled; GUI.enabled = canFire;
            if (GUI.Button(new Rect(rect.x + 18, rect.yMax - 84, 134, 34), new GUIContent("FIRE FIRST", canFire ? "Resolve reaction fire against the triggering unit before its action." : fireReason))) { ResolveFire(reactor, actor, false, true); AdvanceReactionQueue(); }
            GUI.enabled = oldEnabled;
            var canSprint = reactor.kind == "troop";
            GUI.enabled = canSprint;
            if (GUI.Button(new Rect(rect.x + 166, rect.yMax - 84, 134, 34), new GUIContent("SPRINT FIRST", "Use the Reaction for one normal Move before the triggering action resolves."))) { Trace("Reaction", "Choose Reaction Sprint", reactor.name, "Saved Reaction converts to one normal Move before " + TriggerName(trigger) + ".", "Awaiting a legal destination; trigger remains paused."); state.reactionMoveUnitId = reactor.id; state.selectedId = reactor.id; notice = "Reaction Sprint: click a destination on the tabletop."; audio.Play(SoundCue.Sprint); Save(); }
            GUI.enabled = oldEnabled;
            if (GUI.Button(new Rect(rect.x + 314, rect.yMax - 84, 138, 34), new GUIContent("PASS", "Keep the saved Reaction for a later trigger."))) { Trace("Reaction", "Pass interrupt", reactor.name, "No Reaction is spent against " + TriggerName(trigger) + ".", "Reaction retained for a later eligible trigger."); AdvanceReactionQueue(); }
            if (!canFire) GUI.Label(new Rect(rect.x + 18, rect.yMax - 43, rect.width - 36, 30), "Fire unavailable: " + fireReason, smallStyle);
        }

        void AdvanceReactionQueue()
        {
            var trigger = state.pendingTrigger; if (trigger == null) return;
            var actor = Unit(trigger.actorId);
            if (!Effective(actor))
            {
                state.pendingTrigger = null; state.reactionQueue = null; state.reactionIndex = 0; state.reactionMoveUnitId = "";
                Trace("Reaction", "Cancel triggering command", actor?.name ?? "Triggering unit", "Reaction resolution left the actor downed or dead.", TriggerName(trigger) + " is CANCELED.");
                AddEvent(string.Format("{0}'s {1} is canceled by the Reaction.", actor?.name ?? "The unit", TriggerName(trigger)), "reaction"); Save(); return;
            }
            state.reactionIndex++;
            while (state.reactionQueue != null && state.reactionIndex < state.reactionQueue.Length)
            {
                var next = CurrentReactor(); if (Effective(next) && next.reaction) { Save(); return; }
                state.reactionIndex++;
            }
            state.pendingTrigger = null; state.reactionQueue = null; state.reactionIndex = 0; state.reactionMoveUnitId = "";
            Trace("Reaction", "Close interrupt window", actor.name + " · " + TriggerName(trigger), "All eligible saved Reactions resolved or passed.", "Revalidate and resolve the original trigger.");
            ResolvePendingTrigger(trigger);
        }

        void ResolvePendingTrigger(PendingTriggerData trigger)
        {
            var actor = Unit(trigger.actorId); if (!Effective(actor)) return;
            if (trigger.kind == "move")
            {
                var path = MovementPath(actor, actor.x, actor.y, trigger.destinationX, trigger.destinationY);
                var allowance = TacticalRules.MovementAllowance(actor, false) / (actor.sprint ? 2f : 1f);
                if (!path.valid || path.cost > allowance + .05f) { Trace("Movement", "Post-Reaction Move revalidation", actor.name, path.valid ? string.Format("Recomputed path cost {0:0.00}\" > allowance {1:0.00}\"", path.cost, allowance) : path.reason, "CANCELED after Reactions."); notice = path.valid ? "The interrupted movement path is no longer within allowance." : path.reason; AddEvent(actor.name + " cannot complete its interrupted Move.", "reaction"); Save(); return; }
                Trace("Movement", "Post-Reaction Move revalidation", actor.name, string.Format("Recomputed cost {0:0.00}\" <= allowance {1:0.00}\"", path.cost, allowance), "Still legal; resolve movement.");
                ExecuteMovement(actor, trigger.destinationX, trigger.destinationY, path, false); return;
            }
            if (trigger.kind == "fire" || trigger.kind == "suppress") { ResolveFire(actor, Unit(trigger.targetId), trigger.kind == "suppress", false); return; }
            if (trigger.kind == "objective") { ResolveObjectiveAction(actor, state.objectives.FirstOrDefault(item => item.id == trigger.objectiveId), Unit(trigger.targetId)); return; }
            if (trigger.kind == "radio") { ResolveRadioObservation(actor, Unit(trigger.targetId)); return; }
            if (trigger.kind == "treat") ResolveTreatment(actor, Unit(trigger.targetId));
        }

        void DrawInspector(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 9, rect.width - 20, rect.height - 18));
            inspectorScroll = GUILayout.BeginScrollView(inspectorScroll, false, true);
            GUILayout.Label("UNIT CONTROL", smallStyle); var unit = Unit(state.selectedId); var target = Unit(state.targetId);
            if (unit == null) { GUILayout.Label("Select a unit.", titleStyle); GUILayout.EndScrollView(); GUILayout.EndArea(); return; }
            GUILayout.Label(unit.name, titleStyle); GUILayout.Label(unit.role + " · " + unit.status, smallStyle);
            GUILayout.Label(string.Format("MOVE {0:0.#}\"     SKILL d{1}     DEF {2}", TacticalRules.MovementAllowance(unit, false), unit.skill, unit.defense), smallStyle);
            var alarmText = state.alarm ? "ALARM RAISED · " + state.alarmReason : "UNDETECTED · concealment checks active";
            GUILayout.Box(alarmText, new GUIStyle(guideStyle) { normal = { textColor = state.alarm ? new Color(1f, .38f, .3f) : new Color(.48f, .82f, .65f) } });
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
            var zonesVisible = state.showMissionZones;
            state.showMissionZones = GUILayout.Toggle(state.showMissionZones, new GUIContent("Show mission zones", "Show or hide extraction and other mission-zone boundaries. Hidden by default; scoring remains active either way."));
            if (zonesVisible != state.showMissionZones) { Trace("Interface", "Toggle mission-zone overlay", "Show mission zones = " + zonesVisible, "Player changed the display-only overlay setting.", state.showMissionZones ? "Mission zones visible; rules and scoring unchanged." : "Mission zones hidden; rules and scoring unchanged."); audio.Play(SoundCue.Click); Save(); }
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
                "Click a destination. Each path segment is measured: mud, water, steep slopes, dense woods, crawling, and applicable off-road travel cost double; impassable terrain blocks movement. Facing follows movement.", string.IsNullOrEmpty(moveReason), moveReason);
            if (ActionMenuButton(move)) { notice = "MOVE ready: click an open point on the tabletop within the unit's allowance."; audio.Play(SoundCue.MoveReady); }

            var attackReason = generalReason;
            if (string.IsNullOrEmpty(attackReason) && weapon == null) attackReason = "This unit has no ranged weapon.";
            else if (string.IsNullOrEmpty(attackReason) && target == null) attackReason = "Select an opposing miniature as the target.";
            else if (string.IsNullOrEmpty(attackReason) && !opposingTarget) attackReason = "Fire requires an opposing target.";
            else if (string.IsNullOrEmpty(attackReason) && !Effective(target)) attackReason = target.name + " is already out of the fight.";
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
            else if (string.IsNullOrEmpty(suppressReason) && !Effective(target)) suppressReason = target.name + " is already out of the fight.";
            else if (string.IsNullOrEmpty(suppressReason) && targetDistance > weapon.range) suppressReason = string.Format("Target is {0:0.0}\" away; {1} range is {2:0.#}\".", targetDistance, weapon.name, weapon.range);
            else if (string.IsNullOrEmpty(suppressReason) && !CanAimSuppression(selectedLos)) suppressReason = "The first visible aim point is more than 6\" from the concealed target.";
            if (ActionMenuButton(Describe("SUPPRESS", "Pin an enemy instead of wounding it.", "1 action", "Target in the weapon's cone or radius; direct LOS is not required if the attacker can aim within 6\".",
                "Roll Skill only; success gives the target Disadvantage until this side's next turn.", string.IsNullOrEmpty(suppressReason), suppressReason))) Fire(true);

            var reactionReason = canAct && !unit.reaction ? string.Empty : unit.reaction ? "This unit is already holding a reaction." : generalReason;
            if (ActionMenuButton(Describe("HOLD REACTION", "Reserve an attack for the enemy turn.", "1 action", "Effective unit with an unused action.",
                "The unit may Fire during the opposing side's turn.", string.IsNullOrEmpty(reactionReason), reactionReason)))
            { unit.actionUsed = true; unit.reaction = true; Trace("Action", "Hold Reaction", unit.name + " · effective · unused Action", "Spend the Action now; exact Reaction type remains undeclared until an enemy trigger.", "Saved Reaction available until this unit's next turn."); AddEvent(unit.name + " holds a reaction.", "action"); audio.Play(SoundCue.Reaction); Save(); }

            var sprintReason = !canAct ? generalReason : unit.kind != "troop" ? "Only troop units may sprint." : unit.sprint ? "This unit is already sprinting." : string.Empty;
            if (ActionMenuButton(Describe("SPRINT", "Take a second Move this turn.", "1 action", "Active troop unit with an unused action.",
                "Allows two separate Moves this turn; each path uses the normal Move rating and terrain costs. A saved Reaction may instead Sprint from the interrupt prompt.", string.IsNullOrEmpty(sprintReason), sprintReason)))
            {
                unit.actionUsed = true; unit.sprint = true; Trace("Action", "Sprint", unit.name + " · troop · unused Action", "Spend Action to permit a second distinct Move; each Move retains the normal movement allowance and terrain costs.", "Second Move enabled for the current turn."); AddEvent(unit.name + " sacrifices its action for a second Move.", "move");
                audio.Play(SoundCue.Sprint); Save();
            }

            var radioReason = !canAct ? generalReason : !unit.radio ? "This unit is not equipped with a radio." :
                target == null ? "Select an opposing miniature as the target." : !opposingTarget ? "Observation requires an opposing target." :
                selectedLos.classification == "blocked" ? "The observer must have line of sight to the target." : string.Empty;
            if (ActionMenuButton(Describe("RADIO FIRES OBSERVATION", "Mark a visible enemy for friendly fires.", "1 action", "Radio-equipped unit with LOS to a selected opposing target.",
                "Gives friendly attacks against the target Advantage until this observer's next turn.", string.IsNullOrEmpty(radioReason), radioReason)))
                RequestRadioObservation(unit, target);

            var treatReason = !canAct ? generalReason : unit.medicalSkill <= 0 ? "This unit lacks the required medical training and equipment." :
                unit.moved ? "Focused medical care requires the unit to remain stationary for the entire turn." : target == null ? "Select a downed friendly casualty." :
                target.side != unit.side || target.status != "downed" ? "The target must be a downed friendly." :
                targetDistance > 1.5f ? string.Format("Move adjacent first; the casualty is {0:0.0}\" away.", targetDistance) : string.Empty;
            if (ActionMenuButton(Describe("TREAT CASUALTY · FOCUS", "Attempt to revive a downed friendly.", "Entire turn (Focus)", "Medically equipped unit; bases touching (within 1.5\"); no prior movement or action.",
                "Roll medical Skill on the full Rules Table 2-2; the result determines the casualty's new status.", string.IsNullOrEmpty(treatReason), treatReason))) RequestTreatment(unit, target);

            foreach (var objective in state.objectives.Where(item => !item.complete && (item.type == "observe-zone" || item.type == "identify-units")))
            {
                var objectiveReason = ObjectiveActionReason(unit, target, objective);
                var progress = string.Format("Progress {0}/{1}.", objective.progress, Mathf.Max(1, objective.requiredProgress));
                if (ActionMenuButton(Describe(string.IsNullOrEmpty(objective.actionLabel) ? "MISSION ACTION" : objective.actionLabel, objective.text, "1 action",
                    ObjectiveRequirement(objective), progress + (objective.uninterrupted ? " Progress must be maintained on consecutive rounds." : ""), string.IsNullOrEmpty(objectiveReason), objectiveReason))) RequestObjectiveAction(unit, objective, target);
            }

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
            Trace("Command validation", action.title, "Cost " + action.cost + " · requires " + action.requirements, action.unavailable, "REJECTED before resolution.");
            notice = action.unavailable; audio.Play(SoundCue.Error); return false;
        }

        string ActionStateReason(UnitData unit)
        {
            if (!Effective(unit)) return "This unit cannot act while " + unit.status + ".";
            if (state.pendingTrigger != null) return "Resolve the current Reaction interrupt first.";
            if (unit.reaction) return "This saved Reaction is used from the interrupt prompt when an enemy declares an action.";
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
            else move = "<color=#91d6be>1. MOVE: click the map; path terrain is costed against " + (TacticalRules.MovementAllowance(unit, false) / (unit.sprint ? 2f : 1f)).ToString("0.#") + "\".</color>";
            string action;
            if (!Effective(unit)) action = "<color=#e18a75>× Cannot take actions.</color>";
            else if (unit.side != state.activeSide && !unit.reaction) action = "<color=#9aa39c>○ No action during this side's turn.</color>";
            else if (unit.reaction) action = "<color=#f1c66b>2. REACTION READY: wait for an enemy trigger; Fire or Sprint from the interrupt prompt.</color>";
            else if (unit.actionUsed) action = "<color=#9aa39c>✓ Action used.</color>";
            else if (target == null) action = "<color=#91d6be>2. ACTION: select a target or choose a utility action.</color>";
            else action = "<color=#91d6be>2. ACTION: target " + target.name + " is selected; choose an action.</color>";
            GUILayout.Box(move + "\n" + action + "\n3. END TURN when all units are finished (Space). Mission deadline: end of Round " + Mathf.Max(1, request.mission.durationTurns) + ".", guideStyle);
            GUILayout.Label("Hover any miniature, roster entry, or button for details · F1 opens the full guide", smallStyle);
        }

        void Fire(bool suppress)
        {
            var attacker = Unit(state.selectedId); var target = Unit(state.targetId);
            if (BeginReactionWindow(new PendingTriggerData { kind = suppress ? "suppress" : "fire", actorId = attacker?.id, targetId = target?.id, suppress = suppress })) return;
            ResolveFire(attacker, target, suppress, false);
        }

        void ResolveFire(UnitData attacker, UnitData target, bool suppress, bool reactionResolution)
        {
            var weapon = attacker?.weapons?.FirstOrDefault();
            BattleLosResult los = null;
            if (terrain != null && terrain.Ready && attacker != null && target != null) { los = SelectedLos(attacker, target); state.cover = los.classification; }
            var result = TacticalRules.Attack(attacker, target, weapon, request.board, state.round, state.cover, suppress, dice, CanAimSuppression(los));
            var attackInputs = string.Format("{0} → {1} · {2} · range {3:0.00}\"/{4:0.00}\" · LOS {5}", attacker?.name ?? "none", target?.name ?? "none", weapon?.name ?? "no weapon", attacker != null && target != null ? TacticalRules.Distance(attacker, target, request.board) : 0f, weapon?.range ?? 0f, state.cover);
            if (!result.valid) { Trace(suppress ? "Suppression" : "Attack", suppress ? "Declare Suppression" : "Declare Fire", attackInputs, result.reason, "REJECTED."); notice = result.reason; audio.Play(SoundCue.Error); return; }
            audio.Play(suppress ? SoundCue.Suppress : SoundCue.Fire);
            if (reactionResolution) attacker.reaction = false; else ConsumeAction(attacker); RaiseAlarm(attacker.name + " fired a weapon.");
            var advantages = new List<string>(); var disadvantages = new List<string>();
            if (!target.moved) advantages.Add("target did not move"); if (target.observedBy == attacker.side) advantages.Add("fires observation");
            if (state.cover == "partial") disadvantages.Add("partial cover/concealment"); if (attacker.status == "injured") disadvantages.Add("attacker injured"); if (attacker.suppressed) disadvantages.Add("attacker suppressed");
            var modifierText = string.Format("Advantage sources [{0}] · Disadvantage sources [{1}] · final mode {2}", advantages.Count == 0 ? "none" : string.Join(", ", advantages), disadvantages.Count == 0 ? "none" : string.Join(", ", disadvantages), result.skill.mode > 0 ? "Advantage" : result.skill.mode < 0 ? "Disadvantage" : "normal (none or canceled)");
            var attackMath = modifierText + string.Format(" · Skill {0} vs Difficulty {1}", RollText(result.skill), weapon.difficulty);
            if (!suppress && result.hit) attackMath += string.Format(" · Damage {0} vs Defense {1}", RollText(result.damage), result.defense);
            var attackOutcome = !result.hit ? "MISS" : suppress ? "SUCCESS: target suppressed" : result.casualty ? "SUCCESS: casualty; target downed" : "HIT: damage did not equal or exceed Defense";
            Trace(suppress ? "Suppression" : "Attack", reactionResolution ? "Resolve Reaction Fire" : suppress ? "Resolve Suppression" : "Resolve Fire", attackInputs, attackMath, attackOutcome);
            var reactionText = reactionResolution ? " as a Reaction before the triggering action" : "";
            if (!result.hit) AddEvent(string.Format("{0} misses {1} (skill {2} vs {3}).", attacker.name, target.name, RollText(result.skill), weapon.difficulty), "miss");
            else if (suppress) { target.suppressed = true; target.suppressedBySide = attacker.side; AddEvent(attacker.name + " suppresses " + target.name + reactionText + ".", "suppress"); }
            else if (result.casualty) { target.status = "downed"; target.reaction = false; AddEvent(string.Format("{0} downs {1}{2} (damage {3} vs defense {4}).", attacker.name, target.name, reactionText, RollText(result.damage), result.defense), "hit"); audio.Play(SoundCue.Hit); }
            else AddEvent(string.Format("{0} hits {1}{2}, but causes no casualty (damage {3} vs defense {4}).", attacker.name, target.name, reactionText, RollText(result.damage), result.defense), "hit");
            Save();
        }

        void RequestRadioObservation(UnitData unit, UnitData target)
        {
            Trace("Signal", "Declare fires observation", string.Format("Observer {0} · target {1}", unit?.name ?? "none", target?.name ?? "none"), "Action legality and LOS checked; opposing saved Reactions are offered first.", "PENDING reaction check.");
            if (BeginReactionWindow(new PendingTriggerData { kind = "radio", actorId = unit?.id, targetId = target?.id })) return;
            ResolveRadioObservation(unit, target);
        }

        void ResolveRadioObservation(UnitData unit, UnitData target)
        {
            if (!Effective(unit) || !Effective(target)) return;
            ConsumeAction(unit); target.observedBy = unit.side; target.observedRound = state.round;
            Trace("Signal", "Fires observation", string.Format("Observer {0} · target {1} · radio equipped · LOS confirmed", unit.name, target.name), "Spend 1 Action; mark target observed by " + unit.side.ToUpperInvariant() + " until the observer's next turn.", "Friendly attacks against the target receive Advantage while the mark remains.");
            AddEvent(unit.name + " observes " + target.name + " for friendly fires.", "signal"); audio.Play(SoundCue.Radio); Save();
        }

        void RequestTreatment(UnitData medic, UnitData target)
        {
            Trace("Medical", "Declare focused casualty treatment", string.Format("Medic {0} · casualty {1}", medic?.name ?? "none", target?.name ?? "none"), "Training, adjacency, no prior movement, and Action legality checked; opposing saved Reactions resolve first.", "PENDING reaction check.");
            if (BeginReactionWindow(new PendingTriggerData { kind = "treat", actorId = medic?.id, targetId = target?.id })) return;
            ResolveTreatment(medic, target);
        }

        void ResolveTreatment(UnitData medic, UnitData target)
        {
            if (medic == null || medic.medicalSkill <= 0) { notice = "This unit lacks the required medical training and equipment."; audio.Play(SoundCue.Error); return; }
            if (medic.moved) { notice = "Medical treatment requires Focus; the treating unit must not move this turn."; audio.Play(SoundCue.Error); return; }
            if (target == null || target.side != medic.side || target.status != "downed") { notice = "Select a downed friendly casualty."; audio.Play(SoundCue.Error); return; }
            var range = TacticalRules.Distance(medic, target, request.board); if (range > 1.5f) { notice = string.Format("Move adjacent first ({0:0.0}\" away).", range); audio.Play(SoundCue.Error); return; }
            ConsumeAction(medic); medic.focused = true; medic.moved = true; medic.movesMade = 1; DieRoll roll; target.status = TacticalRules.Medicine(medic, dice, out roll);
            Trace("Medical", "Focused casualty treatment", string.Format("Medic {0} medical Skill d{1} · casualty {2} · range {3:0.00}\" · medic status {4}", medic.name, medic.medicalSkill, target.name, range, medic.status), string.Format("Medical Skill {0}; full-turn Focus; Table 2-2 bands 1–2 dead, 3–4 downed, 5–7 injured, 8+ healthy", RollText(roll)), target.name + " becomes " + target.status.ToUpperInvariant() + ".");
            AddEvent(string.Format("{0} treats {1}: {2} — {3}.", medic.name, target.name, RollText(roll), target.status), "medical"); audio.Play(SoundCue.Medical); Save();
        }

        int ExtractedCount(ObjectiveData objective)
        {
            return state.units.Count(unit => unit.side == objective.side && Effective(unit) && unit.enteredField && TacticalRules.InExtractionZone(unit, objective));
        }

        void RaiseAlarm(string reason)
        {
            if (state.alarm) return;
            state.alarm = true; state.alarmReason = reason;
            Trace("Alarm", "Resolve alarm state", "Current alarm = clear", reason, "ALARM RAISED; avoid-alarm objective will fail.");
            AddEvent("ALARM RAISED: " + reason, "alarm"); audio.Play(SoundCue.Alarm);
        }

        void EvaluateDetection(UnitData subject, string activity)
        {
            var objective = state.objectives.FirstOrDefault(item => item.type == "avoid-alarm" && !item.complete);
            if (objective == null || state.alarm || !Effective(subject) || subject.side != objective.side || !subject.enteredField) return;
            var maximum = objective.radius > 0f ? objective.radius : float.MaxValue; var difficulty = objective.difficulty > 0 ? objective.difficulty : 4;
            foreach (var observer in state.units.Where(unit => unit.side != subject.side && Effective(unit)).OrderBy(unit => TacticalRules.Distance(unit, subject, request.board)))
            {
                var distance = TacticalRules.Distance(observer, subject, request.board);
                if (distance > maximum) { Trace("Detection", "Check observer range", string.Format("Observer {0} · subject {1} · {2:0.00}\"", observer.name, subject.name, distance), string.Format("{0:0.00}\" > mission detection radius {1:0.00}\"", distance, maximum), "No detection attempt."); continue; }
                var los = terrain != null && terrain.Ready ? terrain.EvaluateLineOfSight(observer, subject) : new BattleLosResult { classification = "open" };
                var detection = TacticalRules.Detection(observer, los.classification, difficulty, dice);
                if (!detection.attempted) { Trace("Detection", "Check enemy observation", string.Format("Observer {0} · subject {1} · {2:0.00}\" · blocker {3}", observer.name, subject.name, distance, los.blocker), "Total concealment/blocked LOS prevents a detection test.", "UNDETECTED by this observer."); continue; }
                if (los.classification == "open" && detection.detected) { Trace("Detection", "Check enemy observation", string.Format("Observer {0} · subject {1} · {2:0.00}\" · open LOS", observer.name, subject.name, distance), "Open LOS inside the mission detection radius confirms the subject.", "DETECTED."); RaiseAlarm(string.Format("{0} confirmed {1} during {2} at {3:0.0}\".", observer.name, subject.name, activity, distance)); return; }
                var detectionMath = string.Format("Partial concealment: Skill {0} with Disadvantage vs Difficulty {1}", RollText(detection.skill), difficulty);
                if (detection.detected) { Trace("Detection", "Check enemy observation", string.Format("Observer {0} · subject {1} · {2:0.00}\" · partial LOS", observer.name, subject.name, distance), detectionMath, "DETECTED."); RaiseAlarm(string.Format("{0} detected partially concealed {1} during {2} (Skill {3} vs Difficulty {4}).", observer.name, subject.name, activity, RollText(detection.skill), difficulty)); return; }
                Trace("Detection", "Check enemy observation", string.Format("Observer {0} · subject {1} · {2:0.00}\" · partial LOS", observer.name, subject.name, distance), detectionMath, "UNDETECTED by this observer.");
                AddEvent(string.Format("{0} fails to detect partially concealed {1} during {2} (Skill {3} vs Difficulty {4}).", observer.name, subject.name, activity, RollText(detection.skill), difficulty), "detection");
            }
        }

        void EvaluateDetectionForSide(string side)
        {
            foreach (var unit in state.units.Where(item => item.side == side && Effective(item))) { EvaluateDetection(unit, "turn-end observation"); if (state.alarm) break; }
        }

        string ObjectiveRequirement(ObjectiveData objective)
        {
            if (objective.type == "observe-zone") return string.Format("{0} unit within {1:0.#}\" of the objective with line of sight.", (objective.side ?? "blue").ToUpperInvariant(), objective.radius);
            if (objective.type == "identify-units") return string.Format("Select an unidentified mission target within {0:0.#}\" and line of sight; pass Skill against Difficulty {1}.", objective.radius, objective.difficulty);
            if (objective.type == "extract-force") return string.Format("Return at least {0:0}% of the force, effective, to the marked {1} edge after deploying onto the battlefield.", objective.threshold > 0f ? objective.threshold : .75f, objective.edge ?? "friendly");
            return "Mission-defined requirements.";
        }

        BattleLosResult ObjectiveLos(UnitData unit, ObjectiveData objective, UnitData target)
        {
            if (terrain == null || !terrain.Ready) return new BattleLosResult { classification = state.cover };
            return target != null ? terrain.EvaluateLineOfSight(unit, target) : terrain.EvaluateLineOfSight(unit.x, unit.y, objective.x, objective.y, unit.id, "");
        }

        bool ObjectiveLosVisible(UnitData unit, ObjectiveData objective, UnitData target, out BattleLosResult los)
        {
            los = ObjectiveLos(unit, objective, target); if (!objective.requiresLos || los.classification != "blocked") return true;
            var distance = target != null ? TacticalRules.Distance(unit, target, request.board) : TacticalRules.Distance(unit.x, unit.y, objective.x, objective.y, request.board);
            return los.blockerDistance >= 0f && distance - los.blockerDistance <= 1.5f;
        }

        string ObjectiveActionReason(UnitData unit, UnitData target, ObjectiveData objective)
        {
            var general = ActionStateReason(unit); if (!string.IsNullOrEmpty(general)) return general;
            if (unit.side != objective.side) return "Only " + objective.side.ToUpperInvariant() + " units can attempt this objective.";
            if (objective.type == "observe-zone")
            {
                var distance = TacticalRules.Distance(unit.x, unit.y, objective.x, objective.y, request.board);
                if (distance > objective.radius) return string.Format("Move within {0:0.#}\" of the objective; currently {1:0.0}\" away.", objective.radius, distance);
                if (objective.lastProgressRound == state.round) return "This objective has already received its observation for the current round.";
                BattleLosResult los; if (!ObjectiveLosVisible(unit, objective, null, out los)) return "Terrain or a miniature blocks observation of the objective.";
                return "";
            }
            if (objective.type == "identify-units")
            {
                if (target == null || target.side == unit.side) return "Select an opposing mission target to identify.";
                if (objective.targetUnitIds == null || !objective.targetUnitIds.Contains(target.id)) return target.name + " is not one of this objective's identification targets.";
                if (objective.identifiedUnitIds != null && objective.identifiedUnitIds.Contains(target.id)) return target.name + " has already been identified.";
                var distance = TacticalRules.Distance(unit, target, request.board); if (distance > objective.radius) return string.Format("Target is {0:0.0}\" away; identification range is {1:0.#}\".", distance, objective.radius);
                BattleLosResult los; if (!ObjectiveLosVisible(unit, objective, target, out los)) return "Line of sight to the identification target is blocked.";
            }
            return "";
        }

        void RequestObjectiveAction(UnitData unit, ObjectiveData objective, UnitData target)
        {
            var reason = ObjectiveActionReason(unit, target, objective); if (!string.IsNullOrEmpty(reason)) { Trace("Objective", objective?.actionLabel ?? "Mission action", unit?.name ?? "none", reason, "REJECTED."); notice = reason; audio.Play(SoundCue.Error); return; }
            Trace("Objective", "Declare " + (objective.actionLabel ?? "mission action"), string.Format("Actor {0} · target {1} · current progress {2}/{3}", unit.name, target?.name ?? objective.text, objective.progress, objective.requiredProgress), "Action requirements passed; opposing saved Reactions resolve first.", "PENDING reaction check.");
            if (BeginReactionWindow(new PendingTriggerData { kind = "objective", actorId = unit.id, targetId = target?.id, objectiveId = objective.id })) return;
            ResolveObjectiveAction(unit, objective, target);
        }

        void ResolveObjectiveAction(UnitData unit, ObjectiveData objective, UnitData target)
        {
            if (unit == null || objective == null || !Effective(unit)) return;
            var reason = ObjectiveActionReason(unit, target, objective); if (!string.IsNullOrEmpty(reason)) { Trace("Objective", objective.actionLabel, unit.name, "Post-Reaction revalidation: " + reason, "CANCELED."); notice = reason; audio.Play(SoundCue.Error); return; }
            ConsumeAction(unit);
            if (objective.difficulty > 0)
            {
                BattleLosResult los; ObjectiveLosVisible(unit, objective, target, out los); var roll = dice.Skill(unit.skill, los.classification == "partial" ? -1 : 0);
                if (roll.result < objective.difficulty) { Trace("Objective", objective.actionLabel, string.Format("{0} · target {1} · LOS {2} · Skill d{3}", unit.name, target?.name ?? objective.text, los.classification, unit.skill), string.Format("Skill {0} vs static Difficulty {1}{2}", RollText(roll), objective.difficulty, los.classification == "partial" ? " with Disadvantage" : ""), "FAILED; no objective progress."); AddEvent(string.Format("{0} fails to identify {1} (Skill {2} vs Difficulty {3}).", unit.name, target?.name ?? "the objective", RollText(roll), objective.difficulty), "objective"); audio.Play(SoundCue.Error); Save(); return; }
                Trace("Objective", objective.actionLabel, string.Format("{0} · target {1} · LOS {2} · Skill d{3}", unit.name, target?.name ?? objective.text, los.classification, unit.skill), string.Format("Skill {0} vs static Difficulty {1}{2}", RollText(roll), objective.difficulty, los.classification == "partial" ? " with Disadvantage" : ""), "PASSED; apply objective progress.");
            }
            if (objective.type == "observe-zone")
            {
                if (objective.uninterrupted && objective.lastProgressRound > 0 && objective.lastProgressRound != state.round - 1) objective.progress = 0;
                objective.progress = Mathf.Min(Mathf.Max(1, objective.requiredProgress), objective.progress + 1); objective.lastProgressRound = state.round;
                state.observationTurns = objective.progress;
                Trace("Objective", objective.actionLabel, string.Format("{0} within {1:0.0}\" objective radius · LOS confirmed · once per round", unit.name, objective.radius), string.Format("Progress {0} → {1}; required {2}; uninterrupted={3}", objective.progress - 1, objective.progress, objective.requiredProgress, objective.uninterrupted), objective.progress >= objective.requiredProgress ? "OBJECTIVE COMPLETE" : "Progress recorded for Round " + state.round + ".");
                AddEvent(string.Format("{0} advances {1} ({2}/{3}).", unit.name, objective.text, objective.progress, Mathf.Max(1, objective.requiredProgress)), "objective");
            }
            else if (objective.type == "identify-units")
            {
                var identified = new List<string>(objective.identifiedUnitIds ?? new string[0]); if (!identified.Contains(target.id)) identified.Add(target.id);
                objective.identifiedUnitIds = identified.ToArray(); objective.progress = identified.Count;
                Trace("Objective", objective.actionLabel, "Identified target " + target.name, string.Format("Unique identified targets = {0}; required = {1}", objective.progress, objective.requiredProgress), objective.progress >= objective.requiredProgress ? "OBJECTIVE COMPLETE" : "Identification recorded.");
                AddEvent(string.Format("{0} identifies {1} for {2} ({3}/{4}).", unit.name, target.name, objective.text, objective.progress, Mathf.Max(1, objective.requiredProgress)), "objective");
            }
            objective.complete = objective.progress >= Mathf.Max(1, objective.requiredProgress);
            audio.Play(objective.complete ? SoundCue.Objective : SoundCue.Relay); Save();
        }

        void EndTurn()
        {
            if (state.pendingTrigger != null) { Trace("Turn", "End side turn", state.activeSide.ToUpperInvariant(), "A Reaction interrupt remains unresolved.", "REJECTED."); notice = "Resolve or pass the current Reaction interrupt before ending the turn."; audio.Play(SoundCue.Error); return; }
            var side = state.activeSide;
            EvaluateDetectionForSide(side);
            foreach (var objective in state.objectives.Where(item => item.type == "observe-zone" && item.uninterrupted && !item.complete && item.side == side && item.progress > 0 && item.lastProgressRound < state.round))
            {
                Trace("Objective", "Check uninterrupted progress", objective.text, string.Format("Last progress Round {0}; current Round {1}; required observation was not made.", objective.lastProgressRound, state.round), "Progress resets to 0."); objective.progress = 0; state.observationTurns = 0; AddEvent(objective.text + " loses its uninterrupted progress.", "objective");
            }
            var automaticReactions = state.units.Where(item => item.side == side && Effective(item) && !item.actionUsed).ToArray();
            foreach (var unit in automaticReactions) unit.reaction = true;
            Trace("Turn", "End side turn", side.ToUpperInvariant(), automaticReactions.Length == 0 ? "Every effective unit used its Action." : "Unused Actions become saved Reactions: " + string.Join(", ", automaticReactions.Select(unit => unit.name)), state.firstSideFinished ? "Complete the round." : "Pass play to the opposing side.");
            if (!state.firstSideFinished) { state.firstSideFinished = true; StartSide(side == "blue" ? "red" : "blue"); }
            else if (state.round >= Mathf.Max(1, request.mission.durationTurns))
            {
                state.endedByDeadline = true; AddEvent("Mission time limit reached at the end of Round " + state.round + ".", "system"); FinishBattle(true); return;
            }
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

        void FinishBattle(bool deadline = false)
        {
            if (state.pendingTrigger != null) { Trace("Mission scoring", "End Mission", "Pending " + TriggerName(state.pendingTrigger), "A Reaction interrupt must resolve before mission scoring.", "REJECTED."); notice = "Resolve or pass the current Reaction interrupt before ending the mission."; audio.Play(SoundCue.Error); return; }
            if (deadline) state.endedByDeadline = true;
            foreach (var objective in state.objectives)
            {
                var side = string.IsNullOrEmpty(objective.side) ? "blue" : objective.side;
                var force = state.units.Where(unit => unit.side == side).ToArray();
                if (objective.type == "force-effective") objective.complete = force.Length > 0 && (float)force.Count(Effective) / force.Length >= (objective.threshold > 0f ? objective.threshold : .75f);
                else if (objective.type == "extract-force") { objective.progress = ExtractedCount(objective); objective.complete = force.Length > 0 && (float)objective.progress / force.Length >= (objective.threshold > 0f ? objective.threshold : .75f); }
                else if (objective.type == "avoid-alarm") objective.complete = !state.alarm;
                else if (objective.type == "observe-zone" || objective.type == "identify-units") objective.complete = objective.progress >= Mathf.Max(1, objective.requiredProgress);
                var objectiveMath = objective.type == "extract-force" ? string.Format("{0} effective deployed units extracted / {1} starting units; threshold {2:0}%", objective.progress, force.Length, objective.threshold > 0f ? objective.threshold : .75f) : objective.type == "avoid-alarm" ? "Alarm state = " + (state.alarm ? "raised" : "clear") : objective.type == "force-effective" ? string.Format("{0}/{1} effective; threshold {2:0}%", force.Count(Effective), force.Length, objective.threshold > 0f ? objective.threshold : .75f) : string.Format("Progress {0}/{1}", objective.progress, Mathf.Max(1, objective.requiredProgress));
                Trace("Mission scoring", objective.text, objective.type + " · " + objective.points + " point(s)", objectiveMath, objective.complete ? "COMPLETE" : "INCOMPLETE");
            }
            var blue = state.units.Where(unit => unit.side == "blue").ToArray();
            var scoreAvailable = state.objectives.Sum(objective => objective.points); var scoreEarned = state.objectives.Where(objective => objective.complete).Sum(objective => objective.points);
            var outcome = scoreAvailable <= 0 ? "Mission complete" : scoreEarned == scoreAvailable ? "Decisive success" : scoreEarned * 2 >= scoreAvailable ? "Partial success" : "Mission setback";
            Trace("Mission scoring", "Compute final outcome", string.Format("Earned {0} of {1} objective points · Round {2} · deadline={3}", scoreEarned, scoreAvailable, state.round, state.endedByDeadline), "All points = Decisive success; at least half = Partial success; below half = Mission setback.", outcome.ToUpperInvariant());
            var result = new BattleResult
            {
                requestId = request.requestId, resultId = Guid.NewGuid().ToString("N"), completedAt = DateTime.UtcNow.ToString("O"), missionNumber = request.mission.number,
                rounds = state.round, alarm = state.alarm, alarmReason = state.alarmReason, endedByDeadline = state.endedByDeadline, observationTurns = state.observationTurns, events = state.events, calculations = state.calculations,
                scoreEarned = scoreEarned, scoreAvailable = scoreAvailable, outcome = outcome, terrainLocationId = request.mission.locationId,
                units = state.units.Select(unit => new UnitResult { id = unit.id, x = unit.x, y = unit.y, facing = unit.facing, status = unit.status }).ToArray(),
                objectives = state.objectives.Select(objective => new ObjectiveResult { id = objective.id, complete = objective.complete }).ToArray(),
                casualties = blue.Where(unit => unit.status == "downed" || unit.status == "dead").Select(unit => new CasualtyResult { unitId = unit.id, category = unit.status == "dead" ? "KIA" : "WIA-S" }).ToArray()
            };
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true)); state.completed = true; AddEvent(string.Format("Battle result exported: {0}, {1}/{2} objective points.", outcome, scoreEarned, scoreAvailable), "objective"); audio.Play(SoundCue.Mission); Save(); notice = "Result exported and ready for automatic campaign import. You may return to Campaign Command.";
        }
        void AddEvent(string text, string kind)
        {
            var events = new List<BattleEvent>(state.events ?? new BattleEvent[0]) { new BattleEvent { round = state.round, text = text, kind = kind } }; state.events = events.ToArray(); notice = text;
        }
        void Trace(string category, string command, string inputs, string computation, string outcome)
        {
            if (state == null) return;
            string section; int page; RuleCitation(category, command, out section, out page);
            var calculations = new List<RuleCalculation>(state.calculations ?? new RuleCalculation[0]);
            calculations.Add(new RuleCalculation
            {
                sequence = state.nextCalculationSequence <= 0 ? calculations.Count + 1 : state.nextCalculationSequence,
                round = state.round, side = state.activeSide ?? "", category = category ?? "RULE", command = command ?? "Computation",
                inputs = inputs ?? "", computation = computation ?? "", outcome = outcome ?? "", ruleSection = section, rulePage = page
            });
            state.nextCalculationSequence = calculations[calculations.Count - 1].sequence + 1; state.calculations = calculations.ToArray(); Save();
        }
        string TraceText(RuleCalculation item)
        {
            var source = item.rulePage > 0 ? string.Format("\nSOURCE: {0}, PDF page {1}", item.ruleSection, item.rulePage) : "\nSOURCE: Scenario or interface procedure; no universal PDF section.";
            return string.Format("#{0} · ROUND {1} · {2} · {3}\n{4}\nINPUTS: {5}\nCOMPUTATION: {6}\nOUTCOME: {7}{8}", item.sequence, item.round, (item.side ?? "").ToUpperInvariant(), (item.category ?? "RULE").ToUpperInvariant(), item.command, item.inputs, item.computation, item.outcome, source);
        }
        string CompleteTraceText()
        {
            var ordered = (state.calculations ?? new RuleCalculation[0]).OrderBy(item => item.sequence); return string.Join("\n\n", ordered.Select(TraceText));
        }
        void RuleCitation(string category, string command, out string section, out int page)
        {
            var kind = (category ?? "").ToLowerInvariant(); var action = (command ?? "").ToLowerInvariant(); section = ""; page = 0;
            if (kind == "initiative") { section = "Rules 2.2.1 — Initiative"; page = 6; }
            else if (kind == "movement") { section = action.Contains("sprint") ? "Rules 2.3.1.1 — Sprinting" : "Rules 2.3–2.3.1 — Movement"; page = action.Contains("sprint") ? 7 : 6; }
            else if (kind == "line of sight") { section = "Rules 2.4.1 — Checking visibility"; page = 7; }
            else if (kind == "attack") { section = "Rules 2.4–2.6.4 — Attack sequence"; page = 7; }
            else if (kind == "suppression") { section = "Rules 2.6.5 — Suppression"; page = 11; }
            else if (kind == "reaction") { section = "Rules 2.2.2.2 — Reactions"; page = 6; }
            else if (kind == "medical") { section = "Rules 2.8 — Medicine"; page = 11; }
            else if (kind == "signal") { section = "Rules 5.1 — Signals"; page = 25; }
            else if (kind == "objective") { section = "Rules 2.7 — Non-attack actions"; page = 11; }
            else if (kind == "detection") { section = "Rules 2.4.1.1.2 — Concealment; scenario detection test"; page = 8; }
            else if (kind == "alarm") { section = "Rules 2.4.1.1 — Cover and concealment; scenario alarm rule"; page = 7; }
            else if (kind == "turn") { section = "Rules 2.2 — Rounds and turns"; page = 6; }
            else if (kind == "action" && action.Contains("reaction")) { section = "Rules 2.2.2.2 — Reactions"; page = 6; }
            else if (kind == "action" && action.Contains("sprint")) { section = "Rules 2.3.1.1 — Sprinting"; page = 7; }
            else if (kind == "command validation")
            {
                if (action.Contains("suppress")) { section = "Rules 2.6.5 — Suppression"; page = 11; }
                else if (action.Contains("fire")) { section = "Rules 2.4–2.6.4 — Attack sequence"; page = 7; }
                else if (action.Contains("reaction")) { section = "Rules 2.2.2.2 — Reactions"; page = 6; }
                else if (action.Contains("sprint")) { section = "Rules 2.3.1.1 — Sprinting"; page = 7; }
                else if (action.Contains("radio")) { section = "Rules 5.1 — Signals"; page = 25; }
                else if (action.Contains("treat")) { section = "Rules 2.8 — Medicine"; page = 11; }
                else if (action.Contains("observe") || action.Contains("identify")) { section = "Rules 2.7 — Non-attack actions"; page = 11; }
                else if (action.Contains("move")) { section = "Rules 2.3 — Movement"; page = 6; }
            }
        }
        string LocalRulesPdfPath()
        {
            var configured = request?.settings?.rulesPdfPath; if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            var name = "Rules Compressed-278da66fbe36c91eae0252e2830de80b.pdf";
            var candidates = new[] {
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "official", "DownRangeLatest", name)),
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs", "official", "DownRangeLatest", name))
            };
            return candidates.FirstOrDefault(File.Exists) ?? "";
        }
        void OpenRulesPage(RuleCalculation item)
        {
            var pdf = LocalRulesPdfPath();
            if (item == null || item.rulePage <= 0) { notice = "This computation is scenario-specific and has no universal rules page."; audio.Play(SoundCue.Error); return; }
            if (string.IsNullOrEmpty(pdf)) { notice = "The authoritative Rules PDF could not be found. Citation: " + item.ruleSection + ", page " + item.rulePage + "."; audio.Play(SoundCue.Error); return; }
            Application.OpenURL(new Uri(pdf).AbsoluteUri + "#page=" + item.rulePage); notice = "Opening " + item.ruleSection + ", PDF page " + item.rulePage + ". If the viewer ignores page anchors, use this citation manually."; audio.Play(SoundCue.Click);
        }
        void Save() { if (state == null || string.IsNullOrEmpty(statePath)) return; state.rollCount = dice?.RollCount ?? state.rollCount; File.WriteAllText(statePath, JsonUtility.ToJson(state, true)); }

        void DrawFooter(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); var left = new Rect(rect.x + 10, rect.y + 8, rect.width * .48f, rect.height - 16); var right = new Rect(rect.x + rect.width * .49f, rect.y + 8, rect.width * .5f, rect.height - 16);
            GUILayout.BeginArea(left); GUILayout.Label("MISSION OBJECTIVES", smallStyle); GUILayout.BeginHorizontal(); var objectiveWidth = Mathf.Max(70f, (left.width - 8f) / Mathf.Max(1, state.objectives.Length));
            foreach (var objective in state.objectives)
            {
                var progress = !objective.complete && objective.requiredProgress > 1 && (objective.type == "observe-zone" || objective.type == "identify-units") ? string.Format(" [{0}/{1}]", objective.progress, objective.requiredProgress) : "";
                if (objective.type == "extract-force")
                {
                    var forceSize = state.units.Count(unit => unit.side == objective.side); var required = Mathf.CeilToInt(forceSize * (objective.threshold > 0f ? objective.threshold : .75f));
                    progress = string.Format(" [{0}/{1} extracted]", ExtractedCount(objective), required);
                }
                if (objective.type == "avoid-alarm") progress = state.alarm ? " [FAILED]" : " [undetected]";
                GUILayout.Box((objective.complete ? "✓ " : "○ ") + objective.text + progress, objectiveStyle, GUILayout.Width(objectiveWidth), GUILayout.Height(50));
            }
            GUILayout.EndHorizontal(); GUILayout.EndArea();
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
            GUILayout.Label("Click an empty destination. Unity samples the complete path: normal segments cost their measured distance, while mud, water, steep slopes, dense woods, crawling, and applicable off-road segments cost twice as much. Buildings and terrain impassable to the unit block the path. The Impaired movement toggle marks the entire declared path as crawling or otherwise impaired.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("3  ACT", titleStyle);
            GUILayout.Label("Use the scrollable UNIT ACTIONS menu in the right panel. Every action remains visible with a short description and READY or UNAVAILABLE state. Point at a button for its cost, requirements, full effect, and the exact reason it cannot currently be used. Unity checks eye-height LOS before Fire. Suppress can instead use a visible aim point within 6\" of a concealed target, as allowed by the full rules.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("4  REACT OR END", titleStyle);
            GUILayout.Label("Hold Reaction deliberately, or simply end the turn: every effective unit that has not acted automatically holds one. When an enemy declares a Move or supported action, Unity pauses it and opens the Reaction interrupt. Fire or Sprint resolves first; Pass preserves the Reaction for a later trigger. If reaction fire downs the acting unit, its triggering action is canceled.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("RECONNAISSANCE", titleStyle);
            GUILayout.Label("The optional Show mission zones toggle reveals the friendly-edge extraction boundary; it is hidden by default. A unit counts only after it first deploys beyond that boundary and later returns effective. Open enemy LOS confirms detection; partial concealment requires the observing enemy to pass Skill with Disadvantage against the mission Difficulty. Firing always raises the alarm. The mission scores automatically after both sides finish the final round.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("CAMERA", titleStyle);
            GUILayout.Label("WASD or the arrow keys pan by fixed map direction: W north, S south, A west, and D east, regardless of camera rotation. Hold the middle mouse button and drag to rotate or tilt/skew; use the mouse wheel to zoom.", guideStyle);
            GUILayout.Space(10); GUILayout.Box("CURRENT SIDE: " + state.activeSide.ToUpperInvariant() + "    ·    ROUND " + state.round + "    ·    L: LOS tool    ·    Space: end turn    ·    F1: help    ·    F2: rules trace", guideStyle);
            GUILayout.Space(8); GUILayout.Label("Tip: action details appear both beneath the menu and beside the mouse pointer. Unavailable buttons can also be clicked to place their blocking reason in STATUS.", smallStyle);
            GUILayout.FlexibleSpace();
            var enabled = audio.Enabled; var next = GUILayout.Toggle(enabled, new GUIContent(" Tactical sound", "Enable or mute the procedural offline sound cues.")); if (next != enabled) { audio.Enabled = next; audio.Play(SoundCue.Click); }
            GUILayout.EndArea();
        }

        void DrawRulesTraceOverlay()
        {
            GUI.depth = -200;
            var shade = GUI.color; GUI.color = new Color(0f, 0f, 0f, .78f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), pixel); GUI.color = shade;
            var width = Mathf.Min(980f, Screen.width - 40f); var height = Mathf.Min(740f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height); GUI.Box(rect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(rect.x + 22f, rect.y + 18f, rect.width - 44f, rect.height - 36f));
            GUILayout.BeginHorizontal(); GUILayout.Label("RULES COMPUTATION TRACE", titleStyle); GUILayout.FlexibleSpace();
            if (GUILayout.Button(traceNewestFirst ? "NEWEST FIRST" : "OLDEST FIRST", GUILayout.Width(118), GUILayout.Height(30))) { traceNewestFirst = !traceNewestFirst; traceScroll = Vector2.zero; }
            if (GUILayout.Button(new GUIContent("COPY ALL", "Copy the complete chronological trace to the system clipboard."), GUILayout.Width(88), GUILayout.Height(30))) { GUIUtility.systemCopyBuffer = CompleteTraceText(); notice = "Complete rules trace copied to the clipboard."; audio.Play(SoundCue.Click); }
            if (GUILayout.Button(new GUIContent("CLOSE  ×", "Close this tab. Shortcut: F2 or Escape."), GUILayout.Width(92), GUILayout.Height(30))) { showRulesTrace = false; audio.Play(SoundCue.Click); }
            GUILayout.EndHorizontal();
            var items = state.calculations ?? new RuleCalculation[0];
            GUILayout.Label(items.Length == 0 ? "No computations recorded yet. New commands will appear here." : items.Length + " computations recorded in the persistent battle save. Inputs, modifiers, dice, thresholds, and rulings are retained in sequence.", guideStyle);
            GUILayout.Space(8); traceScroll = GUILayout.BeginScrollView(traceScroll);
            var ordered = traceNewestFirst ? items.OrderByDescending(item => item.sequence) : items.OrderBy(item => item.sequence);
            foreach (var item in ordered)
            {
                GUILayout.BeginVertical(tooltipStyle); GUILayout.Label(string.Format("#{0:000}  ·  R{1}  ·  {2}  ·  {3}", item.sequence, item.round, (item.side ?? "").ToUpperInvariant(), (item.category ?? "RULE").ToUpperInvariant()), smallStyle);
                GUILayout.BeginHorizontal(); GUILayout.Label(item.command, titleStyle); GUILayout.FlexibleSpace();
                if (item.rulePage > 0 && GUILayout.Button(new GUIContent("OPEN " + item.ruleSection, "Open the authoritative local Rules PDF at page " + item.rulePage + ". Viewer support for PDF page anchors varies."), GUILayout.Width(310f), GUILayout.Height(27f))) OpenRulesPage(item);
                GUILayout.EndHorizontal();
                GUILayout.Label(item.rulePage > 0 ? string.Format("SOURCE  {0} · PDF page {1}", item.ruleSection, item.rulePage) : "SOURCE  Scenario/interface procedure · no universal PDF section", smallStyle);
                if (!string.IsNullOrEmpty(item.inputs)) GUILayout.Label("INPUTS  " + item.inputs, guideStyle);
                if (!string.IsNullOrEmpty(item.computation)) GUILayout.Label("COMPUTATION  " + item.computation, guideStyle);
                if (!string.IsNullOrEmpty(item.outcome)) GUILayout.Label("RULING  " + item.outcome, guideStyle); GUILayout.EndVertical(); GUILayout.Space(7);
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var matrix = GUI.matrix; var angle = Vector3.Angle(end - start, Vector2.right); if (start.y > end.y) angle = -angle;
            GUI.color = color; GUIUtility.RotateAroundPivot(angle, start); GUI.DrawTexture(new Rect(start.x, start.y, (end - start).magnitude, width), pixel); GUI.matrix = matrix; GUI.color = Color.white;
        }
    }
}
