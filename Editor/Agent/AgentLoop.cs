using System;
using System.Collections.Generic;

namespace HusnainUnityAI
{
    public class AgentLoop
    {
        readonly string _apiKey;
        readonly string _model;
        readonly string _system;
        readonly int _maxTokens;
        readonly List<OutgoingMessage> _messages;
        readonly Action<string> _onAssistantText;
        readonly Action<string> _onError;
        readonly Action _onDone;

        bool _stopped;

        public AgentLoop(
            string apiKey,
            string model,
            string system,
            int maxTokens,
            List<OutgoingMessage> messages,
            Action<string> onAssistantText,
            Action<string> onError,
            Action onDone)
        {
            _apiKey = apiKey;
            _model = model;
            _system = system;
            _maxTokens = maxTokens;
            _messages = messages;
            _onAssistantText = onAssistantText;
            _onError = onError;
            _onDone = onDone;
        }

        public void Stop() { _stopped = true; }

        public void Start()
        {
            var payload = new MessageRequest
            {
                model = _model,
                max_tokens = _maxTokens,
                system = _system,
                messages = _messages,
                tools = ToolRegistry.BuildDefinitions(),
            };

            AnthropicClient.SendMessage(_apiKey, payload, OnSuccess, OnFail);
        }

        void OnFail(string err)
        {
            _onError?.Invoke(err);
            _onDone?.Invoke();
        }

        void OnSuccess(MessageResponse response)
        {
            if (_stopped) { _onDone?.Invoke(); return; }
            if (response == null) { OnFail("empty response"); return; }

            string textOut = null;
            if (response.content != null)
            {
                foreach (var block in response.content)
                {
                    if (block?.type == "text" && !string.IsNullOrEmpty(block.text))
                    {
                        textOut = textOut == null ? block.text : textOut + "\n\n" + block.text;
                    }
                }
            }

            if (textOut != null) _onAssistantText?.Invoke(textOut);
            _onDone?.Invoke();
        }
    }
}
