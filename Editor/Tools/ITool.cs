using System;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class ToolExecutionResult
    {
        public string Text;
        public bool IsError;

        public static ToolExecutionResult Ok(string text) =>
            new ToolExecutionResult { Text = text, IsError = false };

        public static ToolExecutionResult Err(string text) =>
            new ToolExecutionResult { Text = text, IsError = true };
    }

    public interface ITool
    {
        string Name { get; }
        string Description { get; }
        JObject InputSchema { get; }
        bool RequiresApproval { get; }
        string PreviewSummary(JObject input);
        void Execute(JObject input, Action<ToolExecutionResult> onResult);
    }
}
