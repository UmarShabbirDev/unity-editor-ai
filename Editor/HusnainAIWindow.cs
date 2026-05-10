using UnityEditor;
using UnityEngine;

namespace HusnainUnityAI
{
    public class HusnainAIWindow : EditorWindow
    {
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
            }
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
    }
}
