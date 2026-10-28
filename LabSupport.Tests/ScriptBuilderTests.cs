using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Models;
using LabSupport.Shared.Services;
using Xunit;

namespace LabSupport.Tests;

public class ScriptBuilderTests
{
    [Theory]
    [InlineData(ScriptActionType.ClearTempCache)]
    [InlineData(ScriptActionType.FlushDnsNetwork)]
    [InlineData(ScriptActionType.LaunchExecutable)]
    [InlineData(ScriptActionType.InstallWingetSoftware)]
    [InlineData(ScriptActionType.KillProcess)]
    [InlineData(ScriptActionType.SetUsbStorageLock)]
    [InlineData(ScriptActionType.OpenUrlBrowser)]
    [InlineData(ScriptActionType.SetSystemVolume)]
    [InlineData(ScriptActionType.SetWallpaper)]
    [InlineData(ScriptActionType.ShutdownRestart)]
    [InlineData(ScriptActionType.LogoffUser)]
    [InlineData(ScriptActionType.CustomPowerShellBlock)]
    public void All12ScriptActionTypes_ProduceNonEmptyPowerShellSnippets(ScriptActionType actionType)
    {
        var step = new ScriptActionStep(actionType, $"Test {actionType}")
        {
            ExecutablePath = @"C:\Windows\notepad.exe",
            Arguments = "test.txt",
            SoftwarePackageId = "Git.Git",
            ProcessName = "chrome",
            TargetUrl = "https://lab.local",
            WallpaperPath = @"C:\wallpapers\lab.jpg",
            CustomScript = "Write-Output 'Custom action'"
        };

        string snippet = step.ToPowerShellSnippet();

        Assert.False(string.IsNullOrWhiteSpace(snippet));
        Assert.Contains("# [Langkah", snippet);
    }

    [Fact]
    public void LaunchExecutable_InteractiveElevated_GeneratesHighestRunLevelTaskAndCleanup()
    {
        var step = new ScriptActionStep(ScriptActionType.LaunchExecutable, "Launch App")
        {
            ExecutablePath = @"C:\Program Files\TestApp\app.exe",
            Arguments = "--flag-test 'value'",
            LaunchMode = ExecutableLaunchMode.InteractiveElevated
        };

        string snippet = step.ToPowerShellSnippet();

        Assert.Contains("schtasks.exe /create", snippet);
        Assert.Contains("/rl HIGHEST", snippet);
        Assert.Contains("/f", snippet);
        Assert.Contains("/it", snippet);
        Assert.Contains("schtasks.exe /run", snippet);
        Assert.Contains("schtasks.exe /delete", snippet);
        Assert.Contains("$taskName", snippet);
        Assert.Contains("C:\\Program Files\\TestApp\\app.exe", snippet);
    }

    [Fact]
    public void LaunchExecutable_BackgroundSilent_GeneratesHiddenWindowStartProcess()
    {
        var step = new ScriptActionStep(ScriptActionType.LaunchExecutable, "Background App")
        {
            ExecutablePath = @"C:\Tools\updater.exe",
            Arguments = "/silent /all",
            LaunchMode = ExecutableLaunchMode.BackgroundSilent
        };

        string snippet = step.ToPowerShellSnippet();

        Assert.Contains("Start-Process", snippet);
        Assert.Contains("-WindowStyle Hidden", snippet);
        Assert.Contains("-PassThru", snippet);
        Assert.Contains("updater.exe", snippet);
        Assert.DoesNotContain("schtasks.exe", snippet);
    }

