using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace HusnainUnityAI
{
    public class WriteFileTool : ITool
    {
        public string Name => "write_file";
        public string Description =>
            "Creates a new file or overwrites an existing one in the Unity project. " +
            "Path is project-relative. Parent directories are created if needed. " +
            "After writing, AssetDatabase is refreshed so Unity picks up the change.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"":    { ""type"": ""string"", ""description"": ""Project-relative file path"" },
                ""content"": { ""type"": ""string"", ""description"": ""Full file contents"" }
            },
            ""required"": [""path"", ""content""]
        }");

        public bool RequiresApproval => true;

        public string PreviewSummary(JObject input)
        {
            var len = input?["content"]?.ToString().Length ?? 0;
            return $"write {input?["path"]}  ({len} chars)";
        }

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var path = input?["path"]?.ToString();
                var content = input?["content"]?.ToString() ?? "";
                if (!ProjectPaths.TryResolve(path, out var full, out var error))
                {
                    onResult(ToolExecutionResult.Err(error));
                    return;
                }
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, content);
                AssetDatabase.Refresh();
                onResult(ToolExecutionResult.Ok($"wrote {ProjectPaths.Relative(full)} ({content.Length} chars)"));
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }
    }
}
