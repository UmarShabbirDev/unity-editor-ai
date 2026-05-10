using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HusnainUnityAI
{
    [Serializable]
    public class ChatHistorySnapshot
    {
        public int version = 2;
        public string id;
        public string title;
        public string createdAt;
        public string updatedAt;
        public string model;
        public List<HusnainAIWindow.ChatTurn> turns;
    }

    public class ConversationMeta
    {
        public string Id;
        public string Title;
        public DateTime UpdatedAt;
        public DateTime CreatedAt;
    }

    public static class ChatHistory
    {
        const string DirName = "UserSettings/HusnainUnityAI";
        const string ConversationsDirName = "conversations";
        const string LegacyCurrentFile = "current.json";
        const string LegacyArchiveDirName = "archive";

        static bool _migrated;

        public static string Dir => Path.GetFullPath(DirName);
        public static string ConversationsDir => Path.Combine(Dir, ConversationsDirName);

        static string PathFor(string id) => Path.Combine(ConversationsDir, id + ".json");

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 16);

        public static List<ConversationMeta> List()
        {
            MigrateLegacyIfNeeded();
            var result = new List<ConversationMeta>();
            try
            {
                if (!Directory.Exists(ConversationsDir)) return result;
                foreach (var path in Directory.GetFiles(ConversationsDir, "*.json"))
                {
                    try
                    {
                        var snap = JsonConvert.DeserializeObject<ChatHistorySnapshot>(File.ReadAllText(path));
                        if (snap == null) continue;
                        result.Add(new ConversationMeta
                        {
                            Id = string.IsNullOrEmpty(snap.id) ? Path.GetFileNameWithoutExtension(path) : snap.id,
                            Title = string.IsNullOrEmpty(snap.title) ? "(untitled)" : snap.title,
                            UpdatedAt = ParseDate(snap.updatedAt),
                            CreatedAt = ParseDate(snap.createdAt),
                        });
                    }
                    catch { /* skip corrupt files */ }
                }
                result.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HusnainUnityAI] List conversations failed: " + e.Message);
            }
            return result;
        }

        public static ChatHistorySnapshot Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var path = PathFor(id);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<ChatHistorySnapshot>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HusnainUnityAI] Load failed: " + e.Message);
                return null;
            }
        }

        public static void Save(ChatHistorySnapshot snap)
        {
            if (snap == null || string.IsNullOrEmpty(snap.id)) return;
            try
            {
                Directory.CreateDirectory(ConversationsDir);
                if (string.IsNullOrEmpty(snap.createdAt))
                    snap.createdAt = DateTime.UtcNow.ToString("o");
                snap.updatedAt = DateTime.UtcNow.ToString("o");
                File.WriteAllText(PathFor(snap.id),
                    JsonConvert.SerializeObject(snap, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HusnainUnityAI] Save failed: " + e.Message);
            }
        }

        public static void Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                var path = PathFor(id);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HusnainUnityAI] Delete failed: " + e.Message);
            }
        }

        public static string AutoTitle(List<HusnainAIWindow.ChatTurn> turns)
        {
            if (turns == null) return "New conversation";
            foreach (var t in turns)
            {
                if (t.Role == "user" && !string.IsNullOrEmpty(t.Text))
                {
                    var first = t.Text.Replace('\n', ' ').Replace('\r', ' ').Trim();
                    if (first.Length > 40) return first.Substring(0, 40) + "…";
                    return first.Length == 0 ? "New conversation" : first;
                }
            }
            return "New conversation";
        }

        static DateTime ParseDate(string s)
        {
            if (DateTime.TryParse(s, out var d)) return d.ToUniversalTime();
            return DateTime.MinValue;
        }

        static void MigrateLegacyIfNeeded()
        {
            if (_migrated) return;
            _migrated = true;
            try
            {
                Directory.CreateDirectory(ConversationsDir);

                var legacyCurrent = Path.Combine(Dir, LegacyCurrentFile);
                if (File.Exists(legacyCurrent)) MigrateOne(legacyCurrent);

                var legacyArchiveDir = Path.Combine(Dir, LegacyArchiveDirName);
                if (Directory.Exists(legacyArchiveDir))
                {
                    foreach (var path in Directory.GetFiles(legacyArchiveDir, "*.json"))
                    {
                        MigrateOne(path);
                    }
                    try { Directory.Delete(legacyArchiveDir, false); } catch { /* ignore non-empty */ }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HusnainUnityAI] Migration warning: " + e.Message);
            }
        }

        static void MigrateOne(string sourcePath)
        {
            try
            {
                var json = File.ReadAllText(sourcePath);
                var snap = JsonConvert.DeserializeObject<ChatHistorySnapshot>(json);
                if (snap == null || snap.turns == null || snap.turns.Count == 0)
                {
                    File.Delete(sourcePath);
                    return;
                }
                if (string.IsNullOrEmpty(snap.id)) snap.id = NewId();
                if (string.IsNullOrEmpty(snap.title)) snap.title = AutoTitle(snap.turns);
                if (string.IsNullOrEmpty(snap.createdAt)) snap.createdAt = DateTime.UtcNow.ToString("o");
                if (string.IsNullOrEmpty(snap.updatedAt)) snap.updatedAt = snap.createdAt;
                snap.version = 2;
                File.WriteAllText(PathFor(snap.id),
                    JsonConvert.SerializeObject(snap, Formatting.Indented));
                File.Delete(sourcePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HusnainUnityAI] Migrate one failed (" + sourcePath + "): " + e.Message);
            }
        }
    }
}