    [Fact]
    public void ScriptRecipe_JsonRoundtrip_PreservesAllPropertiesAndSteps()
    {
        var recipe = new ScriptRecipe
        {
            Id = "recipe-rt-test-123",
            Name = "Roundtrip Test Recipe",
            Description = "Ensures full fidelity serialization",
            Author = "Tester Agent",
            CreatedAt = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc),
            Version = "2.1",
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.ClearTempCache, "Step 1"),
                new(ScriptActionType.LaunchExecutable, "Step 2")
                {
                    ExecutablePath = @"C:\app.exe",
                    Arguments = "-arg1",
                    LaunchMode = ExecutableLaunchMode.InteractiveElevated
                },
                new(ScriptActionType.SetUsbStorageLock, "Step 3")
                {
                    UsbLocked = true
                },
                new(ScriptActionType.SetSystemVolume, "Step 4")
                {
                    VolumePercent = 75
                },
                new(ScriptActionType.InstallWingetSoftware, "Step 5")
                {
                    SoftwarePackageId = "Test.Package"
                },
                new(ScriptActionType.CustomPowerShellBlock, "Step 6")
                {
                    CustomScript = "Get-Service | Where-Object Status -eq 'Running'"
                }
            }
        };

        string json = recipe.ToJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var deserialized = ScriptRecipe.FromJson(json);
        Assert.NotNull(deserialized);
        Assert.Equal(recipe.Id, deserialized.Id);
        Assert.Equal(recipe.Name, deserialized.Name);
        Assert.Equal(recipe.Description, deserialized.Description);
        Assert.Equal(recipe.Author, deserialized.Author);
        Assert.Equal(recipe.Version, deserialized.Version);
        Assert.Equal(recipe.Steps.Count, deserialized.Steps.Count);

        for (int i = 0; i < recipe.Steps.Count; i++)
        {
            var expected = recipe.Steps[i];
            var actual = deserialized.Steps[i];

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ActionType, actual.ActionType);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.IsEnabled, actual.IsEnabled);

            if (expected.ActionType == ScriptActionType.LaunchExecutable)
            {
                Assert.Equal(expected.ExecutablePath, actual.ExecutablePath);
                Assert.Equal(expected.Arguments, actual.Arguments);
                Assert.Equal(expected.LaunchMode, actual.LaunchMode);
            }
            else if (expected.ActionType == ScriptActionType.SetUsbStorageLock)
            {
                Assert.Equal(expected.UsbLocked, actual.UsbLocked);
            }
            else if (expected.ActionType == ScriptActionType.SetSystemVolume)
            {
                Assert.Equal(expected.VolumePercent, actual.VolumePercent);
            }
            else if (expected.ActionType == ScriptActionType.InstallWingetSoftware)
            {
                Assert.Equal(expected.SoftwarePackageId, actual.SoftwarePackageId);
            }
            else if (expected.ActionType == ScriptActionType.CustomPowerShellBlock)
            {
                Assert.Equal(expected.CustomScript, actual.CustomScript);
            }
        }
    }

    [Fact]
    public void ScriptRecipe_FromJson_HandlesNullOrWhitespaceGracefully()
    {
        Assert.Null(ScriptRecipe.FromJson(null!));
        Assert.Null(ScriptRecipe.FromJson(""));
        Assert.Null(ScriptRecipe.FromJson("   "));
        Assert.Null(ScriptRecipe.FromJson("\t\r\n"));
    }

    [Fact]
    public void ScriptPresetService_All6Presets_ReturnValidNonEmptyRecipes()
    {
        var presets = ScriptPresetService.GetDefaultPresets();

        Assert.Equal(6, presets.Count);

        var deepClean = ScriptPresetService.GetDeepCleanPreset();
        var examPrep = ScriptPresetService.GetPersiapanUjianPreset();
        var postExam = ScriptPresetService.GetNormalisasiPascaUjianPreset();
        var launchApp = ScriptPresetService.GetLuncurkanAplikasiLabPreset();
        var installSoftware = ScriptPresetService.GetPasangSoftwareStandarPreset();
        var scheduledShutdown = ScriptPresetService.GetMatikanLabTerjadwalPreset();

        var presetList = new[] { deepClean, examPrep, postExam, launchApp, installSoftware, scheduledShutdown };

        foreach (var p in presetList)
        {
            Assert.NotNull(p);
            Assert.False(string.IsNullOrWhiteSpace(p.Id));
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.NotEmpty(p.Steps);

            foreach (var step in p.Steps)
            {
                Assert.False(string.IsNullOrWhiteSpace(step.Title));
                string snippet = step.ToPowerShellSnippet();
                Assert.False(string.IsNullOrWhiteSpace(snippet));
            }

            // Verify PowerShell script generation succeeds
            string script = p.GeneratePowerShellScript();
            Assert.Contains(p.Name, script);

            // Verify batch wrapper generation succeeds
            string batch = p.GenerateBatchWrapper();
            Assert.Contains("net session", batch);
            Assert.Contains(":::___LABSUPPORT_POWERSHELL_PAYLOAD___", batch);
        }
    }

    [Fact]
    public void ScriptPresetService_GetPresetById_ReturnsCorrectPresetOrNull()
    {
        var found = ScriptPresetService.GetPresetById("preset-deep-clean");
        Assert.NotNull(found);
        Assert.Equal("Deep Clean", found.Name);

        var foundExam = ScriptPresetService.GetPresetById("PRESET-EXAM-PREP"); // case-insensitive
        Assert.NotNull(foundExam);
        Assert.Equal("Persiapan Ujian", foundExam.Name);

        Assert.Null(ScriptPresetService.GetPresetById("non-existent-id"));
        Assert.Null(ScriptPresetService.GetPresetById(""));
        Assert.Null(ScriptPresetService.GetPresetById(null!));
    }

    [Fact]
    public void GenerateBatchWrapper_ContainsNetSessionElevationAndEmbeddedMarker()
    {
        var recipe = ScriptPresetService.GetDeepCleanPreset();
        string batch = recipe.GenerateBatchWrapper();

        Assert.Contains("@echo off", batch);
        Assert.Contains("net session >nul 2>&1", batch);
        Assert.Contains("Start-Process -FilePath '%~f0' -ArgumentList '%*' -WorkingDirectory '%~dp0' -Verb RunAs", batch);
        Assert.Contains("powershell.exe -NoProfile -ExecutionPolicy Bypass", batch);
        Assert.Contains(":::___LABSUPPORT_POWERSHELL_PAYLOAD___", batch);

        int markerIndex = batch.IndexOf(":::___LABSUPPORT_POWERSHELL_PAYLOAD___", StringComparison.Ordinal);
        Assert.True(markerIndex > 0);
        string payload = batch.Substring(markerIndex + ":::___LABSUPPORT_POWERSHELL_PAYLOAD___".Length);
        Assert.Contains("Deep Clean", payload);
        Assert.Contains("Membersihkan temporary files", payload);
    }

    [Fact]
    public void SetUsbStorageLock_TogglesRegistryValuesCorrectly()
    {
        var stepLock = new ScriptActionStep(ScriptActionType.SetUsbStorageLock) { UsbLocked = true };
        var snippetLock = stepLock.ToPowerShellSnippet();
        Assert.Contains("-Value 4", snippetLock);

        var stepUnlock = new ScriptActionStep(ScriptActionType.SetUsbStorageLock) { UsbLocked = false };
        var snippetUnlock = stepUnlock.ToPowerShellSnippet();
        Assert.Contains("-Value 3", snippetUnlock);
    }

    [Fact]
    public void SetSystemVolume_ClampsVolumeBetween0And100()
    {
        var stepNegative = new ScriptActionStep(ScriptActionType.SetSystemVolume) { VolumePercent = -20 };
        Assert.Contains("$volPercent = 0", stepNegative.ToPowerShellSnippet());

        var stepExcessive = new ScriptActionStep(ScriptActionType.SetSystemVolume) { VolumePercent = 150 };
        Assert.Contains("$volPercent = 100", stepExcessive.ToPowerShellSnippet());

        var stepNormal = new ScriptActionStep(ScriptActionType.SetSystemVolume) { VolumePercent = 65 };
        Assert.Contains("$volPercent = 65", stepNormal.ToPowerShellSnippet());
    }

    [Fact]
    public void ShutdownRestart_HandlesDelayClampingAndRestartFlag()
    {
        var stepShutdown = new ScriptActionStep(ScriptActionType.ShutdownRestart)
        {
            IsRestart = false,
            DelaySeconds = -10
        };
        string shutSnippet = stepShutdown.ToPowerShellSnippet();
        Assert.Contains("shutdown.exe /s /t 0", shutSnippet);

        var stepRestart = new ScriptActionStep(ScriptActionType.ShutdownRestart)
        {
            IsRestart = true,
            DelaySeconds = 30
        };
        string restartSnippet = stepRestart.ToPowerShellSnippet();
        Assert.Contains("shutdown.exe /r /t 30", restartSnippet);
    }

    [Fact]
    public void SingleQuoteEscaping_PreventsInjectionInPowerShellSnippets()
    {
        var step = new ScriptActionStep(ScriptActionType.LaunchExecutable)
        {
            ExecutablePath = @"C:\Program's Path\app.exe",
            Arguments = "--user 'John Doe'",
            LaunchMode = ExecutableLaunchMode.BackgroundSilent
        };

        string snippet = step.ToPowerShellSnippet();

        Assert.Contains(@"$exePath = 'C:\Program''s Path\app.exe'", snippet);
        Assert.Contains(@"$exeArgs = '--user ''John Doe'''", snippet);
    }

    [Fact]
    public void GeneratePowerShellScript_IgnoresDisabledSteps()
    {
        var recipe = new ScriptRecipe
        {
            Name = "Disabled Steps Test",
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.ClearTempCache, "Active Step 1") { IsEnabled = true },
                new(ScriptActionType.FlushDnsNetwork, "Disabled Step 2") { IsEnabled = false },
                new(ScriptActionType.LogoffUser, "Active Step 3") { IsEnabled = true }
            }
        };

        string ps = recipe.GeneratePowerShellScript();

        Assert.Contains("Active Step 1", ps);
        Assert.DoesNotContain("Disabled Step 2", ps);
        Assert.Contains("Active Step 3", ps);
        Assert.Contains("[1/2] Active Step 1", ps);
        Assert.Contains("[2/2] Active Step 3", ps);
    }

    [Theory]
    [InlineData("```json\n{\"name\": \"AI Recipe\", \"steps\": []}\n```", "{\"name\": \"AI Recipe\", \"steps\": []}")]
    [InlineData("```\n{\"name\": \"AI Recipe 2\", \"steps\": []}\n```", "{\"name\": \"AI Recipe 2\", \"steps\": []}")]
    [InlineData("Here is your recipe:\n{\"name\": \"AI Recipe 3\", \"steps\": []}\nEnjoy!", "{\"name\": \"AI Recipe 3\", \"steps\": []}")]
    [InlineData("{\"name\": \"Pure JSON\", \"steps\": []}", "{\"name\": \"Pure JSON\", \"steps\": []}")]
    public void ExtractJsonFromResponse_ParsesVariedAiOutputs(string input, string expected)
    {
        string extracted = AiScriptGeneratorService.ExtractJsonFromResponse(input);
        Assert.Equal(expected, extracted);
    }

    [Fact]
    public async Task AiScriptGeneratorService_MockHttp_GeneratesValidRecipe()
    {
        var responseJson = @"{
          ""choices"": [
            {
              ""message"": {
                ""content"": ""```json\n{\n  \""name\"": \""Resep AI Ujian\"",\n  \""description\"": \""Resep otomatis\"",\n  \""steps\"": [\n    {\n      \""actionType\"": \""SetUsbStorageLock\"",\n      \""title\"": \""Kunci USB\"",\n      \""usbLocked\"": true\n    }\n  ]\n}\n```""
              }
            }
          ]
        }";

        var mockHandler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        });

        using var client = new HttpClient(mockHandler);
        using var service = new AiScriptGeneratorService(client);

        var (success, recipe, error) = await service.GenerateRecipeAsync(
            "Buat resep ujian",
            "https://mock.api/v1",
            "mock-key",
            "mock-model");

        Assert.True(success);
        Assert.Empty(error);
        Assert.NotNull(recipe);
        Assert.Equal("Resep AI Ujian", recipe.Name);
        Assert.Single(recipe.Steps);
        Assert.Equal(ScriptActionType.SetUsbStorageLock, recipe.Steps[0].ActionType);
        Assert.True(recipe.Steps[0].UsbLocked);
    }

    [Fact]
    public void All6Presets_JsonRoundtrip_PreservesAllData()
    {
        var presets = ScriptPresetService.GetDefaultPresets();
        foreach (var preset in presets)
        {
            string json = preset.ToJson();
            var restored = ScriptRecipe.FromJson(json);

            Assert.NotNull(restored);
            Assert.Equal(preset.Id, restored.Id);
            Assert.Equal(preset.Name, restored.Name);
            Assert.Equal(preset.Description, restored.Description);
            Assert.Equal(preset.Steps.Count, restored.Steps.Count);

            for (int i = 0; i < preset.Steps.Count; i++)
            {
                Assert.Equal(preset.Steps[i].ActionType, restored.Steps[i].ActionType);
                Assert.Equal(preset.Steps[i].Title, restored.Steps[i].Title);
                Assert.Equal(preset.Steps[i].Description, restored.Steps[i].Description);
                Assert.Equal(preset.Steps[i].IsEnabled, restored.Steps[i].IsEnabled);
            }
        }
    }

    [Fact]
    public void ScriptActionStep_ParametersDictionary_SyncsWithTypedProperties()
    {
        var step = new ScriptActionStep();

        step.SoftwarePackageId = "PackageA";
        Assert.Equal("PackageA", step.WingetPackageId);
        Assert.Equal("PackageA", step.Parameters["SoftwarePackageId"]);
        Assert.Equal("PackageA", step.Parameters["WingetPackageId"]);

        step.TargetUrl = "https://example.com";
        Assert.Equal("https://example.com", step.Url);
        Assert.Equal("https://example.com", step.Parameters["TargetUrl"]);
        Assert.Equal("https://example.com", step.Parameters["Url"]);

        step.VolumePercent = 80;
        Assert.Equal(80, step.VolumeLevel);
        Assert.Equal("80", step.Parameters["VolumePercent"]);
        Assert.Equal("80", step.Parameters["VolumeLevel"]);

        step.IsRestart = true;
        Assert.Equal("Restart", step.ShutdownAction);
        Assert.Equal("true", step.Parameters["IsRestart"]);
        Assert.Equal("Restart", step.Parameters["ShutdownAction"]);

        step.DelaySeconds = 45;
        Assert.Equal(45, step.TimeoutSeconds);
        Assert.Equal("45", step.Parameters["DelaySeconds"]);
        Assert.Equal("45", step.Parameters["TimeoutSeconds"]);
    }

    [Fact]
    public void ScriptRecipe_WithUnicodeAndSpecialCharacters_SerializesCleanly()
    {
        var recipe = new ScriptRecipe
        {
            Name = "Resep Khusus: 100% Bersih & Aman <Lab 1>",
            Description = "Menangani path dengan spasi & karakter unik: C:\\Users\\Guru's PC\\Data",
            Steps = new List<ScriptActionStep>
            {
                new(ScriptActionType.LaunchExecutable)
                {
                    ExecutablePath = "C:\\Program Files\\Guru's Tools\\tool.exe",
                    Arguments = "--name \"Siswa #1\" --flag 'test'"
                }
            }
        };

        string json = recipe.ToJson();
        var restored = ScriptRecipe.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(recipe.Name, restored.Name);
        Assert.Equal(recipe.Description, restored.Description);
        Assert.Equal(recipe.Steps[0].ExecutablePath, restored.Steps[0].ExecutablePath);
        Assert.Equal(recipe.Steps[0].Arguments, restored.Steps[0].Arguments);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
