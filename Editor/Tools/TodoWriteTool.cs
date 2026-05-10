using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class TodoWriteTool : ITool
    {
        public string Name => "todo_write";
        public string Description =>
            "Maintain a structured todo list visible to the user. Use this for multi-step tasks " +
            "(3+ items). Each call REPLACES the entire list with the provided items. " +
            "Mark exactly ONE task as in_progress at a time. Mark tasks completed immediately when done. " +
            "Each todo has: content (imperative form, e.g. 'Add Player.cs'), activeForm (present " +
            "continuous, e.g. 'Adding Player.cs'), and status (pending | in_progress | completed).";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""todos"": {
                    ""type"": ""array"",
                    ""description"": ""Full ordered list of todos. Replaces previous list."",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""content"":    { ""type"": ""string"", ""description"": ""Imperative form, e.g. 'Build Player'"" },
                            ""activeForm"": { ""type"": ""string"", ""description"": ""Present continuous, e.g. 'Building Player'"" },
                            ""status"":     { ""type"": ""string"", ""enum"": [""pending"", ""in_progress"", ""completed""] }
                        },
                        ""required"": [""content"", ""status""]
                    }
                }
            },
            ""required"": [""todos""]
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input)
        {
            var arr = input?["todos"] as JArray;
            return arr == null ? "todo_write" : $"todo_write ({arr.Count} items)";
        }

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var arr = input?["todos"] as JArray;
                if (arr == null)
                {
                    onResult(ToolExecutionResult.Err("'todos' array is required"));
                    return;
                }

                int pending = 0, inProgress = 0, completed = 0;
                var sb = new StringBuilder();

                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i] as JObject;
                    if (item == null) continue;
                    var content = item["content"]?.ToString() ?? "(no content)";
                    var activeForm = item["activeForm"]?.ToString();
                    var status = item["status"]?.ToString() ?? "pending";

                    string marker;
                    switch (status)
                    {
                        case "completed": marker = "[x]"; completed++; break;
                        case "in_progress": marker = "[>]"; inProgress++; break;
                        default: marker = "[ ]"; pending++; break;
                    }

                    var label = status == "in_progress" && !string.IsNullOrEmpty(activeForm)
                        ? activeForm
                        : content;
                    sb.AppendLine($"{marker} {label}");
                }

                var summary = $"Todos ({arr.Count}): {pending} pending, {inProgress} in progress, {completed} completed";
                onResult(ToolExecutionResult.Ok(summary + "\n" + sb.ToString().TrimEnd()));
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }
    }
}
