using System;
using System.Text;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace HusnainUnityAI
{
    public static class AnthropicClient
    {
        const string ApiUrl = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2023-06-01";

        public static UnityWebRequestAsyncOperation SendMessage(
            string apiKey,
            MessageRequest payload,
            Action<MessageResponse> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                onError("API key is not set.");
                return null;
            }

            string body;
            try
            {
                body = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                });
            }
            catch (Exception e)
            {
                onError("Failed to serialize request: " + e.Message);
                return null;
            }

            var request = new UnityWebRequest(ApiUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("x-api-key", apiKey);
            request.SetRequestHeader("anthropic-version", ApiVersion);
            request.SetRequestHeader("content-type", "application/json");
            request.SetRequestHeader("accept", "application/json");
            request.timeout = 120;

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var json = request.downloadHandler.text;
                        try
                        {
                            var parsed = JsonConvert.DeserializeObject<MessageResponse>(json);
                            onSuccess(parsed);
                        }
                        catch (Exception e)
                        {
                            onError("Failed to parse response: " + e.Message);
                        }
                    }
                    else
                    {
                        var raw = request.downloadHandler != null ? request.downloadHandler.text : null;
                        var msg = $"HTTP {request.responseCode}: {request.error}";
                        if (!string.IsNullOrEmpty(raw))
                        {
                            try
                            {
                                var parsed = JsonConvert.DeserializeObject<AnthropicErrorResponse>(raw);
                                if (parsed?.error != null)
                                {
                                    msg = $"{parsed.error.type}: {parsed.error.message}";
                                }
                            }
                            catch { /* keep raw msg */ }
                        }
                        onError(msg);
                    }
                }
                finally
                {
                    request.Dispose();
                }
            };
            return op;
        }
    }
}
