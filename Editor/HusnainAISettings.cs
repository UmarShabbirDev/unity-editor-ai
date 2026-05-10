using UnityEditor;

namespace HusnainUnityAI
{
    public static class HusnainAISettings
    {
        const string ApiKeyPref = "HusnainUnityAI.ApiKey";
        const string ModelPref = "HusnainUnityAI.Model";
        const string MaxTokensPref = "HusnainUnityAI.MaxTokens";
        const string SystemPromptPref = "HusnainUnityAI.SystemPrompt";

        public const string DefaultModel = "claude-opus-4-7";
        public const int DefaultMaxTokens = 8192;

        public const string DefaultSystemPrompt =
@"You are an AI assistant embedded in the Unity Editor. You help with Unity projects: explaining code, suggesting changes, and answering questions about Unity APIs.

Be concise. Reference file paths with line numbers when relevant.";

        public static string ApiKey
        {
            get => EditorPrefs.GetString(ApiKeyPref, "");
            set => EditorPrefs.SetString(ApiKeyPref, value ?? "");
        }

        public static string Model
        {
            get => EditorPrefs.GetString(ModelPref, DefaultModel);
            set => EditorPrefs.SetString(ModelPref, string.IsNullOrEmpty(value) ? DefaultModel : value);
        }

        public static int MaxTokens
        {
            get => EditorPrefs.GetInt(MaxTokensPref, DefaultMaxTokens);
            set => EditorPrefs.SetInt(MaxTokensPref, value <= 0 ? DefaultMaxTokens : value);
        }

        public static string SystemPrompt
        {
            get => EditorPrefs.GetString(SystemPromptPref, DefaultSystemPrompt);
            set => EditorPrefs.SetString(SystemPromptPref, string.IsNullOrEmpty(value) ? DefaultSystemPrompt : value);
        }

        public static void ClearApiKey() => EditorPrefs.DeleteKey(ApiKeyPref);
    }
}
