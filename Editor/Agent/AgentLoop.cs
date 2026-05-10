using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace HusnainUnityAI
{
    public class AgentLoop
    {
        public delegate bool ApprovalCallback(ITool tool, JObject input);

        readonly string _apiKey;
        readonly string _model;
        readonly string _system;
        readonly int _maxTokens;
        readonly List<OutgoingMessage> _messages;
        readonly Action<ToolCallRecord> _onToolCallStarted;
        readonly Action<ToolResultRecord> _onToolResult;
        readonly Action<string> _onAssistantText;
        readonly Action<string> _onError;
        readonly Action _onDone;
        readonly ApprovalCallback _approval;
        readonly int _maxIterations;

        bool _stopped;
        bool _reloadLocked;
        bool _doneFired;
        int _iter;

        public AgentLoop(
            string apiKey,
            string model,
            string system,
            int maxTokens,
            List<OutgoingMessage> messages,
            Action<ToolCallRecord> onToolCallStarted,
            Action<ToolResultRecord> onToolResult,
            Action<string> onAssistantText,
            Action<string> onError,
            Action onDone,
            ApprovalCallback approval,
            int maxIterations = 25)
        {
            _apiKey = apiKey;
            _model = model;
            _system = system;
            _maxTokens = maxTokens;
            _messages = messages;
            _onToolCallStarted = onToolCallStarted;
            _onToolResult = onToolResult;
            _onAssistantText = onAssistantText;
            _onError = onError;
            _onDone = onDone;
            _approval = approval;
            _maxIterations = maxIterations;
        }

        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            EditorApplication.delayCall += () =>
            {
                _onError?.Invoke("Cancelled by user.");
                FireDoneOnce();
            };
        }

        public void Start()
        {
            LockReloads();
            SendOnce();
        }

        void LockReloads()
        {
            if (_reloadLocked) return;
            try
            {
                EditorApplication.LockReloadAssemblies();
                _reloadLocked = true;
            }
            catch { /* ignore — best-effort */ }
        }

        void UnlockReloads()
        {
            if (!_reloadLocked) return;
            try { EditorApplication.UnlockReloadAssemblies(); }
            catch { /* ignore */ }
            _reloadLocked = false;
        }

        void FireDoneOnce()
        {
            if (_doneFired) return;
            _doneFired = true;
            UnlockReloads();
            _onDone?.Invoke();
        }

        void SendOnce()
        {
            if (_stopped) return;
            if (++_iter > _maxIterations)
            {
                _onError("agent loop exceeded " + _maxIterations + " iterations");
                FireDoneOnce();
                return;
            }

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
            _onError(err);
            FireDoneOnce();
        }

        void OnSuccess(MessageResponse response)
        {
            if (_stopped) { FireDoneOnce(); return; }
            if (response == null)
            {
                OnFail("empty response");
                return;
            }

            // Only keep tool_use blocks when we actually plan to execute them.
            // Otherwise (max_tokens, refusal, etc.) they'd be persisted as orphans
            // — the next API call would 400 because no tool_result follows.
            bool keepToolUse = response.stop_reason == "tool_use";

            var assistantBlocks = new List<OutgoingContentBlock>();
            var toolCalls = new List<(string id, string name, JObject input)>();
            string textOut = null;

            if (response.content != null)
            {
                foreach (var block in response.content)
                {
                    if (block == null) continue;
                    if (block.type == "text" && !string.IsNullOrEmpty(block.text))
                    {
                        textOut = textOut == null ? block.text : textOut + "\n\n" + block.text;
                        assistantBlocks.Add(new OutgoingContentBlock
                        {
                            type = "text",
                            text = block.text,
                        });
                    }
                    else if (block.type == "tool_use" && keepToolUse)
                    {
                        toolCalls.Add((block.id, block.name, block.input ?? new JObject()));
                        assistantBlocks.Add(new OutgoingContentBlock
                        {
                            type = "tool_use",
                            id = block.id,
                            name = block.name,
                            input = block.input ?? new JObject(),
                        });
                    }
                }
            }

            if (textOut != null && _onAssistantText != null)
            {
                _onAssistantText(textOut);
            }

            if (assistantBlocks.Count > 0)
            {
                _messages.Add(new OutgoingMessage { role = "assistant", content = assistantBlocks });
            }

            if (response.stop_reason == "tool_use" && toolCalls.Count > 0)
            {
                ExecuteToolCalls(toolCalls, 0);
                return;
            }

            if (response.stop_reason == "end_turn" || response.stop_reason == "stop_sequence")
            {
                FireDoneOnce();
                return;
            }

            if (response.stop_reason == "max_tokens")
            {
                _onError("Stopped: model hit max_tokens (output cap). " +
                         "Increase Settings → max_tokens, or send 'continue' to resume from where it left off.");
                FireDoneOnce();
                return;
            }

            if (response.stop_reason == "refusal")
            {
                _onError("Stopped: model refused for safety reasons.");
                FireDoneOnce();
                return;
            }

            if (toolCalls.Count > 0)
            {
                _onError($"Stopped: unexpected stop_reason '{response.stop_reason}' with pending tool calls.");
                FireDoneOnce();
                return;
            }

            FireDoneOnce();
        }

        void ExecuteToolCalls(List<(string id, string name, JObject input)> calls, int index)
        {
            if (_stopped) { FireDoneOnce(); return; }
            if (index >= calls.Count)
            {
                SendOnce();
                return;
            }

            var (id, name, input) = calls[index];
            var tool = ToolRegistry.Get(name);

            if (tool == null)
            {
                AppendResult(id, "unknown tool: " + name, true);
                _onToolResult?.Invoke(new ToolResultRecord
                {
                    ToolUseId = id,
                    Content = "unknown tool: " + name,
                    IsError = true,
                });
                ExecuteToolCalls(calls, index + 1);
                return;
            }

            _onToolCallStarted?.Invoke(new ToolCallRecord
            {
                ToolUseId = id,
                Name = name,
                InputJson = input.ToString(Newtonsoft.Json.Formatting.None),
            });

            bool approved = true;
            if (tool.RequiresApproval && _approval != null)
            {
                approved = _approval(tool, input);
            }

            if (!approved)
            {
                var msg = "user rejected this " + name + " call";
                AppendResult(id, msg, true);
                _onToolResult?.Invoke(new ToolResultRecord
                {
                    ToolUseId = id,
                    Content = msg,
                    IsError = true,
                });
                ExecuteToolCalls(calls, index + 1);
                return;
            }

            try
            {
                tool.Execute(input, result =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (_stopped) { FireDoneOnce(); return; }
                        var text = result?.Text ?? "";
                        var isError = result?.IsError ?? false;
                        AppendResult(id, text, isError);
                        _onToolResult?.Invoke(new ToolResultRecord
                        {
                            ToolUseId = id,
                            Content = text,
                            IsError = isError,
                        });
                        ExecuteToolCalls(calls, index + 1);
                    };
                });
            }
            catch (Exception e)
            {
                AppendResult(id, "tool threw: " + e.Message, true);
                _onToolResult?.Invoke(new ToolResultRecord
                {
                    ToolUseId = id,
                    Content = "tool threw: " + e.Message,
                    IsError = true,
                });
                ExecuteToolCalls(calls, index + 1);
            }
        }

        void AppendResult(string toolUseId, string text, bool isError)
        {
            _messages.Add(new OutgoingMessage
            {
                role = "user",
                content = new List<OutgoingContentBlock>
                {
                    new OutgoingContentBlock
                    {
                        type = "tool_result",
                        tool_use_id = toolUseId,
                        content = text,
                        is_error = isError ? (bool?)true : null,
                    },
                },
            });
        }
    }
}
