using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace ReleasePrepTool.Services;

public class AIOperationService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    // We assume OpenAI API compatibility for simplicity, can be adjusted for Gemini
    public AIOperationService(string apiKey, string model = "gpt-4o")
    {
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/") };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
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

    private async Task<string> CreateChatCompletionAsync(string prompt)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.2
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("chat/completions", content);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync();
            dynamic aiResponse = JsonConvert.DeserializeObject(responseString);
            return aiResponse.choices[0].message.content.ToString();
        }

        var error = await response.Content.ReadAsStringAsync();
        return $"Error communicating with AI API: {response.StatusCode} - {error}";
    }
}
