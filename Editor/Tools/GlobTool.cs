using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace HusnainUnityAI
{
    public class GlobTool : ITool
    {
        const int MaxEntries = 500;

        public string Name => "glob";
        public string Description =>
            "Recursive file search by glob pattern. Examples: 'Assets/Scripts/**/*.cs', " +
            "'Assets/**/*.unity', '**/*.prefab'. Returns matching project-relative paths. " +
            ".meta files and hidden directories are excluded.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""pattern"": { ""type"": ""string"", ""description"": ""Glob pattern, project-relative."" }
            },
            ""required"": [""pattern""]
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input) => $"glob {input?["pattern"]}";

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
                pattern = pattern.Replace('\\', '/');

                var root = ProjectPaths.ProjectRoot;
                var regex = GlobToRegex(pattern);

                var matches = new List<string>();
                Walk(root, root, regex, matches);

                if (matches.Count == 0)
                {
                    onResult(ToolExecutionResult.Ok("(no matches)"));
                    return;
                }
                matches.Sort(StringComparer.Ordinal);
                var sb = new StringBuilder();
                int shown = 0;
                foreach (var m in matches)
                {
                    sb.AppendLine(m);
                    if (++shown >= MaxEntries) break;
                }
                if (matches.Count > MaxEntries)
                {
                    sb.AppendLine($"...truncated at {MaxEntries} of {matches.Count} matches");
                }
                onResult(ToolExecutionResult.Ok(sb.ToString().TrimEnd()));
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }

        static void Walk(string root, string dir, Regex regex, List<string> matches)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith(".") || name.EndsWith(".meta")) continue;
                    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (regex.IsMatch(rel)) matches.Add(rel);
                }
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith(".") || name == "Library" || name == "Temp" || name == "obj")
                        continue;
                    Walk(root, sub, regex, matches);
                }
            }
            catch { /* skip unreadable */ }
        }

        static Regex GlobToRegex(string glob)
        {
            var sb = new StringBuilder("^");
            for (int i = 0; i < glob.Length; i++)
            {
                char c = glob[i];
                if (c == '*')
                {
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?') sb.Append("[^/]");
                else if ("[](){}+.^$|\\".IndexOf(c) >= 0) { sb.Append('\\'); sb.Append(c); }
                else sb.Append(c);
            }
            sb.Append("$");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }
    }
}
