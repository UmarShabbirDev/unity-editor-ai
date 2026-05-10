using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HusnainUnityAI
{
    public class HusnainAIWindow : EditorWindow
    {
        [Serializable]
        public class Attachment
        {
            public string Filename;
            public string MediaType;
            public string Base64Data;
            public bool IsImage;
            public long SizeBytes;
        }

        [Serializable]
        public struct ChatTurn
        {
            public string Role;
            public string Text;
            public List<Attachment> Attachments;
            public List<ToolCallRecord> ToolCalls;
            public List<ToolResultRecord> ToolResults;
        }

        [SerializeField] List<ChatTurn> _turns = new List<ChatTurn>();
        [SerializeField] List<Attachment> _pendingAttachments = new List<Attachment>();
        [SerializeField] string _input = "";
        [SerializeField] string _conversationId;
        [SerializeField] string _conversationTitle = "New conversation";
        [SerializeField] string _conversationCreatedAt;
        [SerializeField] bool _showSidebar = true;

        Vector2 _scroll;
        Vector2 _inputScroll;
        Vector2 _sidebarScroll;
        bool _waiting;
        string _error;
        bool _showSettings;

        string _apiKeyDraft;
        string _modelDraft;
        string _systemDraft;
        bool _showApiKey;

        readonly List<string> _undoStack = new List<string>();
        readonly List<string> _redoStack = new List<string>();
        string _lastInputSnapshot = "";
        double _lastSnapshotTime;
        const int MaxUndoDepth = 100;
        const double SnapshotIntervalSec = 0.4;

        const long MaxImageBytes = 5L * 1024 * 1024;
        const long MaxPdfBytes = 32L * 1024 * 1024;
        const float SidebarWidth = 220f;

        List<ConversationMeta> _conversationsList = new List<ConversationMeta>();
        AgentLoop _activeLoop;

        [MenuItem("Window/Husnain AI")]
        public static void Open()
        {
            var w = GetWindow<HusnainAIWindow>("Husnain AI");
            w.minSize = new Vector2(560, 600);
            w.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Husnain AI");
            _lastInputSnapshot = _input ?? "";

            if (_turns == null) _turns = new List<ChatTurn>();
            if (_pendingAttachments == null) _pendingAttachments = new List<Attachment>();

            RefreshConversations();

            if (!string.IsNullOrEmpty(_conversationId))
            {
                var snap = ChatHistory.Load(_conversationId);
                if (snap == null)
                {
                    _conversationId = null;
                    _conversationTitle = "New conversation";
                    _turns = new List<ChatTurn>();
                }
            }

            if (string.IsNullOrEmpty(_conversationId))
            {
                if (_conversationsList.Count > 0)
                {
                    LoadConversation(_conversationsList[0].Id);
                }
                else
                {
                    StartNewConversation(persistImmediately: false);
                }
            }
        }

        void OnGUI()
        {
            HandleInputUndoShortcuts();
            DrawHeader();

            if (_showSettings)
            {
                DrawSettings();
                return;
            }

            if (string.IsNullOrEmpty(HusnainAISettings.ApiKey))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Set your Anthropic API key to start chatting. Click Settings above.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (_showSidebar)
            {
                DrawSidebar();
            }
            EditorGUILayout.BeginVertical();
            DrawTranscript();
            DrawInput();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button(_showSidebar ? "Hide Chats" : "Chats",
                                 EditorStyles.toolbarButton,
                                 GUILayout.Width(80)))
            {
                _showSidebar = !_showSidebar;
            }

            var headerLabel = string.IsNullOrEmpty(_conversationTitle)
                ? HusnainAISettings.Model
                : _conversationTitle + "  ·  " + HusnainAISettings.Model;
            GUILayout.Label(headerLabel, EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            var approvalLabel = HusnainAISettings.AutoApproveEdits ? "Auto-accept: ON" : "Auto-accept: OFF";
            var approvalColor = HusnainAISettings.AutoApproveEdits
                ? new Color(0.4f, 0.8f, 0.4f)
                : new Color(0.85f, 0.85f, 0.85f);
            var prevColor = GUI.color;
            GUI.color = approvalColor;
            if (GUILayout.Button(approvalLabel, EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                HusnainAISettings.AutoApproveEdits = !HusnainAISettings.AutoApproveEdits;
            }
            GUI.color = prevColor;

            if (_waiting && _activeLoop != null)
            {
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    _activeLoop.Stop();
                }
            }

            if (GUILayout.Button(_showSettings ? "Close" : "Settings",
                                 EditorStyles.toolbarButton,
                                 GUILayout.Width(64)))
            {
                _showSettings = !_showSettings;
                if (_showSettings)
                {
                    _apiKeyDraft = HusnainAISettings.ApiKey;
                    _modelDraft = HusnainAISettings.Model;
                    _systemDraft = HusnainAISettings.SystemPrompt;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("+ New chat", EditorStyles.toolbarButton))
            {
                AbandonActiveLoop();
                StartNewConversation(persistImmediately: true);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("⟳", EditorStyles.toolbarButton, GUILayout.Width(28)))
            {
                RefreshConversations();
            }
            EditorGUILayout.EndHorizontal();

            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);

            string toDelete = null;
            string toLoad = null;

            foreach (var c in _conversationsList)
            {
                bool isActive = c.Id == _conversationId;
                var rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.label,
                                                     GUILayout.Height(40),
                                                     GUILayout.ExpandWidth(true));

                if (isActive)
                {
                    EditorGUI.DrawRect(rect, new Color(1f, 0.65f, 0.18f, 0.18f));
                }

                var bodyRect = new Rect(rect.x + 4, rect.y + 2, rect.width - 26, rect.height - 4);
                var titleStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                };
                var titleRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width, 18);
                var dateRect = new Rect(bodyRect.x, bodyRect.y + 18, bodyRect.width, 14);

                GUI.Label(titleRect, c.Title, titleStyle);
                GUI.Label(dateRect, RelativeTime(c.UpdatedAt), EditorStyles.miniLabel);

                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && bodyRect.Contains(Event.current.mousePosition))
                {
                    toLoad = c.Id;
                    Event.current.Use();
                }

                var delRect = new Rect(rect.xMax - 22, rect.y + (rect.height - 18) * 0.5f, 18, 18);
                if (GUI.Button(delRect, "×", EditorStyles.miniButton))
                {
                    toDelete = c.Id;
                }
            }

            if (toLoad != null && toLoad != _conversationId)
            {
                AbandonActiveLoop();
                SaveCurrent();
                LoadConversation(toLoad);
                Repaint();
            }

            if (toDelete != null)
            {
                if (EditorUtility.DisplayDialog(
                        "Delete conversation?",
                        "This permanently removes this conversation. This cannot be undone.",
                        "Delete", "Cancel"))
                {
                    ChatHistory.Delete(toDelete);
                    if (toDelete == _conversationId)
                    {
                        _conversationId = null;
                        RefreshConversations();
                        if (_conversationsList.Count > 0)
                        {
                            LoadConversation(_conversationsList[0].Id);
                        }
                        else
                        {
                            StartNewConversation(persistImmediately: false);
                        }
                    }
                    else
                    {
                        RefreshConversations();
                    }
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Anthropic API Key", EditorStyles.boldLabel);
            _showApiKey = EditorGUILayout.Toggle("Show key", _showApiKey);
            _apiKeyDraft = _showApiKey
                ? EditorGUILayout.TextField("API key", _apiKeyDraft)
                : EditorGUILayout.PasswordField("API key", _apiKeyDraft);
            EditorGUILayout.HelpBox(
                "Stored in EditorPrefs on this machine. Sent only to api.anthropic.com.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
            _modelDraft = EditorGUILayout.TextField("Model", _modelDraft);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Permissions", EditorStyles.boldLabel);
            var auto = EditorGUILayout.Toggle("Auto-accept edits (write_file / edit_file)",
                HusnainAISettings.AutoApproveEdits);
            if (auto != HusnainAISettings.AutoApproveEdits)
            {
                HusnainAISettings.AutoApproveEdits = auto;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("System prompt", EditorStyles.boldLabel);
            _systemDraft = EditorGUILayout.TextArea(_systemDraft, GUILayout.MinHeight(120));

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                HusnainAISettings.ApiKey = _apiKeyDraft?.Trim() ?? "";
                HusnainAISettings.Model = _modelDraft?.Trim() ?? "";
                HusnainAISettings.SystemPrompt = _systemDraft ?? "";
                _showSettings = false;
            }
            if (GUILayout.Button("Reset model"))
            {
                _modelDraft = HusnainAISettings.DefaultModel;
            }
            if (GUILayout.Button("Reset prompt"))
            {
                _systemDraft = HusnainAISettings.DefaultSystemPrompt;
            }
            if (GUILayout.Button("Clear API key"))
            {
                _apiKeyDraft = "";
                HusnainAISettings.ClearApiKey();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Conversation storage", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(ChatHistory.Dir, EditorStyles.miniLabel,
                                             GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        void DrawTranscript()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var t in _turns)
            {
                EditorGUILayout.Space(6);

                bool isToolResultTurn = t.Role == "user"
                                        && t.ToolResults != null
                                        && t.ToolResults.Count > 0;

                if (isToolResultTurn)
                {
                    foreach (var r in t.ToolResults)
                    {
                        var prefix = r.IsError ? "✕" : "→";
                        var snippet = Truncate(r.Content ?? "", 240);
                        var color = r.IsError ? new Color(0.95f, 0.5f, 0.5f) : new Color(0.6f, 0.85f, 0.6f);
                        var prev = GUI.color;
                        GUI.color = color;
                        EditorGUILayout.SelectableLabel(
                            "  " + prefix + " " + snippet,
                            EditorStyles.miniLabel,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        GUI.color = prev;
                    }
                    continue;
                }

                var label = t.Role == "user" ? "You" : "Husnain AI";
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

                if (t.Attachments != null && t.Attachments.Count > 0)
                {
                    foreach (var a in t.Attachments)
                    {
                        var kind = a.IsImage ? "image" : "doc";
                        EditorGUILayout.SelectableLabel(
                            $"  [{kind}] {a.Filename} ({FormatSize(a.SizeBytes)})",
                            EditorStyles.miniLabel,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
                }

                if (!string.IsNullOrEmpty(t.Text))
                {
                    var textStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = true,
                        richText = false,
                    };
                    float availW = position.width - (_showSidebar ? SidebarWidth : 0f) - 40f;
                    float h = textStyle.CalcHeight(new GUIContent(t.Text), availW);
                    EditorGUILayout.SelectableLabel(
                        t.Text,
                        textStyle,
                        GUILayout.Height(h + 6f));
                }

                if (t.ToolCalls != null && t.ToolCalls.Count > 0)
                {
                    foreach (var tc in t.ToolCalls)
                    {
                        var preview = ToolPreview(tc);
                        EditorGUILayout.SelectableLabel(
                            "  ⚙ " + preview,
                            EditorStyles.miniLabel,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
                }
            }

            if (_waiting)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Husnain AI", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Working…", EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawInput()
        {
            if (_pendingAttachments.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Attachments:", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                int removeIndex = -1;
                for (int i = 0; i < _pendingAttachments.Count; i++)
                {
                    var a = _pendingAttachments[i];
                    var kind = a.IsImage ? "img" : "doc";
                    var label = $"[{kind}] {a.Filename}  ✕";
                    if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.MaxWidth(220)))
                    {
                        removeIndex = i;
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                if (removeIndex >= 0) _pendingAttachments.RemoveAt(removeIndex);
            }

            EditorGUILayout.Space(4);
            {
                var inputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                float maxH = Mathf.Max(160f, position.height * 0.55f);
                float availW = position.width - (_showSidebar ? SidebarWidth : 0f) - 30f;
                var measureText = string.IsNullOrEmpty(_input) ? " " : _input + "\n ";
                float contentH = inputStyle.CalcHeight(new GUIContent(measureText), availW);
                float dynamicH = Mathf.Clamp(contentH + 8f, 140f, maxH);
                _inputScroll = EditorGUILayout.BeginScrollView(
                    _inputScroll,
                    GUILayout.Height(dynamicH));
                _input = EditorGUILayout.TextArea(
                    _input, inputStyle,
                    GUILayout.ExpandHeight(true),
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.EndScrollView();
            }
            TrackInputForUndo();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_waiting))
            {
                if (GUILayout.Button("Attach", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    AttachFile();
                }
            }

            GUILayout.FlexibleSpace();
            bool canSend = !_waiting
                           && (!string.IsNullOrWhiteSpace(_input) || _pendingAttachments.Count > 0);
            using (new EditorGUI.DisabledScope(!canSend))
            {
                if (GUILayout.Button("Send", GUILayout.Width(80), GUILayout.Height(24)))
                {
                    Send();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        void AttachFile()
        {
            string path = EditorUtility.OpenFilePanel(
                "Attach image or PDF",
                "",
                "png,jpg,jpeg,gif,webp,pdf");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return;

                string mediaType = MediaTypeFor(fi.Extension);
                if (mediaType == null)
                {
                    EditorUtility.DisplayDialog(
                        "Unsupported file",
                        "Only PNG, JPEG, GIF, WebP and PDF are supported.",
                        "OK");
                    return;
                }

                bool isImage = mediaType.StartsWith("image/");
                long limit = isImage ? MaxImageBytes : MaxPdfBytes;
                if (fi.Length > limit)
                {
                    EditorUtility.DisplayDialog(
                        "File too large",
                        $"Max size is {FormatSize(limit)}. This file is {FormatSize(fi.Length)}.",
                        "OK");
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                _pendingAttachments.Add(new Attachment
                {
                    Filename = fi.Name,
                    MediaType = mediaType,
                    Base64Data = Convert.ToBase64String(bytes),
                    IsImage = isImage,
                    SizeBytes = fi.Length,
                });
                Repaint();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Failed to attach", e.Message, "OK");
            }
        }

        void HandleInputUndoShortcuts()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            bool mod = e.control || e.command;
            if (!mod) return;

            if (e.keyCode == KeyCode.Z && !e.shift)
            {
                if (_undoStack.Count > 0)
                {
                    _redoStack.Add(_input ?? "");
                    _input = _undoStack[_undoStack.Count - 1];
                    _undoStack.RemoveAt(_undoStack.Count - 1);
                    _lastInputSnapshot = _input ?? "";
                    GUI.changed = true;
                    e.Use();
                    Repaint();
                }
            }
            else if ((e.keyCode == KeyCode.Z && e.shift) || e.keyCode == KeyCode.Y)
            {
                if (_redoStack.Count > 0)
                {
                    _undoStack.Add(_input ?? "");
                    _input = _redoStack[_redoStack.Count - 1];
                    _redoStack.RemoveAt(_redoStack.Count - 1);
                    _lastInputSnapshot = _input ?? "";
                    GUI.changed = true;
                    e.Use();
                    Repaint();
                }
            }
        }

        void TrackInputForUndo()
        {
            var current = _input ?? "";
            if (current == _lastInputSnapshot) return;

            var now = EditorApplication.timeSinceStartup;
            bool isLargeChange = Math.Abs(current.Length - _lastInputSnapshot.Length) > 32;
            if (isLargeChange || now - _lastSnapshotTime > SnapshotIntervalSec)
            {
                _undoStack.Add(_lastInputSnapshot);
                if (_undoStack.Count > MaxUndoDepth) _undoStack.RemoveAt(0);
                _redoStack.Clear();
                _lastSnapshotTime = now;
            }
            _lastInputSnapshot = current;
        }

        static string MediaTypeFor(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".pdf": return "application/pdf";
                default: return null;
            }
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        static string Truncate(string s, int n)
        {
            if (s == null) return "";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > n ? s.Substring(0, n) + "…" : s;
        }

        static string RelativeTime(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            var diff = DateTime.UtcNow - utc;
            if (diff.TotalSeconds < 60) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return utc.ToLocalTime().ToString("MMM d");
        }

        static string ToolPreview(ToolCallRecord tc)
        {
            var tool = ToolRegistry.Get(tc.Name);
            if (tool == null) return tc.Name;
            try
            {
                var input = JObject.Parse(tc.InputJson ?? "{}");
                return tool.PreviewSummary(input);
            }
            catch
            {
                return tc.Name;
            }
        }

        void RefreshConversations()
        {
            _conversationsList = ChatHistory.List();
        }

        void StartNewConversation(bool persistImmediately)
        {
            _conversationId = ChatHistory.NewId();
            _conversationTitle = "New conversation";
            _conversationCreatedAt = DateTime.UtcNow.ToString("o");
            _turns.Clear();
            _pendingAttachments.Clear();
            _input = "";
            _lastInputSnapshot = "";
            _undoStack.Clear();
            _redoStack.Clear();
            _error = null;
            if (persistImmediately) SaveCurrent();
            RefreshConversations();
        }

        void LoadConversation(string id)
        {
            var snap = ChatHistory.Load(id);
            if (snap == null) return;
            _conversationId = snap.id;
            _conversationTitle = string.IsNullOrEmpty(snap.title) ? "New conversation" : snap.title;
            _conversationCreatedAt = snap.createdAt;
            _turns = snap.turns ?? new List<ChatTurn>();
            _pendingAttachments.Clear();
            _input = "";
            _lastInputSnapshot = "";
            _undoStack.Clear();
            _redoStack.Clear();
            _error = null;
        }

        void SaveCurrent()
        {
            if (string.IsNullOrEmpty(_conversationId))
            {
                _conversationId = ChatHistory.NewId();
            }

            if ((_conversationTitle == "New conversation" || string.IsNullOrEmpty(_conversationTitle))
                && _turns.Count > 0)
            {
                _conversationTitle = ChatHistory.AutoTitle(_turns);
            }

            ChatHistory.Save(new ChatHistorySnapshot
            {
                id = _conversationId,
                title = _conversationTitle,
                createdAt = _conversationCreatedAt,
                model = HusnainAISettings.Model,
                turns = _turns,
            });
        }

        void AbandonActiveLoop()
        {
            if (_activeLoop != null)
            {
                _activeLoop.Stop();
                _activeLoop = null;
            }
            _waiting = false;
        }

        void Send()
        {
            var prompt = _input.Trim();
            bool hasText = !string.IsNullOrEmpty(prompt);
            bool hasAttachments = _pendingAttachments.Count > 0;
            if (!hasText && !hasAttachments) return;

            var turn = new ChatTurn
            {
                Role = "user",
                Text = prompt,
                Attachments = hasAttachments ? new List<Attachment>(_pendingAttachments) : null,
            };
            _turns.Add(turn);
            _pendingAttachments.Clear();
            _input = "";
            _error = null;
            _waiting = true;
            Repaint();

            SaveCurrent();
            RefreshConversations();

            var system = HusnainAISettings.SystemPrompt
                + "\n\nProject root (real path): " + ProjectPaths.ProjectRoot
                + "\nUnity version: " + Application.unityVersion;

            var initialMessages = BuildOutgoing();
            AgentLoop loop = null;
            loop = new AgentLoop(
                HusnainAISettings.ApiKey,
                HusnainAISettings.Model,
                system,
                HusnainAISettings.MaxTokens,
                initialMessages,
                onToolCallStarted: tc => { if (_activeLoop == loop) OnAgentToolCall(tc); },
                onToolResult:      tr => { if (_activeLoop == loop) OnAgentToolResult(tr); },
                onAssistantText:   tx => { if (_activeLoop == loop) OnAgentText(tx); },
                onError: msg =>
                {
                    if (_activeLoop != loop) return;
                    _error = msg;
                    Repaint();
                },
                onDone: () =>
                {
                    if (_activeLoop != loop) return;
                    _waiting = false;
                    _activeLoop = null;
                    SaveCurrent();
                    RefreshConversations();
                    _scroll = new Vector2(_scroll.x, float.MaxValue);
                    Repaint();
                },
                approval: (tool, input) => _activeLoop == loop && OnApprovalRequest(tool, input));
            _activeLoop = loop;
            loop.Start();
        }

        void OnAgentText(string text)
        {
            _turns.Add(new ChatTurn { Role = "assistant", Text = text });
            SaveCurrent();
            _scroll = new Vector2(_scroll.x, float.MaxValue);
            Repaint();
        }

        void OnAgentToolCall(ToolCallRecord tc)
        {
            if (_turns.Count > 0)
            {
                var last = _turns[_turns.Count - 1];
                if (last.Role == "assistant"
                    && (last.ToolResults == null || last.ToolResults.Count == 0))
                {
                    if (last.ToolCalls == null) last.ToolCalls = new List<ToolCallRecord>();
                    last.ToolCalls.Add(tc);
                    _turns[_turns.Count - 1] = last;
                    SaveCurrent();
                    Repaint();
                    return;
                }
            }
            _turns.Add(new ChatTurn
            {
                Role = "assistant",
                ToolCalls = new List<ToolCallRecord> { tc },
            });
            SaveCurrent();
            _scroll = new Vector2(_scroll.x, float.MaxValue);
            Repaint();
        }

        void OnAgentToolResult(ToolResultRecord tr)
        {
            if (_turns.Count > 0)
            {
                var last = _turns[_turns.Count - 1];
                if (last.Role == "user"
                    && last.ToolResults != null
                    && string.IsNullOrEmpty(last.Text))
                {
                    last.ToolResults.Add(tr);
                    _turns[_turns.Count - 1] = last;
                    SaveCurrent();
                    Repaint();
                    return;
                }
            }
            _turns.Add(new ChatTurn
            {
                Role = "user",
                ToolResults = new List<ToolResultRecord> { tr },
            });
            SaveCurrent();
            _scroll = new Vector2(_scroll.x, float.MaxValue);
            Repaint();
        }

        bool OnApprovalRequest(ITool tool, JObject input)
        {
            if (HusnainAISettings.AutoApproveEdits) return true;

            var detail = "";
            var contentToken = input?["content"];
            if (contentToken != null)
            {
                var content = contentToken.ToString();
                var snippet = content.Length > 800
                    ? content.Substring(0, 800) + "\n\n[...truncated]"
                    : content;
                detail = "\n\n--- content preview ---\n" + snippet;
            }
            var newToken = input?["new_string"];
            var oldToken = input?["old_string"];
            if (newToken != null || oldToken != null)
            {
                detail = "\n\n--- old_string ---\n" + Snip(oldToken?.ToString(), 400)
                       + "\n\n--- new_string ---\n" + Snip(newToken?.ToString(), 400);
            }

            return EditorUtility.DisplayDialog(
                "Approve " + tool.Name + "?",
                tool.PreviewSummary(input) + detail,
                "Approve", "Reject");
        }

        static string Snip(string s, int n)
        {
            if (s == null) return "(empty)";
            return s.Length > n ? s.Substring(0, n) + "\n[...truncated]" : s;
        }

        static void SanitizeOrphanToolUse(List<OutgoingMessage> messages)
        {
            // Drop any `tool_use` blocks that don't have a matching `tool_result`
            // in the immediately-following user message. Otherwise the API 400s
            // with "tool_use ids were found without tool_result blocks".
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.role != "assistant" || msg.content == null) continue;

                bool hasToolUse = false;
                foreach (var b in msg.content)
                {
                    if (b.type == "tool_use") { hasToolUse = true; break; }
                }
                if (!hasToolUse) continue;

                var resolvedIds = new HashSet<string>();
                if (i + 1 < messages.Count
                    && messages[i + 1].role == "user"
                    && messages[i + 1].content != null)
                {
                    foreach (var b in messages[i + 1].content)
                    {
                        if (b.type == "tool_result" && !string.IsNullOrEmpty(b.tool_use_id))
                        {
                            resolvedIds.Add(b.tool_use_id);
                        }
                    }
                }

                var filtered = new List<OutgoingContentBlock>();
                foreach (var b in msg.content)
                {
                    if (b.type == "tool_use"
                        && (string.IsNullOrEmpty(b.id) || !resolvedIds.Contains(b.id)))
                    {
                        continue;
                    }
                    filtered.Add(b);
                }
                if (filtered.Count == 0)
                {
                    filtered.Add(new OutgoingContentBlock { type = "text", text = "(prior tool call was interrupted)" });
                }
                msg.content = filtered;
            }

            // Also drop `tool_result` blocks whose `tool_use_id` doesn't exist —
            // the inverse case, less common but possible after history corruption.
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.role != "user" || msg.content == null) continue;

                bool hasToolResult = false;
                foreach (var b in msg.content)
                {
                    if (b.type == "tool_result") { hasToolResult = true; break; }
                }
                if (!hasToolResult) continue;

                var availableIds = new HashSet<string>();
                if (i > 0
                    && messages[i - 1].role == "assistant"
                    && messages[i - 1].content != null)
                {
                    foreach (var b in messages[i - 1].content)
                    {
                        if (b.type == "tool_use" && !string.IsNullOrEmpty(b.id))
                        {
                            availableIds.Add(b.id);
                        }
                    }
                }

                var filtered = new List<OutgoingContentBlock>();
                foreach (var b in msg.content)
                {
                    if (b.type == "tool_result"
                        && (string.IsNullOrEmpty(b.tool_use_id) || !availableIds.Contains(b.tool_use_id)))
                    {
                        continue;
                    }
                    filtered.Add(b);
                }
                if (filtered.Count == 0)
                {
                    filtered.Add(new OutgoingContentBlock { type = "text", text = "(continuing)" });
                }
                msg.content = filtered;
            }
        }

        List<OutgoingMessage> BuildOutgoing()
        {
            var list = new List<OutgoingMessage>(_turns.Count);
            foreach (var t in _turns)
            {
                var blocks = new List<OutgoingContentBlock>();

                if (t.Attachments != null)
                {
                    foreach (var a in t.Attachments)
                    {
                        blocks.Add(new OutgoingContentBlock
                        {
                            type = a.IsImage ? "image" : "document",
                            source = new OutgoingSource
                            {
                                type = "base64",
                                media_type = a.MediaType,
                                data = a.Base64Data,
                            },
                        });
                    }
                }

                if (!string.IsNullOrEmpty(t.Text))
                {
                    blocks.Add(new OutgoingContentBlock { type = "text", text = t.Text });
                }

                if (t.ToolCalls != null)
                {
                    foreach (var tc in t.ToolCalls)
                    {
                        JObject input;
                        try { input = JObject.Parse(tc.InputJson ?? "{}"); }
                        catch { input = new JObject(); }

                        blocks.Add(new OutgoingContentBlock
                        {
                            type = "tool_use",
                            id = tc.ToolUseId,
                            name = tc.Name,
                            input = input,
                        });
                    }
                }

                if (t.ToolResults != null)
                {
                    foreach (var tr in t.ToolResults)
                    {
                        blocks.Add(new OutgoingContentBlock
                        {
                            type = "tool_result",
                            tool_use_id = tr.ToolUseId,
                            content = tr.Content ?? "",
                            is_error = tr.IsError ? (bool?)true : null,
                        });
                    }
                }

                if (blocks.Count == 0)
                {
                    blocks.Add(new OutgoingContentBlock { type = "text", text = "(continuing)" });
                }

                list.Add(new OutgoingMessage { role = t.Role, content = blocks });
            }
            SanitizeOrphanToolUse(list);
            return list;
        }
    }
}
