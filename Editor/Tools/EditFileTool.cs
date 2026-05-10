using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace HusnainUnityAI
{
    public class EditFileTool : ITool
    {
        public string Name => "edit_file";
        public string Description =>
            "Edits a file by replacing one exact string with another. " +
            "Fails if old_string appears more than once (be more specific) or zero times (string not found). " +
            "Path is project-relative. AssetDatabase is refreshed after.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"":       { ""type"": ""string"", ""description"": ""Project-relative file path"" },
                ""old_string"": { ""type"": ""string"", ""description"": ""Exact string to replace. Must be unique in the file."" },
                ""new_string"": { ""type"": ""string"", ""description"": ""Replacement string."" }
            },
            ""required"": [""path"", ""old_string"", ""new_string""]
        }");

        public bool RequiresApproval => true;

        public string PreviewSummary(JObject input)
        {
            var oldLen = input?["old_string"]?.ToString().Length ?? 0;
            var newLen = input?["new_string"]?.ToString().Length ?? 0;
            return $"edit {input?["path"]}  ({oldLen} -> {newLen} chars)";
        }

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var path = input?["path"]?.ToString();
                var oldStr = input?["old_string"]?.ToString() ?? "";
                var newStr = input?["new_string"]?.ToString() ?? "";

                if (!ProjectPaths.TryResolve(path, out var full, out var error))
                {
                    onResult(ToolExecutionResult.Err(error));
                    return;
                }
                if (!File.Exists(full))
                {
                    onResult(ToolExecutionResult.Err("file not found: " + path));
                    return;
                }
                if (string.IsNullOrEmpty(oldStr))
                {
                    onResult(ToolExecutionResult.Err("old_string is empty"));
                    return;
                }

                var content = File.ReadAllText(full);
                int first = content.IndexOf(oldStr, StringComparison.Ordinal);
                if (first < 0)
                {
                    onResult(ToolExecutionResult.Err("old_string not found in file"));
                    return;
                }
                int second = content.IndexOf(oldStr, first + oldStr.Length, StringComparison.Ordinal);
                if (second >= 0)
                {
                    onResult(ToolExecutionResult.Err(
                        "old_string appears more than once. Provide a longer, unique snippet."));
                    return;
                }

                var updated = content.Substring(0, first) + newStr + content.Substring(first + oldStr.Length);
                File.WriteAllText(full, updated);
                AssetDatabase.Refresh();
                onResult(ToolExecutionResult.Ok(
                    $"edited {ProjectPaths.Relative(full)} (replaced 1 occurrence)"));
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }
    }
}
