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
}
