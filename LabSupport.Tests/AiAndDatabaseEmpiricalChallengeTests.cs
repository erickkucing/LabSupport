using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Shared.Models;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class AiAndDatabaseEmpiricalChallengeTests : IDisposable
{
    private readonly string _testDbPath;

    public AiAndDatabaseEmpiricalChallengeTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"empirical_test_db_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    #region Mock HttpMessageHandler Helper
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

        public HttpRequestMessage? LastRequest { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        public MockHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
        {
            _handlerFunc = (req, ct) =>
            {
                var resp = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await _handlerFunc(request, cancellationToken);
        }
    }
    #endregion

    #region 1. ExtractJsonFromResponse Empirical Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void ExtractJson_EmptyOrWhitespace_ReturnsEmptyString(string? input)
    {
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractJson_PureJson_ReturnsCleanJson()
    {
        string json = "{\"name\":\"Clean PC\",\"steps\":[]}";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(json);
        Assert.Equal(json, result);
    }

    [Fact]
    public void ExtractJson_StandardMarkdownFence_ExtractsInnerJson()
    {
        string input = "```json\n{\n  \"name\": \"Clean Temp\",\n  \"steps\": []\n}\n```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.Contains("\"name\": \"Clean Temp\"", result);
    }

    [Fact]
    public void ExtractJson_MarkdownFenceWithoutLanguageTag_ExtractsInnerJson()
    {
        string input = "```\n{\n  \"name\": \"No Tag Recipe\",\n  \"steps\": []\n}\n```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.Contains("\"name\": \"No Tag Recipe\"", result);
    }

    [Fact]
    public void ExtractJson_MarkdownFenceWithUppercaseTag_ExtractsInnerJson()
    {
        string input = "```JSON\n{\n  \"name\": \"Uppercase JSON\",\n  \"steps\": []\n}\n```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.Contains("\"name\": \"Uppercase JSON\"", result);
    }

    [Fact]
    public void ExtractJson_PreambleAndConversationalWrapper_ExtractsInnerJson()
    {
        string input = "Halo Bapak/Ibu Guru! Berikut adalah resep otomasi yang Anda minta:\n\n```json\n{\n  \"name\": \"Resep Ujian\",\n  \"steps\": []\n}\n```\n\nSemoga resep ini bermanfaat dan membantu kelancaran ujian di lab!";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.DoesNotContain("Halo", result);
        Assert.DoesNotContain("bermanfaat", result);
        Assert.Contains("\"name\": \"Resep Ujian\"", result);
    }

    [Fact]
    public void ExtractJson_ConversationalTextWithoutMarkdownFences_ExtractsOuterJson()
    {
        string input = "Tentu, berikut resepnya: {\"name\": \"Resep Langsung\", \"steps\": []} Silakan digunakan!";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.Equal("{\"name\": \"Resep Langsung\", \"steps\": []}", result);
    }

    [Fact]
    public void ExtractJson_NestedBracesAndBracesInsideStrings_PreservesFullStructure()
    {
        string input = @"```json
{
  ""name"": ""Recipe {Special} 2026"",
  ""steps"": [
    {
      ""actionType"": ""ClearTempCache"",
      ""title"": ""Step {1} Temp""
    }
  ]
}
```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.Contains("Recipe {Special} 2026", result);
        Assert.Contains("Step {1} Temp", result);

        var recipe = ScriptRecipe.FromJson(result);
        Assert.NotNull(recipe);
        Assert.Equal("Recipe {Special} 2026", recipe!.Name);
        Assert.Single(recipe.Steps);
    }

    [Fact]
    public void ExtractJson_NoBracesInResponse_ReturnsTrimmedRawContent()
    {
        string input = "Maaf, saya tidak dapat memahami permintaan Anda.";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.Equal("Maaf, saya tidak dapat memahami permintaan Anda.", result);
    }

    [Fact]
    public void ExtractJson_SingleOrReversedBraces_ReturnsRawContent()
    {
        string single = "{ unclosed brace";
        string singleResult = AiScriptGeneratorService.ExtractJsonFromResponse(single);
        Assert.Equal(single, singleResult);

        string reversed = "} reversed {";
        string reversedResult = AiScriptGeneratorService.ExtractJsonFromResponse(reversed);
        Assert.Equal(reversed, reversedResult);
    }

    [Fact]
    public void ExtractJson_MultipleCodeFences_ExtractsFirstMatchingFence()
    {
        string input = "Schema:\n```json\n{\"id\": 1}\n```\nActual:\n```json\n{\"id\": 2}\n```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.Equal("{\"id\": 1}", result);
    }

    #endregion

    #region 2. AiScriptGeneratorService TestConnectionAsync Empirical Tests

    [Fact]
    public async Task TestConnection_Http200Ok_ReturnsSuccessTrue()
    {
        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.TestConnectionAsync("https://api.9router.com/v1", "test-key-123", "openai/gpt-4o-mini");

        Assert.True(result.Success);
        Assert.Contains("berhasil terverifikasi", result.Message);
        Assert.NotNull(mock.LastRequest);
        Assert.Equal("Bearer", mock.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("test-key-123", mock.LastRequest.Headers.Authorization?.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TestConnection_Http401Or403_ReturnsAuthenticationError(HttpStatusCode statusCode)
    {
        var mock = new MockHttpMessageHandler(statusCode, "{\"error\":\"invalid_api_key\"}");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.TestConnectionAsync("https://api.9router.com/v1", "wrong-key", "openai/gpt-4o-mini");

        Assert.False(result.Success);
        Assert.Contains("API Key tidak valid atau otentikasi gagal", result.Message);
    }

    [Fact]
    public async Task TestConnection_Http404NotFound_ReturnsEndpointNotFoundError()
    {
        var mock = new MockHttpMessageHandler(HttpStatusCode.NotFound, "Not Found");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.TestConnectionAsync("https://api.9router.com/invalid/endpoint", "key", "model");

        Assert.False(result.Success);
        Assert.Contains("Endpoint AI Router tidak ditemukan", result.Message);
    }

    [Fact]
    public async Task TestConnection_Http500ServerError_ReturnsHttpErrorDetail()
    {
        var mock = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "Service Unavailable Internally");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.TestConnectionAsync("https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Contains("500", result.Message);
    }

    [Fact]
    public async Task TestConnection_EmptyBaseUrlAndModel_UsesDefaults()
    {
        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"choices\":[]}");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.TestConnectionAsync("", "", "");

        Assert.True(result.Success);
        Assert.NotNull(mock.LastRequest);
        Assert.Equal("https://api.9router.com/v1/chat/completions", mock.LastRequest!.RequestUri?.ToString());
    }

    [Fact]
    public async Task TestConnection_InvalidUri_CatchesExceptionAndReturnsFalseGracefully()
    {
        // Malformed URI that throws UriFormatException
        using var service = new AiScriptGeneratorService();

        var result = await service.TestConnectionAsync("http:// invalid url with spaces", "key", "model");

        Assert.False(result.Success);
        Assert.Contains("Kesalahan koneksi", result.Message);
    }

    #endregion

    #region 3. AiScriptGeneratorService GenerateRecipeAsync Empirical Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateRecipe_EmptyUserPrompt_ReturnsFalseWithoutCallingNetwork(string? prompt)
    {
        bool networkCalled = false;
        var mock = new MockHttpMessageHandler((req, ct) =>
        {
            networkCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync(prompt!, "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("Prompt instruksi pengguna tidak boleh kosong", result.Error);
        Assert.False(networkCalled, "Network should not be invoked when prompt is empty");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GenerateRecipe_HttpErrors_ReturnsFalseWithAppropriateError(HttpStatusCode statusCode)
    {
        var mock = new MockHttpMessageHandler(statusCode, "{\"error\": \"AI gateway error\"}");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Buat script bersihkan temp", "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains(((int)statusCode).ToString(), result.Error);
    }

    [Fact]
    public async Task GenerateRecipe_SuccessResponseWithMarkdownFence_ParsesRecipeProperly()
    {
        string aiReplyJson = @"
{
  ""name"": ""Pembersih Lab Lengkap"",
  ""description"": ""Membersihkan cache dan flush dns"",
  ""steps"": [
    {
      ""actionType"": ""ClearTempCache"",
      ""title"": ""Hapus File Temp"",
      ""isEnabled"": true
    },
    {
      ""actionType"": ""FlushDnsNetwork"",
      ""title"": ""Flush DNS"",
      ""isEnabled"": true
    }
  ]
}";
        string openaiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = $"```json\n{aiReplyJson}\n```"
                    }
                }
            }
        });

        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, openaiResponse);
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Bersihkan lab", "https://api.9router.com/v1", "key", "model");

        Assert.True(result.Success, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Recipe);
        Assert.Equal("Pembersih Lab Lengkap", result.Recipe!.Name);
        Assert.Equal(2, result.Recipe.Steps.Count);
        Assert.Equal(ScriptActionType.ClearTempCache, result.Recipe.Steps[0].ActionType);
        Assert.Equal(ScriptActionType.FlushDnsNetwork, result.Recipe.Steps[1].ActionType);
        Assert.False(string.IsNullOrWhiteSpace(result.Recipe.Id));
    }

    [Fact]
    public async Task GenerateRecipe_NonJsonResponseBody_ReturnsFalseGracefully()
    {
        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, "<html><body>502 Bad Gateway from Cloudflare</body></html>");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Bersihkan lab", "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("Terjadi kesalahan saat memproses resep AI", result.Error);
    }

    [Fact]
    public async Task GenerateRecipe_MissingChoicesOrEmptyContent_ReturnsFalse()
    {
        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"choices\": []}");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Bersihkan lab", "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("Respons AI tidak memuat konten resep", result.Error);
    }

    [Fact]
    public async Task GenerateRecipe_ValidJsonWithoutSteps_ReturnsFalse()
    {
        string openaiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "{\"name\": \"Resep Tanpa Step\", \"steps\": []}"
                    }
                }
            }
        });

        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, openaiResponse);
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Resep kosong", "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("tidak memiliki langkah aksi", result.Error);
    }

    [Fact]
    public async Task GenerateRecipe_CancelledToken_ReturnsCancelledError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Prompt", "https://api.9router.com/v1", "key", "model", cts.Token);

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("dibatalkan", result.Error);
    }

    #endregion

    #region 4. DatabaseService AI Settings Empirical Tests

    [Fact]
    public async Task DatabaseService_GetAiRouterSettings_FreshDb_ReturnsDefaults()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

        Assert.Equal("https://api.9router.com/v1", baseUrl);
        Assert.Equal(string.Empty, apiKey);
        Assert.Equal("openai/gpt-4o-mini", model);
    }

    [Fact]
    public async Task DatabaseService_SaveAndGetAiRouterSettings_PersistsAccurately()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        string customUrl = "https://custom-ai-router.internal.org/v1";
        string customKey = "sk-custom-live-token-9988";
        string customModel = "anthropic/claude-3-5-sonnet";

        await db.SaveAiRouterSettingsAsync(customUrl, customKey, customModel);

        var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

        Assert.Equal(customUrl, baseUrl);
        Assert.Equal(customKey, apiKey);
        Assert.Equal(customModel, model);
    }

    [Fact]
    public async Task DatabaseService_SaveAiRouterSettings_WithNullOrWhitespace_FallsBackToDefaults()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        await db.SaveAiRouterSettingsAsync("", null!, "  ");

        var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

        Assert.Equal("https://api.9router.com/v1", baseUrl);
        Assert.Equal(string.Empty, apiKey);
        Assert.Equal("openai/gpt-4o-mini", model);
    }

    [Fact]
    public async Task DatabaseService_SaveAiRouterSettings_TrimsSurroundingWhitespace()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        await db.SaveAiRouterSettingsAsync("  https://my-router.com/v1  ", "  my-secret-key  ", "  gpt-4o-custom  ");

        var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

        Assert.Equal("https://my-router.com/v1", baseUrl);
        Assert.Equal("my-secret-key", apiKey);
        Assert.Equal("gpt-4o-custom", model);
    }

    [Fact]
    public async Task DatabaseService_DualKeySupport_ReadsLegacyPascalCaseIfSnakeCaseAbsent()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        // Simulate legacy DB where only PascalCase keys were written
        await db.SetSettingAsync("AiRouterBaseUrl", "https://legacy-router.com/v1");
        await db.SetSettingAsync("AiRouterApiKey", "legacy-key-123");
        await db.SetSettingAsync("AiRouterModel", "legacy-model-v1");

        var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

        Assert.Equal("https://legacy-router.com/v1", baseUrl);
        Assert.Equal("legacy-key-123", apiKey);
        Assert.Equal("legacy-model-v1", model);
    }

    [Fact]
    public async Task DatabaseService_PersistenceAcrossInstances_RetainsDataOnDisk()
    {
        // 1. Write using first instance
        using (var db1 = new DatabaseService(_testDbPath))
        {
            await db1.InitializeAsync();
            await db1.SaveAiRouterSettingsAsync("https://persisted-router.com/v1", "persisted-key-456", "deepseek/deepseek-chat");
        }

        // 2. Read using second instance pointing to the same file
        using (var db2 = new DatabaseService(_testDbPath))
        {
            await db2.InitializeAsync();
            var (baseUrl, apiKey, model) = await db2.GetAiRouterSettingsAsync();

            Assert.Equal("https://persisted-router.com/v1", baseUrl);
            Assert.Equal("persisted-key-456", apiKey);
            Assert.Equal("deepseek/deepseek-chat", model);
        }
    }

    [Fact]
    public async Task DatabaseService_RepeatedOverwrites_UpdateInPlaceWithoutConflict()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        for (int i = 1; i <= 5; i++)
        {
            await db.SaveAiRouterSettingsAsync($"https://router-{i}.com/v1", $"key-{i}", $"model-{i}");
            var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

            Assert.Equal($"https://router-{i}.com/v1", baseUrl);
            Assert.Equal($"key-{i}", apiKey);
            Assert.Equal($"model-{i}", model);
        }
    }

    [Fact]
    public async Task DatabaseService_SpecialCharactersAndSqlInjection_SafelyPersistsWithoutInjection()
    {
        using var db = new DatabaseService(_testDbPath);
        await db.InitializeAsync();

        string maliciousUrl = "https://example.com/v1?token=' OR '1'='1; DROP TABLE settings; --";
        string unicodeKey = "sk-🔑-test-🚀-123456-'\"`~";
        string complexModel = "meta/llama-3.1-70b-instruct:free; SELECT * FROM computers;";

        await db.SaveAiRouterSettingsAsync(maliciousUrl, unicodeKey, complexModel);

        var (baseUrl, apiKey, model) = await db.GetAiRouterSettingsAsync();

        Assert.Equal(maliciousUrl, baseUrl);
        Assert.Equal(unicodeKey, apiKey);
        Assert.Equal(complexModel, model);

        // Verify settings table is still intact and queryable
        string val = await db.GetSettingAsync("ai_router_base_url");
        Assert.Equal(maliciousUrl, val);
    }

    [Fact]
    public async Task DatabaseService_Uninitialized_ThrowsSqliteException()
    {
        using var uninitDb = new DatabaseService(_testDbPath);
        // Do NOT call InitializeAsync
        await Assert.ThrowsAnyAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
        {
            await uninitDb.GetAiRouterSettingsAsync();
        });
    }

    #endregion

    #region 5. Deeper Adversarial Edge Cases

    [Fact]
    public void ExtractJson_ConversationalPreambleWithCurlyBraces_WithoutFences_ExposesOuterDelimitationLimitation()
    {
        // When AI writes curly braces in conversational preamble without markdown fences,
        // ExtractJsonFromResponse grabs from the first '{' in preamble to last '}' in JSON.
        string input = "Periksa langkah {1} ini terlebih dahulu: {\"name\": \"Resep Bersih\", \"steps\": []} Selesai.";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);

        // Result will start at '{1}' because it's the outermost '{'
        Assert.StartsWith("{1}", result);
        Assert.EndsWith("}", result);

        // Downstream FromJson throws JsonException on this malformed text:
        Assert.ThrowsAny<JsonException>(() => ScriptRecipe.FromJson(result));
    }

    [Fact]
    public void ExtractJson_FenceWithCustomSuffixTag_FallsBackToBraceMatching()
    {
        string input = "```json:recipe\n{\"name\": \"Custom Tag Recipe\", \"steps\": []}\n```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);

        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
        Assert.Contains("Custom Tag Recipe", result);
    }

    [Fact]
    public void ExtractJson_JsonWithStringContainingSingleBackticks_ParsesCleanly()
    {
        string input = "```json\n{\n  \"name\": \"Script `Get-Process`\",\n  \"steps\": []\n}\n```";
        string result = AiScriptGeneratorService.ExtractJsonFromResponse(input);

        Assert.Contains("Script `Get-Process`", result);
        var recipe = ScriptRecipe.FromJson(result);
        Assert.NotNull(recipe);
        Assert.Equal("Script `Get-Process`", recipe!.Name);
    }

    [Fact]
    public async Task GenerateRecipe_AiReturnsUnknownActionType_FailsGracefullyWithoutCrashing()
    {
        string openaiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "```json\n{\"name\": \"Bad Enum\", \"steps\": [{\"actionType\": \"InventedNonExistentAction\"}]}\n```"
                    }
                }
            }
        });

        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, openaiResponse);
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Prompt", "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("Terjadi kesalahan saat memproses resep AI", result.Error);
    }

    [Fact]
    public async Task GenerateRecipe_AiReturnsTrailingComma_FailsGracefullyWithoutCrashing()
    {
        string jsonWithTrailingComma = "{\n  \"name\": \"Trailing Comma\",\n  \"steps\": [\n    {\n      \"actionType\": \"ClearTempCache\",\n    },\n  ],\n}";
        string openaiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = $"```json\n{jsonWithTrailingComma}\n```"
                    }
                }
            }
        });

        var mock = new MockHttpMessageHandler(HttpStatusCode.OK, openaiResponse);
        using var client = new HttpClient(mock);
        using var service = new AiScriptGeneratorService(client);

        var result = await service.GenerateRecipeAsync("Prompt", "https://api.9router.com/v1", "key", "model");

        Assert.False(result.Success);
        Assert.Null(result.Recipe);
        Assert.Contains("Terjadi kesalahan saat memproses resep AI", result.Error);
    }

    #endregion
}
