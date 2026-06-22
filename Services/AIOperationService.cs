using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace ReleasePrepTool.Services;

public class AIOperationService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _provider;

    // Configured to support Gemini, OpenAI, Claude, and GitHub Copilot
    public AIOperationService(string apiKey, string model = "gemini-2.0-flash", string provider = "Gemini")
    {
        _apiKey = apiKey;
        _model = model;
        _provider = provider;
        _httpClient = new HttpClient();
        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
        else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
        else if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.BaseAddress = new Uri("https://api.anthropic.com/");
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        else if (provider.Equals("Github Copilot", StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.BaseAddress = new Uri("https://api.githubcopilot.com/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("Editor-Version", "vscode/1.90.0");
            _httpClient.DefaultRequestHeaders.Add("Editor-Plugin-Version", "copilot/1.90.0");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GithubCopilot/1.90.0");
        }
        else if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            _httpClient.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/ngocbui97/release-version");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Release Prep Tool");
        }
    }

    public static async Task<string> GetModelStatusAsync(string provider, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "No Key";
        try
        {
            using var client = new HttpClient();
            string url;
            object requestBody;

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://generativelanguage.googleapis.com/v1beta/openai/v1/chat/completions";
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 2
                };
            }
            else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://api.openai.com/v1/chat/completions";
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 2
                };
            }
            else if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://api.anthropic.com/v1/messages";
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                requestBody = new
                {
                    model = model,
                    max_tokens = 5,
                    messages = new[] { new { role = "user", content = "ping" } }
                };
            }
            else if (provider.Equals("Github Copilot", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://api.githubcopilot.com/chat/completions";
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.Add("Editor-Version", "vscode/1.90.0");
                client.DefaultRequestHeaders.Add("Editor-Plugin-Version", "copilot/1.90.0");
                client.DefaultRequestHeaders.Add("User-Agent", "GithubCopilot/1.90.0");
                requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 2
                };
            }
            else if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://openrouter.ai/api/v1/chat/completions";
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/ngocbui97/release-version");
                client.DefaultRequestHeaders.Add("X-Title", "Release Prep Tool");
                requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 2
                };
            }
            else
            {
                return "Unknown Provider";
            }

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                return "Active";
            }
            
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                var errorObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(responseText);
                var errMsg = errorObj?["error"]?["message"]?.ToString() ?? errorObj?["error"]?["status"]?.ToString();
                if (!string.IsNullOrEmpty(errMsg))
                {
                    if (errMsg.Contains("quota") || errMsg.Contains("exhausted") || errMsg.Contains("ResourceExhausted") || errMsg.Contains("rate_limit_exceeded")) return "Quota Exhausted";
                    if (errMsg.Contains("API key") || errMsg.Contains("invalid") || errMsg.Contains("unauthorized")) return "Invalid Key";
                    if (errMsg.Contains("model") && errMsg.Contains("not found")) return "Model Not Found";
                    return errMsg.Length > 22 ? errMsg.Substring(0, 19) + "..." : errMsg;
                }
            }
            catch {}

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return "Invalid Key";
            if (response.StatusCode == (System.Net.HttpStatusCode)429)
                return "Quota Exhausted";
            
            return $"HTTP {(int)response.StatusCode}";
        }
        catch
        {
            return "Connection Error";
        }
    }

    public async Task<string> ReviewSqlScriptAsync(string sqlScript, string context)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return "AI API Key is not configured.";

        var prompt = $@"
You are a senior database administrator reviewing a PostgreSQL release script.
Context: {context}

Review the following SQL script for:
1. Potential data loss (e.g., dropping tables/columns that might be a mistake).
2. Missing default values for new NOT NULL columns.
3. Syntax and logic issues.
4. Any garbage data insertion.

Script:
```sql
{sqlScript}
```

Provide a concise, bulleted review focusing only on potential issues or risks. If the script looks safe, say 'The script appears strictly safe.'.";

        return await CreateChatCompletionAsync(prompt);
    }
    
    public async Task<string> ReviewConfigChangesAsync(string configDiff)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return "AI API Key is not configured.";

         var prompt = $@"
You are a DevOps engineer reviewing configuration changes between application versions.

Review the following changes:
```
{configDiff}
```

Identify any potential issues such as:
1. Hardcoded passwords or tokens.
2. Inconsistent environment variables.
3. Potentially dangerous configuration flags turned on (like Debug mode in production).

Provide a concise, bulleted review.";

        return await CreateChatCompletionAsync(prompt);
    }

    public async Task<string> ReviewDataChangesAsync(string sqlScript, string context)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return "AI API Key is not configured.";

        var prompt = $@"
You are a senior database administrator reviewing a PostgreSQL data synchronization script.
Context: {context}

Review the following data sync SQL script for:
1. Potential bulk deletion or updates that might be accidental.
2. Inconsistent values or formatting.
3. Violations of data integrity or suspicious values.
4. Security/Privacy issues (e.g. inserting unhashed passwords).

Script:
```sql
{sqlScript}
```

Provide a concise, bulleted review focusing only on potential issues or risks. If the script looks safe, say 'The script appears strictly safe.'.";

        return await CreateChatCompletionAsync(prompt);
    }

    private async Task<string> CreateChatCompletionAsync(string prompt)
    {
        string requestUrl;
        object requestBody;

        if (_provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
        {
            requestUrl = "v1/messages";
            requestBody = new
            {
                model = _model,
                max_tokens = 1024,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            };
        }
        else
        {
            requestUrl = _provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ? "v1/chat/completions" : "chat/completions";
            requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            };
        }

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(requestUrl, content);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync();
            var aiResponse = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(responseString);
            
            if (_provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            {
                var contentToken = aiResponse?["content"]?[0]?["text"];
                if (contentToken != null) return contentToken.ToString();
            }
            else
            {
                var contentToken = aiResponse?["choices"]?[0]?["message"]?["content"];
                if (contentToken != null) return contentToken.ToString();
            }
            return "AI API returned an unexpected response structure.";
        }

        var error = await response.Content.ReadAsStringAsync();
        return $"Error communicating with AI API: {response.StatusCode} - {error}";
    }
}
