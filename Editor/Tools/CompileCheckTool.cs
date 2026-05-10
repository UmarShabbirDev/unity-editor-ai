using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace HusnainUnityAI
{
    public class CompileCheckTool : ITool
    {
        public string Name => "compile_check";
        public string Description =>
            "Forces a Unity script recompilation and reports any compile errors or warnings. " +
            "Use this after writing or editing C# files to verify they compile cleanly. " +
            "Returns 'OK' if there are no errors, otherwise lists each error with file:line.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {},
            ""required"": []
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input) => "compile_check";

        readonly List<string> _errors = new List<string>();
        readonly List<string> _warnings = new List<string>();
        Action<ToolExecutionResult> _pending;
        double _startedAt;
        const double TimeoutSeconds = 60.0;

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            if (_pending != null)
            {
                onResult(ToolExecutionResult.Err("compile_check is already running"));
                return;
            }

            _pending = onResult;
            _errors.Clear();
            _warnings.Clear();
            _startedAt = EditorApplication.timeSinceStartup;

            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            CompilationPipeline.compilationFinished += OnAllFinished;
            EditorApplication.update += WatchTimeout;

            try
            {
                AssetDatabase.Refresh();
                CompilationPipeline.RequestScriptCompilation();
            }
            catch (Exception e)
            {
                CompleteWith(ToolExecutionResult.Err("failed to request compilation: " + e.Message));
            }
        }

        void WatchTimeout()
        {
            if (_pending == null) return;
            var elapsed = EditorApplication.timeSinceStartup - _startedAt;
            if (elapsed < TimeoutSeconds) return;

            // Force-complete after timeout regardless of EditorApplication.isCompiling.
            // When LockReloadAssemblies is in effect, isCompiling can stay true indefinitely
            // because compilation is queued behind the lock. Don't block on it.
            var hint = _errors.Count > 0
                ? _errors.Count + " errors observed before timeout. Fix those first.\n"
                  + string.Join("\n", _errors)
                : "Unity is likely blocked on existing compile errors that prevent the recompile from finishing. " +
                  "Do NOT call compile_check again immediately — instead inspect recent edits with read_file " +
                  "and look for missing usings, typos, or undefined names. Only retry compile_check after fixing them.";
            CompleteWith(ToolExecutionResult.Ok(
                $"compile_check timed out after {(int)elapsed}s. " + hint));
        }

        void OnAssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var m in messages)
            {
                var entry = $"{m.file}({m.line},{m.column}): {m.message}";
                if (m.type == CompilerMessageType.Error) _errors.Add(entry);
                else if (m.type == CompilerMessageType.Warning) _warnings.Add(entry);
            }
        }

        void OnAllFinished(object _)
        {
            var sb = new StringBuilder();
            if (_errors.Count == 0)
            {
                sb.AppendLine("OK — no compile errors.");
                if (_warnings.Count > 0)
                {
                    sb.AppendLine($"({_warnings.Count} warnings)");
                    foreach (var w in _warnings) sb.AppendLine("WARN  " + w);
                }
            }
            else
            {
                sb.AppendLine($"{_errors.Count} compile error(s):");
                foreach (var e in _errors) sb.AppendLine("ERROR " + e);
                if (_warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"{_warnings.Count} warnings:");
                    foreach (var w in _warnings) sb.AppendLine("WARN  " + w);
                }
            }

            CompleteWith(_errors.Count == 0
                ? ToolExecutionResult.Ok(sb.ToString().TrimEnd())
                : new ToolExecutionResult { Text = sb.ToString().TrimEnd(), IsError = false });
        }

        void CompleteWith(ToolExecutionResult result)
        {
            var pending = _pending;
            Cleanup();
            pending?.Invoke(result);
        }

        void Cleanup()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyFinished;
            CompilationPipeline.compilationFinished -= OnAllFinished;
            EditorApplication.update -= WatchTimeout;
            _pending = null;
        }
    }
}
