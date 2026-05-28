using Newtonsoft.Json.Linq;
using System.Text;

namespace ReleasePrepTool.Services;

public class ConfigCompareService
{
    public string CompareJsonFiles(string oldJsonPath, string newJsonPath, out bool hasChanges, out string cleanJsonContent)
    {
        hasChanges = false;
        if (!File.Exists(oldJsonPath) || !File.Exists(newJsonPath))
        {
            cleanJsonContent = File.Exists(newJsonPath) ? File.ReadAllText(newJsonPath) : "";
            return "One of the JSON files is missing.";
        }

        var oldJson = JObject.Parse(File.ReadAllText(oldJsonPath));
        var newJson = JObject.Parse(File.ReadAllText(newJsonPath));
        
        var sb = new StringBuilder();
        CompareJTokens(oldJson, newJson, "", sb, ref hasChanges);

        // Clean up passwords and junk data
        CleanupJsonNode(newJson);
        cleanJsonContent = newJson.ToString();
        
        return sb.ToString();
    }

    private void CompareJTokens(JToken oldNode, JToken newNode, string path, StringBuilder sb, ref bool hasChanges)
    {
        if (oldNode.Type == JTokenType.Object && newNode.Type == JTokenType.Object)
        {
            var oldObj = (JObject)oldNode;
            var newObj = (JObject)newNode;

            var oldProps = oldObj.Properties().Select(p => p.Name).ToList();
            var newProps = newObj.Properties().Select(p => p.Name).ToList();

            var addedProps = newProps.Except(oldProps).ToList();
            var removedProps = oldProps.Except(newProps).ToList();
            var commonProps = oldProps.Intersect(newProps).ToList();

            foreach (var prop in addedProps)
            {
                hasChanges = true;
                sb.AppendLine($"+ Added Property: {path}{prop} = {newObj[prop]}");
            }

            foreach (var prop in removedProps)
            {
                hasChanges = true;
                sb.AppendLine($"- Removed Property: {path}{prop} = {oldObj[prop]}");
            }

            foreach (var prop in commonProps)
            {
                CompareJTokens(oldObj[prop], newObj[prop], path + prop + ".", sb, ref hasChanges);
            }
        }
        else if (oldNode.Type == JTokenType.Array && newNode.Type == JTokenType.Array)
        {
            // Simple array comparison, can be enhanced
            if (oldNode.ToString() != newNode.ToString())
            {
                hasChanges = true;
                sb.AppendLine($"~ Changed Array: {path.TrimEnd('.')} from\n {oldNode} \nto\n {newNode}");
            }
        }
        else
        {
            if (!JToken.DeepEquals(oldNode, newNode))
            {
                hasChanges = true;
                sb.AppendLine($"~ Changed Value: {path.TrimEnd('.')} from '{oldNode}' to '{newNode}'");
            }
        }
    }

    private void CleanupJsonNode(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                var lowerName = property.Name.ToLower();
                if (lowerName.Contains("password") || lowerName.Contains("secret") || lowerName.Contains("token") || lowerName.Contains("key"))
                {
                    if (property.Value.Type == JTokenType.String)
                    {
                        property.Value = ""; // clear out secrets
                    }
                }
                else
                {
                    CleanupJsonNode(property.Value);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
            {
                CleanupJsonNode(item);
            }
        }
    }

    public string CompareEnvFiles(string oldEnvPath, string newEnvPath, out bool hasChanges, out string cleanEnvContent)
    {
        hasChanges = false;
        if (!File.Exists(oldEnvPath) || !File.Exists(newEnvPath))
        {
            cleanEnvContent = File.Exists(newEnvPath) ? File.ReadAllText(newEnvPath) : "";
            return "One of the ENV files is missing.";
        }

        var oldEnv = DotNetEnv.Env.LoadMulti(new[] { oldEnvPath }).ToDictionary();
        var newEnv = DotNetEnv.Env.LoadMulti(new[] { newEnvPath }).ToDictionary();

        var sb = new StringBuilder();

        var addedKeys = newEnv.Keys.Except(oldEnv.Keys).ToList();
        var removedKeys = oldEnv.Keys.Except(newEnv.Keys).ToList();
        var commonKeys = oldEnv.Keys.Intersect(newEnv.Keys).ToList();

        foreach (var key in addedKeys)
        {
            hasChanges = true;
            sb.AppendLine($"+ Added Variable: {key} = {newEnv[key]}");
        }

        foreach (var key in removedKeys)
        {
            hasChanges = true;
            sb.AppendLine($"- Removed Variable: {key} = {oldEnv[key]}");
        }

        foreach (var key in commonKeys)
        {
            if (oldEnv[key] != newEnv[key])
            {
                hasChanges = true;
                sb.AppendLine($"~ Changed Variable: {key} from '{oldEnv[key]}' to '{newEnv[key]}'");
            }
        }

        var cleanSb = new StringBuilder();
        foreach (var kvp in newEnv)
        {
            var lowerKey = kvp.Key.ToLower();
            if (lowerKey.Contains("password") || lowerKey.Contains("secret") || lowerKey.Contains("token") || lowerKey.Contains("key"))
            {
                cleanSb.AppendLine($"{kvp.Key}=");
            }
            else
            {
                cleanSb.AppendLine($"{kvp.Key}={kvp.Value}");
            }
        }

        cleanEnvContent = cleanSb.ToString();

        return sb.ToString();
    }

