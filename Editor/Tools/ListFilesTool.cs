using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class ListFilesTool : ITool
    {
        const int MaxEntries = 500;

        public string Name => "list_files";
        public string Description =>
            "Lists files and folders in a project directory (non-recursive). " +
            "Path is project-relative. Hidden files and .meta files are excluded.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""Project-relative directory path. Use '.' for project root."" }
            },
            ""required"": [""path""]
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input) => $"list {input?["path"]}";

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var path = input?["path"]?.ToString();
                if (path == ".") path = "";
                if (!ProjectPaths.TryResolve(path ?? "", out var full, out var error))
                {
                    onResult(ToolExecutionResult.Err(error));
                    return;
                }
                if (!Directory.Exists(full))
                {
                    onResult(ToolExecutionResult.Err("directory not found: " + path));
                    return;
                }

                var sb = new StringBuilder();
                int count = 0;

                foreach (var dir in Directory.GetDirectories(full).OrderBy(p => p))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(".")) continue;
                    sb.AppendLine(name + "/");
                    if (++count >= MaxEntries) break;
                }
                if (count < MaxEntries)
                {
                    foreach (var file in Directory.GetFiles(full).OrderBy(p => p))
                    {
                        var name = Path.GetFileName(file);
                        if (name.StartsWith(".") || name.EndsWith(".meta")) continue;
                        var info = new FileInfo(file);
                        sb.AppendLine($"{name}  ({info.Length} bytes)");
                        if (++count >= MaxEntries) break;
                    }
                }

                if (sb.Length == 0)
                {
                    onResult(ToolExecutionResult.Ok("(empty)"));
                }
                else
                {
                    if (count >= MaxEntries) sb.AppendLine($"...truncated at {MaxEntries} entries");
                    onResult(ToolExecutionResult.Ok(sb.ToString().TrimEnd()));
                }
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }
    }
}
