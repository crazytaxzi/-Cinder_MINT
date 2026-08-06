from pathlib import Path
import re

path = Path("src/Cinder.MINT.App/Models/AudioGraphModel.cs")
content = path.read_text(encoding="utf-8")
pattern = re.compile(
    r"    private static bool TryFindEndpointCycle\(.*?\n    public static AudioGraphModel CreateDefault\(\)",
    re.DOTALL,
)
replacement = '''    private static bool TryFindEndpointCycle(
        IReadOnlyDictionary<string, HashSet<string>> edges,
        out List<string> cycle)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        List<string> foundCycle = [];

        bool Visit(string key)
        {
            state[key] = 1;
            path.Add(key);

            if (edges.TryGetValue(key, out HashSet<string>? targets))
            {
                foreach (string target in targets)
                {
                    if (!state.TryGetValue(target, out int targetState))
                    {
                        if (Visit(target)) return true;
                    }
                    else if (targetState == 1)
                    {
                        int start = path.FindIndex(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                        foundCycle = path.Skip(Math.Max(0, start)).Append(target).ToList();
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[key] = 2;
            return false;
        }

        foreach (string key in edges.Keys)
        {
            if (!state.ContainsKey(key) && Visit(key))
            {
                cycle = foundCycle;
                return true;
            }
        }

        cycle = [];
        return false;
    }

    public static AudioGraphModel CreateDefault()'''
updated, count = pattern.subn(replacement, content, count=1)
if count != 1:
    raise SystemExit("Endpoint cycle detector patch did not match exactly once.")
path.write_text(updated, encoding="utf-8", newline="\n")
