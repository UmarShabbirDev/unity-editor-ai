using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class GrepTool : ITool
    {
        const int MaxMatches = 200;
        const long MaxFileBytes = 1024 * 1024;

        public string Name => "grep";
        public string Description =>
            "Searches file contents using a regular expression. Returns lines that match, " +
            "in 'path:line: text' form. Pass an optional path to limit search scope " +
            "(directory or single file). Skips Library/, Temp/, .meta files, and binaries.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""pattern"":    { ""type"": ""string"", ""description"": ""Regex pattern to search for."" },
                ""path"":       { ""type"": ""string"", ""description"": ""Optional. Project-relative directory or file. Defaults to project root."" },
                ""ignore_case"": { ""type"": ""boolean"", ""description"": ""Optional. Default false."" }
            },
            ""required"": [""pattern""]
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input) =>
            $"grep '{Truncate(input?["pattern"]?.ToString(), 40)}' in {input?["path"] ?? "."}";

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var pattern = input?["pattern"]?.ToString();
                if (string.IsNullOrEmpty(pattern))
                {
                    onResult(ToolExecutionResult.Err("pattern is empty"));
                    return;
                }
                var rawPath = input?["path"]?.ToString();
                if (string.IsNullOrEmpty(rawPath) || rawPath == ".") rawPath = "";
                bool ignoreCase = input?["ignore_case"]?.Value<bool>() ?? false;

                if (!ProjectPaths.TryResolve(rawPath, out var full, out var error))
                {
                    onResult(ToolExecutionResult.Err(error));
                    return;
                }

                var options = RegexOptions.Compiled;
                if (ignoreCase) options |= RegexOptions.IgnoreCase;
                Regex regex;
                try { regex = new Regex(pattern, options); }
                catch (Exception e) { onResult(ToolExecutionResult.Err("invalid regex: " + e.Message)); return; }

                var lines = new List<string>();
                int count = 0;

                if (File.Exists(full))
                {
                    SearchFile(full, regex, lines, ref count);
                }
                else if (Directory.Exists(full))
                {
                    SearchDir(full, regex, lines, ref count);
                }
                else
                {
                    onResult(ToolExecutionResult.Err("path not found: " + rawPath));
                    return;
                }

                if (lines.Count == 0)
                {
                    onResult(ToolExecutionResult.Ok("(no matches)"));
                    return;
                }
                var sb = new StringBuilder();
                foreach (var l in lines) sb.AppendLine(l);
                if (count >= MaxMatches) sb.AppendLine($"...truncated at {MaxMatches} matches");
                onResult(ToolExecutionResult.Ok(sb.ToString().TrimEnd()));
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }

        static readonly string[] SkipDirs = { "Library", "Temp", "obj", "Logs", "UserSettings/HusnainUnityAI" };
        static readonly string[] BinaryExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".dll",
            ".so", ".dylib", ".exe", ".pdb", ".mdb", ".asset", ".unitypackage", ".fbx", ".obj",
            ".mp3", ".wav", ".ogg", ".mp4", ".mov", ".tga", ".psd", ".tif", ".tiff", ".ttf", ".otf" };

        static void SearchDir(string dir, Regex regex, List<string> lines, ref int count)
        {
            if (count >= MaxMatches) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (count >= MaxMatches) break;
                    var name = Path.GetFileName(f);
                    if (name.StartsWith(".") || name.EndsWith(".meta")) continue;
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    bool skip = false;
                    foreach (var b in BinaryExts) if (b == ext) { skip = true; break; }
                    if (skip) continue;
                    SearchFile(f, regex, lines, ref count);
                }
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (count >= MaxMatches) break;
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith(".")) continue;
                    bool skip = false;
                    foreach (var s in SkipDirs) if (sub.EndsWith(s) || name == s) { skip = true; break; }
                    if (skip) continue;
                    SearchDir(sub, regex, lines, ref count);
                }
            }
            catch { /* skip unreadable */ }
        }

        static void SearchFile(string file, Regex regex, List<string> lines, ref int count)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length > MaxFileBytes) return;
                var rel = ProjectPaths.Relative(file);
                int lineNum = 0;
                using (var sr = new StreamReader(file))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lineNum++;
                        if (regex.IsMatch(line))
                        {
                            lines.Add($"{rel}:{lineNum}: {Truncate(line, 200)}");
                            if (++count >= MaxMatches) return;
                        }
                    }
                }
            }
            catch { /* skip unreadable */ }
        }

        static string Truncate(string s, int n)
        {
            if (s == null) return "";
            return s.Length > n ? s.Substring(0, n) + "…" : s;
        }
    }
}
