using System;

namespace HusnainUnityAI
{
    [Serializable]
    public class ToolCallRecord
    {
        public string ToolUseId;
        public string Name;
        public string InputJson;
    }

    [Serializable]
    public class ToolResultRecord
    {
        public string ToolUseId;
        public string Content;
        public bool IsError;
    }
}
