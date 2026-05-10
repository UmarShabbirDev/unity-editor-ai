using System.Collections.Generic;

namespace HusnainUnityAI
{
    public static class ToolRegistry
    {
        static readonly Dictionary<string, ITool> _tools = BuildDefault();

        static Dictionary<string, ITool> BuildDefault()
        {
            var dict = new Dictionary<string, ITool>();
            void Add(ITool t) => dict[t.Name] = t;
            Add(new ReadFileTool());
            Add(new ListFilesTool());
            Add(new GlobTool());
            Add(new GrepTool());
            Add(new WriteFileTool());
            Add(new EditFileTool());
            return dict;
        }

        public static ITool Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _tools.TryGetValue(name, out var t);
            return t;
        }

        public static IEnumerable<ITool> All => _tools.Values;

        public static List<ToolDefinition> BuildDefinitions()
        {
            var list = new List<ToolDefinition>(_tools.Count);
            foreach (var t in _tools.Values)
            {
                list.Add(new ToolDefinition
                {
                    name = t.Name,
                    description = t.Description,
                    input_schema = t.InputSchema,
                });
            }
            return list;
        }
    }
}
