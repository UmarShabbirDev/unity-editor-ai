using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HusnainUnityAI
{
    public class HusnainAIWindow : EditorWindow
    {
        [Serializable]
        public struct ChatTurn
        {
            public string Role;
            public string Text;
            public List<ToolCallRecord> ToolCalls;
            public List<ToolResultRecord> ToolResults;
        }

        [SerializeField] List<ChatTurn> _turns = new List<ChatTurn>();
        [SerializeField] string _input = "";

        Vector2 _scroll;
        bool _waiting;
        string _error;
        bool _showSettings;

        string _apiKeyDraft;
        string _modelDraft;
        string _systemDraft;
        bool _showApiKey;

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
            if (_turns == null) _turns = new List<ChatTurn>();
        }

        void OnGUI()
        {
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

            DrawTranscript();
            DrawInput();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(HusnainAISettings.Model, EditorStyles.boldLabel);
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
                        EditorGUILayout.LabelField("  " + prefix + " " + snippet, EditorStyles.miniLabel);
                        GUI.color = prev;
                    }
                    continue;
                }

                var label = t.Role == "user" ? "You" : "Husnain AI";
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

                if (!string.IsNullOrEmpty(t.Text))
                {
                    var textStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = true,
                        richText = false,
                    };
                    EditorGUILayout.SelectableLabel(
                        t.Text,
                        textStyle,
                        GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 2),
                        GUILayout.ExpandHeight(true));
                }

                if (t.ToolCalls != null && t.ToolCalls.Count > 0)
                {
                    foreach (var tc in t.ToolCalls)
                    {
                        var preview = ToolPreview(tc);
                        EditorGUILayout.LabelField("  ⚙ " + preview, EditorStyles.miniLabel);
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
            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_waiting))
            {
                _input = EditorGUILayout.TextArea(_input, GUILayout.Height(80));
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_waiting || string.IsNullOrWhiteSpace(_input)))
            {
                if (GUILayout.Button("Send", GUILayout.Width(80), GUILayout.Height(24)))
                {
                    Send();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        static string Truncate(string s, int n)
        {
            if (s == null) return "";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > n ? s.Substring(0, n) + "…" : s;
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

        void Send()
        {
            var prompt = _input.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            _turns.Add(new ChatTurn { Role = "user", Text = prompt });
            _input = "";
            _error = null;
            _waiting = true;
            Repaint();

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
                    Repaint();
                    return;
                }
            }
            _turns.Add(new ChatTurn
            {
                Role = "assistant",
                ToolCalls = new List<ToolCallRecord> { tc },
            });
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
                    Repaint();
                    return;
                }
            }
            _turns.Add(new ChatTurn
            {
                Role = "user",
                ToolResults = new List<ToolResultRecord> { tr },
            });
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

        List<OutgoingMessage> BuildOutgoing()
        {
            var list = new List<OutgoingMessage>(_turns.Count);
            foreach (var t in _turns)
            {
                var blocks = new List<OutgoingContentBlock>();

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

                list.Add(new OutgoingMessage { role = t.Role, content = blocks });
            }
            return list;
        }
    }
}
