using System;
using System.Collections.Generic;
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
                var label = t.Role == "user" ? "You" : "Husnain AI";
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
                if (!string.IsNullOrEmpty(t.Text))
                {
                    var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                    EditorGUILayout.SelectableLabel(t.Text, style,
                        GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 2),
                        GUILayout.ExpandHeight(true));
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

        void Send()
        {
            var prompt = _input.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            _turns.Add(new ChatTurn { Role = "user", Text = prompt });
            _input = "";
            _error = null;
            _waiting = true;
            Repaint();

            var messages = new List<OutgoingMessage>();
            foreach (var t in _turns)
            {
                messages.Add(new OutgoingMessage
                {
                    role = t.Role,
                    content = new List<OutgoingContentBlock>
                    {
                        new OutgoingContentBlock { type = "text", text = t.Text }
                    },
                });
            }

            var req = new MessageRequest
            {
                model = HusnainAISettings.Model,
                max_tokens = HusnainAISettings.MaxTokens,
                system = HusnainAISettings.SystemPrompt,
                messages = messages,
            };

            AnthropicClient.SendMessage(HusnainAISettings.ApiKey, req,
                response =>
                {
                    _waiting = false;
                    string textOut = null;
                    if (response?.content != null)
                    {
                        foreach (var b in response.content)
                        {
                            if (b?.type == "text" && !string.IsNullOrEmpty(b.text))
                            {
                                textOut = textOut == null ? b.text : textOut + "\n\n" + b.text;
                            }
                        }
                    }
                    if (textOut != null) _turns.Add(new ChatTurn { Role = "assistant", Text = textOut });
                    _scroll = new Vector2(_scroll.x, float.MaxValue);
                    Repaint();
                },
                err =>
                {
                    _waiting = false;
                    _error = err;
                    Repaint();
                });
        }
    }
}
