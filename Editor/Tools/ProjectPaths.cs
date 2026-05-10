using System;
using System.IO;
using UnityEngine;

namespace HusnainUnityAI
{
    public static class ProjectPaths
    {
        public static string ProjectRoot
        {
            get
            {
                var dataPath = Application.dataPath;
                return Path.GetFullPath(Path.Combine(dataPath, ".."));
            }
        }

        public static bool TryResolve(string requested, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;
            if (string.IsNullOrEmpty(requested))
            {
                error = "path is empty";
                return false;
            }

            string combined;
            try
            {
                combined = Path.IsPathRooted(requested)
                    ? Path.GetFullPath(requested)
                    : Path.GetFullPath(Path.Combine(ProjectRoot, requested));
            }
            catch (Exception e)
            {
                error = "invalid path: " + e.Message;
                return false;
            }

            var root = ProjectRoot;
            if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = $"path '{requested}' is outside the project root ({root})";
                return false;
            }

            fullPath = combined;
            return true;
        }

        public static string Relative(string fullPath)
        {
            try
            {
                var root = ProjectRoot;
                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                }
            }
            catch { }
            return fullPath;
        }
    }
}
