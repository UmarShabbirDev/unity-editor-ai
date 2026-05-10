using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class MessageRequest
    {
        public string model;
        public int max_tokens;
        public string system;
        public List<OutgoingMessage> messages;
        public List<ToolDefinition> tools;
    }

    public class ToolDefinition
    {
        public string name;
        public string description;
        public JObject input_schema;
    }

    public class OutgoingMessage
    {
        public string role;
        public List<OutgoingContentBlock> content;
    }

    public class OutgoingContentBlock
    {
        public string type;
        public string text;
        public OutgoingSource source;

        public string id;
        public string name;
        public JObject input;

        public string tool_use_id;
        public object content;
        public bool? is_error;
    }

    public class OutgoingSource
    {
        public string type;
        public string media_type;
        public string data;
    }

    public class MessageResponse
    {
        public string id;
        public string type;
        public string role;
        public string model;
        public List<ContentBlock> content;
        public string stop_reason;
        public Usage usage;
    }

    public class ContentBlock
    {
        public string type;
        public string text;

        public string id;
        public string name;
        public JObject input;
    }

    public class Usage
    {
        public int input_tokens;
        public int output_tokens;
        public int cache_read_input_tokens;
        public int cache_creation_input_tokens;
    }

    public class AnthropicErrorResponse
    {
        public string type;
        public AnthropicError error;
    }

    public class AnthropicError
    {
        public string type;
        public string message;
    }
}
