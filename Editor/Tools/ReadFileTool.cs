using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class ReadFileTool : ITool
    {
        const int MaxBytes = 256 * 1024;

        public string Name => "read_file";
        public string Description =>
            "Reads the contents of a text file in the Unity project. Path is project-relative " +
            "(e.g. 'Assets/Scripts/Foo.cs'). Returns the file contents as a string. " +
            "Maximum 256 KB; larger files are truncated.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""Project-relative file path"" }
            },
            ""required"": [""path""]
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input) => $"read {input?["path"]}";

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var path = input?["path"]?.ToString();
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
                var info = new FileInfo(full);
                if (info.Length > MaxBytes)
                {
                    using (var sr = new StreamReader(full))
                    {
                        var buffer = new char[MaxBytes];
                        int read = sr.ReadBlock(buffer, 0, MaxBytes);
                        var truncated = new string(buffer, 0, read);
                        onResult(ToolExecutionResult.Ok(truncated +
                            $"\n\n[truncated: file is {info.Length} bytes, showing first {MaxBytes}]"));
                    }
                }
                else
                {
                    onResult(ToolExecutionResult.Ok(File.ReadAllText(full)));
                }
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }
    }
}
