using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Models;

namespace LabSupport.Shared.Services;

public class AiScriptGeneratorService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    public const string DefaultBaseUrl = "https://api.9router.com/v1";
    public const string DefaultModel = "openai/gpt-4o-mini";

    public const string SystemPrompt = @"You are the LabSupport Script Assistant. You generate automated PC lab maintenance recipes for non-technical teachers and lab assistants.
You MUST output ONLY valid JSON adhering strictly to the ScriptRecipe schema below. Do NOT include explanatory text, conversational introductions, or markdown commentary outside the JSON block.

JSON Schema:
{
  ""name"": ""Recipe Title"",
  ""description"": ""Brief description of what the recipe does"",
  ""steps"": [
    {
      ""actionType"": ""<ActionTypeEnum>"",
      ""title"": ""Step Title"",
      ""description"": ""Description of the step"",
      ""isEnabled"": true,
      ""executablePath"": """",
      ""arguments"": """",
      ""launchMode"": ""InteractiveElevated"" or ""BackgroundSilent"",
      ""softwarePackageId"": """",
      ""processName"": """",
      ""targetUrl"": """",
      ""usbLocked"": true,
      ""volumePercent"": 50,
      ""wallpaperPath"": """",
      ""isRestart"": false,
      ""delaySeconds"": 0,
      ""customScript"": """"
    }
  ]
}

Valid ActionTypeEnum values:
- ClearTempCache
- FlushDnsNetwork
- LaunchExecutable
- InstallWingetSoftware
- KillProcess
- SetUsbStorageLock
- OpenUrlBrowser
- SetSystemVolume
- SetWallpaper
- ShutdownRestart
- LogoffUser
- CustomPowerShellBlock";

    public AiScriptGeneratorService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _disposeClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _disposeClient = true;
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(string baseUrl, string apiKey, string model)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            string endpoint = $"{url.TrimEnd('/')}/chat/completions";
            string modelToUse = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

            var requestBody = new
            {
                model = modelToUse,
                messages = new[]
                {
                    new { role = "user", content = "respond with OK" }
                },
                max_tokens = 5
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            request.Headers.Add("User-Agent", "LabSupport-Manager");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return (true, "Koneksi ke AI Router berhasil terverifikasi.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return (false, "API Key tidak valid atau otentikasi gagal (HTTP 401/403).");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (false, "Endpoint AI Router tidak ditemukan. Pastikan Base URL benar (HTTP 404).");
            }

            string errContent = await response.Content.ReadAsStringAsync();
            return (false, $"Gagal terhubung ke AI Router: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {errContent}".Trim());
        }
        catch (OperationCanceledException)
        {
            return (false, "Koneksi ke AI Router timeout (waktu habis setelah 15 detik).");
        }
        catch (Exception ex)
        {
            return (false, $"Kesalahan koneksi AI Router: {ex.Message}");
        }
    }

    public async Task<(bool Success, ScriptRecipe? Recipe, string Error)> GenerateRecipeAsync(
        string userPrompt,
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return (false, null, "Prompt instruksi pengguna tidak boleh kosong.");
        }

        try
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            string endpoint = $"{url.TrimEnd('/')}/chat/completions";
            string modelToUse = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

            var requestBody = new
            {
                model = modelToUse,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = userPrompt.Trim() }
                },
                temperature = 0.2
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            request.Headers.Add("User-Agent", "LabSupport-Manager");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"Permintaan AI gagal (HTTP {(int)response.StatusCode}): {responseBody}");
            }

            string rawContent = string.Empty;
            using (var doc = JsonDocument.Parse(responseBody))
            {
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElem))
                {
                    rawContent = contentElem.GetString() ?? string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                return (false, null, "Respons AI tidak memuat konten resep.");
            }

            string extractedJson = ExtractJsonFromResponse(rawContent);
            if (string.IsNullOrWhiteSpace(extractedJson))
            {
                return (false, null, "Gagal mengekstrak struktur JSON dari respons AI.");
            }

            var recipe = ScriptRecipe.FromJson(extractedJson);
            if (recipe == null)
            {
                return (false, null, "Gagal mendeserialisasi JSON resep dari respons AI.");
            }

            if (recipe.Steps == null || recipe.Steps.Count == 0)
            {
                return (false, null, "Resep yang dihasilkan AI tidak memiliki langkah aksi.");
            }

            if (string.IsNullOrWhiteSpace(recipe.Id))
            {
                recipe.Id = Guid.NewGuid().ToString("N");
            }
            if (string.IsNullOrWhiteSpace(recipe.Name))
            {
                recipe.Name = "Resep AI Kustom";
            }
            if (string.IsNullOrWhiteSpace(recipe.Author))
            {
                recipe.Author = "LabSupport AI Assistant";
            }

            foreach (var step in recipe.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Id))
                {
                    step.Id = Guid.NewGuid().ToString("N");
                }
                if (string.IsNullOrWhiteSpace(step.Title))
                {
                    step.Title = step.ActionType.ToString();
                }
            }

            return (true, recipe, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, null, "Operasi pembuatan resep AI dibatalkan.");
        }
        catch (Exception ex)
        {
            return (false, null, $"Terjadi kesalahan saat memproses resep AI: {ex.Message}");
        }
    }

    public static string ExtractJsonFromResponse(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
            return string.Empty;

        string text = responseContent.Trim();

        // 1. Check for markdown code fences ```json ... ``` or ``` ... ```
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            text = fenceMatch.Groups[1].Value.Trim();
        }

        // 2. Find outermost JSON object delimiters { ... }
        int firstBrace = text.IndexOf('{');
        int lastBrace = text.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }

        return text;
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }
}