    public string CompareDirectories(string oldDir, string newDir, out bool hasChanges, out List<(string FileName, string CleanContent)> cleanFiles)
    {
        hasChanges = false;
        cleanFiles = new List<(string, string)>();
        if (!Directory.Exists(oldDir) || !Directory.Exists(newDir))
        {
            return "One of the directories is missing.";
        }

        var oldFiles = Directory.GetFiles(oldDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json") || f.EndsWith(".env"))
            .Select(f => Path.GetRelativePath(oldDir, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newFiles = Directory.GetFiles(newDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json") || f.EndsWith(".env"))
            .Select(f => Path.GetRelativePath(newDir, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var addedFiles = newFiles.Except(oldFiles, StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
        var removedFiles = oldFiles.Except(newFiles, StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
        var commonFiles = oldFiles.Intersect(newFiles, StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();

        var sb = new StringBuilder();

        if (addedFiles.Any())
        {
            sb.AppendLine("=== Added Files in Target ===");
            foreach (var file in addedFiles)
            {
                hasChanges = true;
                sb.AppendLine($"   + {file}");
                string fullNewPath = Path.Combine(newDir, file);
                try
                {
                    string cleanContent = "";
                    if (file.EndsWith(".json"))
                    {
                        var json = JObject.Parse(File.ReadAllText(fullNewPath));
                        CleanupJsonNode(json);
                        cleanContent = json.ToString();
                    }
                    else
                    {
                        var cleanSb = new StringBuilder();
                        foreach (var line in File.ReadLines(fullNewPath))
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            {
                                cleanSb.AppendLine(line);
                                continue;
                            }
                            var idx = trimmed.IndexOf('=');
                            if (idx > 0)
                            {
                                var key = trimmed.Substring(0, idx).Trim();
                                var lowerKey = key.ToLower();
                                if (lowerKey.Contains("password") || lowerKey.Contains("secret") || lowerKey.Contains("token") || lowerKey.Contains("key"))
                                {
                                    cleanSb.AppendLine($"{key}=");
                                }
                                else
                                {
                                    cleanSb.AppendLine(trimmed);
                                }
                            }
                            else
                            {
                                cleanSb.AppendLine(trimmed);
                            }
                        }
                        cleanContent = cleanSb.ToString();
                    }
                    cleanFiles.Add((file, cleanContent));
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  [Error processing added file {file}: {ex.Message}]");
                }
            }
            sb.AppendLine();
        }

        if (removedFiles.Any())
        {
            sb.AppendLine("=== Removed Files in Target ===");
            foreach (var file in removedFiles)
            {
                hasChanges = true;
                sb.AppendLine($"   - {file}");
            }
            sb.AppendLine();
        }

        if (commonFiles.Any())
        {
            foreach (var file in commonFiles)
            {
                string oldFilePath = Path.Combine(oldDir, file);
                string newFilePath = Path.Combine(newDir, file);
                bool fileHasChanges = false;
                string fileCleanContent = "";
                string diff = "";

                try
                {
                    if (file.EndsWith(".json"))
                    {
                        diff = CompareJsonFiles(oldFilePath, newFilePath, out fileHasChanges, out fileCleanContent);
                    }
                    else if (file.EndsWith(".env"))
                    {
                        diff = CompareEnvFiles(oldFilePath, newFilePath, out fileHasChanges, out fileCleanContent);
                    }

                    if (fileHasChanges)
                    {
                        hasChanges = true;
                        sb.AppendLine($"--- {file} ---");
                        var lines = diff.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                sb.AppendLine("   " + line);
                            }
                        }
                        sb.AppendLine();
                        cleanFiles.Add((file, fileCleanContent));
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"--- {file} ---");
                    sb.AppendLine($"[Error comparing file: {ex.Message}]");
                }
            }
        }

        return sb.ToString();
    }
}
