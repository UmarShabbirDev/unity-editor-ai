using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace HusnainUnityAI
{
    public class WebFetchTool : ITool
    {
        const int MaxBytes = 80 * 1024;
        const int TimeoutSeconds = 30;

        public string Name => "web_fetch";
        public string Description =>
            "Fetches a URL via HTTP GET and returns the response body as text. HTML is roughly stripped " +
            "to plain text. Returns up to ~80 KB; longer responses are truncated. Use to read docs, " +
            "package READMEs, API references, or any web page Claude needs context from.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""url"": { ""type"": ""string"", ""description"": ""Full URL starting with http(s)://"" }
            },
            ""required"": [""url""]
        }");

        public bool RequiresApproval => false;

        public string PreviewSummary(JObject input) => $"web_fetch {input?["url"]}";

        public void Execute(JObject input, Action<ToolExecutionResult> onResult)
        {
            try
            {
                var url = input?["url"]?.ToString();
                if (string.IsNullOrEmpty(url))
                {
                    onResult(ToolExecutionResult.Err("url is required"));
                    return;
                }
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    onResult(ToolExecutionResult.Err("url must start with http:// or https://"));
                    return;
                }

                var request = UnityWebRequest.Get(url);
                request.timeout = TimeoutSeconds;
                var op = request.SendWebRequest();
                op.completed += _ =>
                {
                    try
                    {
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            var body = request.downloadHandler.text ?? "";
                            var contentType = request.GetResponseHeader("Content-Type") ?? "";
                            if (contentType.Contains("html"))
                            {
                                body = HtmlToText(body);
                            }
                            if (body.Length > MaxBytes)
                            {
                                body = body.Substring(0, MaxBytes) + "\n\n[truncated]";
                            }
                            onResult(ToolExecutionResult.Ok(body));
                        }
                        else
                        {
                            onResult(ToolExecutionResult.Err($"HTTP {request.responseCode}: {request.error}"));
                        }
                    }
                    catch (Exception e)
                    {
                        onResult(ToolExecutionResult.Err(e.Message));
                    }
                    finally
                    {
                        request.Dispose();
                    }
                };
            }
            catch (Exception e)
            {
                onResult(ToolExecutionResult.Err(e.Message));
            }
        }

        static readonly Regex ScriptStyleRx = new Regex(
            @"<(script|style)[^>]*>.*?</\1>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex TagRx = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        static readonly Regex WhitespaceRx = new Regex(@"\s{3,}", RegexOptions.Compiled);

        static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var stripped = ScriptStyleRx.Replace(html, "");
            stripped = TagRx.Replace(stripped, " ");
            stripped = System.Net.WebUtility.HtmlDecode(stripped);
            stripped = WhitespaceRx.Replace(stripped, "\n\n");
            return stripped.Trim();
        }
    }
}
