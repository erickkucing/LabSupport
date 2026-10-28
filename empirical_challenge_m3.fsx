// Empirical Challenge Suite for Milestone 3 (Manager UI & ViewModels)
#r "/root/project/LabSupport/LabSupport.Shared/bin/Release/net8.0/LabSupport.Shared.dll"
open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Reflection
open System.Collections.ObjectModel
open System.Threading.Tasks
open System.Threading

// Global counters
let mutable passedCount = 0
let mutable failedCount = 0

let assertEq (testName: string) (expected: obj) (actual: obj) =
    if Object.Equals(expected, actual) then
        printfn "  [PASS] %s" testName
        passedCount <- passedCount + 1
    else
        printfn "  [FAIL] %s - Expected: %A, Got: %A" testName expected actual
        failedCount <- failedCount + 1

let assertTrue (testName: string) (condition: bool) =
    if condition then
        printfn "  [PASS] %s" testName
        passedCount <- passedCount + 1
    else
        printfn "  [FAIL] %s - Condition was false" testName
        failedCount <- failedCount + 1

// Assembly resolver for dependencies
AppDomain.CurrentDomain.add_AssemblyResolve(fun sender args ->
    let name = AssemblyName(args.Name).Name
    let searchDirs = [|
        "/root/project/LabSupport/LabSupport.Shared/bin/Release/net8.0"
        "/root/project/LabSupport/LabSupport.Agent/bin/Debug/net8.0"
        "/root/project/LabSupport/LabSupport.Manager/bin/Release/net8.0-windows"
        "/root/project/LabSupport/stub_wpf/bin/Release/net8.0"
    |]
    let mutable resolved : Assembly = null
    for dir in searchDirs do
        let p = Path.Combine(dir, name + ".dll")
        if File.Exists(p) && resolved = null then
            resolved <- Assembly.LoadFrom(p)
    resolved
)

// Mock HTTP Handlers
type MockHttpMessageHandler(handlerFunc: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) =
        Task.FromResult(handlerFunc request)

type MockThrowingHttpMessageHandler(ex: Exception) =
    inherit HttpMessageHandler()
    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) =
        Task.FromException<HttpResponseMessage>(ex)

// Load assemblies
let sharedAsm = Assembly.LoadFrom("/root/project/LabSupport/LabSupport.Shared/bin/Release/net8.0/LabSupport.Shared.dll")
let mgrAsm = Assembly.LoadFrom("/root/project/LabSupport/LabSupport.Manager/bin/Release/net8.0-windows/LabSupport.Manager.dll")

// Types
let dbType = sharedAsm.GetType("LabSupport.Shared.Services.DatabaseService")
let cmdType = sharedAsm.GetType("LabSupport.Shared.Services.AgentCommandService")
let aiGenType = sharedAsm.GetType("LabSupport.Shared.Services.AiScriptGeneratorService")
let labCompType = mgrAsm.GetType("LabSupport.Manager.Models.LabComputer")
let compListType = typedefof<ObservableCollection<_>>.MakeGenericType([| labCompType |])
let scriptVmType = mgrAsm.GetType("LabSupport.Manager.ViewModels.ScriptViewModel")
let settingsVmType = mgrAsm.GetType("LabSupport.Manager.ViewModels.SettingsViewModel")
let scriptRecipeType = sharedAsm.GetType("LabSupport.Shared.Models.ScriptRecipe")
let scriptActionStepType = sharedAsm.GetType("LabSupport.Shared.Models.ScriptActionStep")
let scriptActionTypeEnum = sharedAsm.GetType("LabSupport.Shared.Models.ScriptActionType")

printfn "================================================================="
printfn "  MILENSTONE 3 EMPIRICAL CHALLENGE SUITE: ViewModels & UX Logic  "
printfn "================================================================="

// Helper to create test DB
let createTestDb () =
    let path = Path.Combine(Path.GetTempPath(), sprintf "empirical_m3_%s.db" (Guid.NewGuid().ToString("N")))
    let db = Activator.CreateInstance(dbType, [| box path |])
    let initTask = dbType.GetMethod("InitializeAsync").Invoke(db, [||]) :?> Task
    initTask.Wait()
    (path, db)

// Helper properties
let getProp (obj: obj) (t: Type) (name: string) =
    t.GetProperty(name).GetValue(obj)

let setProp (obj: obj) (t: Type) (name: string) (value: obj) =
    t.GetProperty(name).SetValue(obj, value)


// -------------------------------------------------------------
// SECTION 1: SettingsViewModel Empirical Tests
// -------------------------------------------------------------
printfn "\n--- Section 1: SettingsViewModel AI Router Configuration & Commands ---"

let (dbPath1, db1) = createTestDb()
let setVm1 = Activator.CreateInstance(settingsVmType, [| db1; null |])

// 1.1 Default values
assertEq "SettingsViewModel Default AiRouterBaseUrl" "https://api.9router.com/v1" (getProp setVm1 settingsVmType "AiRouterBaseUrl")
assertEq "SettingsViewModel Default AiRouterApiKey" "" (getProp setVm1 settingsVmType "AiRouterApiKey")
assertEq "SettingsViewModel Default AiRouterModel" "openai/gpt-4o-mini" (getProp setVm1 settingsVmType "AiRouterModel")
assertEq "SettingsViewModel Default IsTestingAiConnection" false (getProp setVm1 settingsVmType "IsTestingAiConnection")

// 1.2 Update and Save to SQLite
setProp setVm1 settingsVmType "AiRouterBaseUrl" "https://custom.router.local/v1"
setProp setVm1 settingsVmType "AiRouterApiKey" "sk-test-secret-key-12345"
setProp setVm1 settingsVmType "AiRouterModel" "custom/deepseek-r1"

let saveTask = settingsVmType.GetMethod("SaveSettingsAsync").Invoke(setVm1, [||]) :?> Task
saveTask.Wait()

assertEq "SettingsViewModel Save StatusMessage" "Pengaturan berhasil disimpan." (getProp setVm1 settingsVmType "StatusMessage")

// Verify directly from SQLite DatabaseService
let getSettingsTask = dbType.GetMethod("GetAiRouterSettingsAsync").Invoke(db1, [||]) :?> Task
getSettingsTask.Wait()
let resultProp = getSettingsTask.GetType().GetProperty("Result").GetValue(getSettingsTask)
let tupleFields = resultProp.GetType().GetFields()
let savedUrl = tupleFields.[0].GetValue(resultProp) :?> string
let savedKey = tupleFields.[1].GetValue(resultProp) :?> string
let savedModel = tupleFields.[2].GetValue(resultProp) :?> string

assertEq "SQLite DatabaseService Saved BaseUrl" "https://custom.router.local/v1" savedUrl
assertEq "SQLite DatabaseService Saved ApiKey" "sk-test-secret-key-12345" savedKey
assertEq "SQLite DatabaseService Saved Model" "custom/deepseek-r1" savedModel

// 1.3 Load into fresh SettingsViewModel
let setVm2 = Activator.CreateInstance(settingsVmType, [| db1; null |])
let loadTask = settingsVmType.GetMethod("LoadSettingsAsync").Invoke(setVm2, [||]) :?> Task
loadTask.Wait()

assertEq "Fresh SettingsViewModel Loaded AiRouterBaseUrl" "https://custom.router.local/v1" (getProp setVm2 settingsVmType "AiRouterBaseUrl")
assertEq "Fresh SettingsViewModel Loaded AiRouterApiKey" "sk-test-secret-key-12345" (getProp setVm2 settingsVmType "AiRouterApiKey")
assertEq "Fresh SettingsViewModel Loaded AiRouterModel" "custom/deepseek-r1" (getProp setVm2 settingsVmType "AiRouterModel")

// 1.4 TestAiConnectionCommand with Mock Service
// Success mock: returns HTTP 200 with OK
let successHandler = new MockHttpMessageHandler(fun req ->
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent("""{"choices":[{"message":{"content":"OK"}}]}""", Encoding.UTF8, "application/json")
    resp
)
let mockSuccessService = new LabSupport.Shared.Services.AiScriptGeneratorService(new HttpClient(successHandler))
let setVmSuccess = Activator.CreateInstance(settingsVmType, [| db1; mockSuccessService |])
let testCmdSuccess = getProp setVmSuccess settingsVmType "TestAiConnectionCommand" :?> System.Windows.Input.ICommand

assertTrue "TestAiConnectionCommand CanExecute initially" (testCmdSuccess.CanExecute(null))
let testTask1 = settingsVmType.GetMethod("TestAiConnectionAsync").Invoke(setVmSuccess, [||]) :?> Task
testTask1.Wait()

let statusSuccess = getProp setVmSuccess settingsVmType "AiConnectionStatus" :?> string
assertTrue "TestAiConnection Success Status contains ✅" (statusSuccess.StartsWith("✅"))
assertTrue "TestAiConnection Success Status contains message" (statusSuccess.Contains("berhasil"))
assertEq "TestAiConnection IsTestingAiConnection reset to false" false (getProp setVmSuccess settingsVmType "IsTestingAiConnection")

// Failure mock: returns HTTP 401 Unauthorized
let failHandler = new MockHttpMessageHandler(fun req ->
    let resp = new HttpResponseMessage(HttpStatusCode.Unauthorized)
    resp.Content <- new StringContent("""{"error":{"message":"Invalid API key"}}""", Encoding.UTF8, "application/json")
    resp
)
let mockFailService = new LabSupport.Shared.Services.AiScriptGeneratorService(new HttpClient(failHandler))
let setVmFail = Activator.CreateInstance(settingsVmType, [| db1; mockFailService |])
let testTask2 = settingsVmType.GetMethod("TestAiConnectionAsync").Invoke(setVmFail, [||]) :?> Task
testTask2.Wait()

let statusFail = getProp setVmFail settingsVmType "AiConnectionStatus" :?> string
assertTrue "TestAiConnection Failure Status contains ❌" (statusFail.StartsWith("❌"))
assertTrue "TestAiConnection Failure Status contains 401" (statusFail.Contains("401"))

// Exception mock: throws network exception
let throwHandler = new MockThrowingHttpMessageHandler(new HttpRequestException("Connection refused by endpoint"))
let mockThrowService = new LabSupport.Shared.Services.AiScriptGeneratorService(new HttpClient(throwHandler))
let setVmThrow = Activator.CreateInstance(settingsVmType, [| db1; mockThrowService |])
let testTask3 = settingsVmType.GetMethod("TestAiConnectionAsync").Invoke(setVmThrow, [||]) :?> Task
testTask3.Wait()

let statusThrow = getProp setVmThrow settingsVmType "AiConnectionStatus" :?> string
printfn "  DEBUG statusThrow: '%s'" statusThrow
assertTrue "TestAiConnection Exception Status contains ❌" (statusThrow.StartsWith("❌"))
assertTrue "TestAiConnection Exception Status contains Connection refused" (statusThrow.Contains("Connection refused"))
assertEq "TestAiConnection Exception resets IsTestingAiConnection" false (getProp setVmThrow settingsVmType "IsTestingAiConnection")

// Clean DB 1
try File.Delete(dbPath1) with _ -> ()


// -------------------------------------------------------------
// SECTION 2: ScriptViewModel Dual-Mode & Tab Switching
// -------------------------------------------------------------
printfn "\n--- Section 2: ScriptViewModel Dual-Mode & Synchronization ---"

let (dbPath2, db2) = createTestDb()
let cmdService = Activator.CreateInstance(cmdType, [| box 5902; box "" |])
let compList = Activator.CreateInstance(compListType, [||])
let scriptVm = Activator.CreateInstance(scriptVmType, [| cmdService; db2; compList; null |])

assertEq "Initial IsVisualMode is true" true (getProp scriptVm scriptVmType "IsVisualMode")
assertEq "Initial IsCodeMode is false" false (getProp scriptVm scriptVmType "IsCodeMode")

// Toggle to Code mode
setProp scriptVm scriptVmType "IsVisualMode" false
assertEq "After toggling IsVisualMode false, IsCodeMode is true" true (getProp scriptVm scriptVmType "IsCodeMode")

// Toggle to Visual mode via IsCodeMode
setProp scriptVm scriptVmType "IsCodeMode" false
assertEq "After setting IsCodeMode false, IsVisualMode is true" true (getProp scriptVm scriptVmType "IsVisualMode")

// Edit script in code mode and switch back
setProp scriptVm scriptVmType "IsVisualMode" false
setProp scriptVm scriptVmType "ScriptContent" "# Custom code written directly in editor"
assertEq "ScriptContent updated in code mode" "# Custom code written directly in editor" (getProp scriptVm scriptVmType "ScriptContent")

// Switching to visual mode regenerates script from recipe
setProp scriptVm scriptVmType "IsVisualMode" true
let regenerated = getProp scriptVm scriptVmType "ScriptContent" :?> string
assertTrue "Switching to visual mode regenerates from recipe" (regenerated.Contains("LabSupport Recipe: Deep Clean"))


// -------------------------------------------------------------
// SECTION 3: ScriptViewModel Action Steps & Sequence Manipulation
// -------------------------------------------------------------
printfn "\n--- Section 3: Action Step Catalog & Sequence Manipulation ---"

let stepsCollection = getProp scriptVm scriptVmType "ActionSteps" :?> System.Collections.IList
assertEq "Initial Deep Clean step count" 4 stepsCollection.Count

// 3.1 AddAction by Enum
let addActionMethod = scriptVmType.GetMethod("AddAction")
let flushDnsEnumVal = Enum.Parse(scriptActionTypeEnum, "FlushDnsNetwork")
addActionMethod.Invoke(scriptVm, [| flushDnsEnumVal |]) |> ignore

assertEq "Step count after AddAction(FlushDnsNetwork)" 5 stepsCollection.Count
let lastStep = stepsCollection.[stepsCollection.Count - 1]
let lastStepTitle = lastStep.GetType().GetProperty("Title").GetValue(lastStep) :?> string
assertEq "Last step title matches Flush DNS" "Flush DNS & Jaringan" lastStepTitle

// 3.2 AddAction by String (LaunchExecutable)
addActionMethod.Invoke(scriptVm, [| box "LaunchExecutable" |]) |> ignore
assertEq "Step count after AddAction('LaunchExecutable')" 6 stepsCollection.Count
let exeStep = stepsCollection.[stepsCollection.Count - 1]
let exeStepTitle = exeStep.GetType().GetProperty("Title").GetValue(exeStep) :?> string
assertEq "Executable step title matches" "Jalankan Executable (.exe)" exeStepTitle

// Verify generated PowerShell reflects scheduled task launch mode
let scriptAfterExe = getProp scriptVm scriptVmType "ScriptContent" :?> string
assertTrue "Script contains scheduled task interactive launcher" (scriptAfterExe.Contains("InteractiveElevated") || scriptAfterExe.Contains("Register-ScheduledTask") || scriptAfterExe.Contains("schtasks.exe") || scriptAfterExe.Contains("ExecutablePath"))

// 3.3 Add all 12 action types in loop
let allActionNames = Enum.GetNames(scriptActionTypeEnum)
for actName in allActionNames do
    addActionMethod.Invoke(scriptVm, [| box actName |]) |> ignore

assertEq "Step count after adding all 12 action types" (6 + 12) stepsCollection.Count

// 3.4 MoveUpStepCommand & MoveDownStepCommand
let moveUpMethod = scriptVmType.GetMethod("MoveUpStep")
let moveDownMethod = scriptVmType.GetMethod("MoveDownStep")

let stepAt1 = stepsCollection.[1]
moveUpMethod.Invoke(scriptVm, [| stepAt1 |]) |> ignore
assertEq "Step 1 moved up to index 0" stepAt1 stepsCollection.[0]

// Boundary: Move top step up does nothing
moveUpMethod.Invoke(scriptVm, [| stepsCollection.[0] |]) |> ignore
assertEq "Moving top step up leaves it at index 0" stepAt1 stepsCollection.[0]

// Move down
moveDownMethod.Invoke(scriptVm, [| stepsCollection.[0] |]) |> ignore
assertEq "Moving index 0 down places it at index 1" stepAt1 stepsCollection.[1]

// Boundary: Move bottom step down does nothing
let bottomStep = stepsCollection.[stepsCollection.Count - 1]
moveDownMethod.Invoke(scriptVm, [| bottomStep |]) |> ignore
assertEq "Moving bottom step down leaves it at bottom" bottomStep stepsCollection.[stepsCollection.Count - 1]

// Null argument safety
moveUpMethod.Invoke(scriptVm, [| null |]) |> ignore
moveDownMethod.Invoke(scriptVm, [| null |]) |> ignore
assertTrue "MoveUp/Down with null executed without throwing" true

// 3.5 RemoveStep
let removeMethod = scriptVmType.GetMethod("RemoveStep")
let countBeforeRemove = stepsCollection.Count
let stepToRemove = stepsCollection.[2]
removeMethod.Invoke(scriptVm, [| stepToRemove |]) |> ignore
assertEq "Step count decremented after RemoveStep" (countBeforeRemove - 1) stepsCollection.Count
assertTrue "Removed step is no longer in collection" (not (stepsCollection.Contains(stepToRemove)))

// Null argument safety
removeMethod.Invoke(scriptVm, [| null |]) |> ignore
assertEq "RemoveStep with null does not change count" (countBeforeRemove - 1) stepsCollection.Count

// 3.6 ClearRecipe
let clearMethod = scriptVmType.GetMethod("ClearRecipe")
clearMethod.Invoke(scriptVm, [||]) |> ignore
assertEq "ActionSteps empty after ClearRecipe" 0 stepsCollection.Count
assertEq "RecipeName reset to Resep Script Baru" "Resep Script Baru" (getProp scriptVm scriptVmType "RecipeName")


// -------------------------------------------------------------
// SECTION 4: ScriptViewModel Presets
// -------------------------------------------------------------
printfn "\n--- Section 4: Script Preset Applications ---"

let applyPresetMethod = scriptVmType.GetMethod("ApplyPreset")

let presetTests = [
    ("preset-deep-clean", 4, "Deep Clean")
    ("preset-exam-prep", 5, "Persiapan Ujian")
    ("preset-post-exam", 4, "Normalisasi Pasca Ujian")
    ("preset-launch-lab", 1, "Luncurkan Aplikasi Lab")
    ("preset-install-software", 2, "Pasang Software Standar")
    ("preset-scheduled-shutdown", 2, "Matikan Lab Terjadwal")
]

for (presetId, expectedSteps, expectedNamePrefix) in presetTests do
    applyPresetMethod.Invoke(scriptVm, [| box presetId |]) |> ignore
    let rName = getProp scriptVm scriptVmType "RecipeName" :?> string
    let currentSteps = (getProp scriptVm scriptVmType "ActionSteps" :?> System.Collections.IList).Count
    assertTrue (sprintf "Preset %s name contains '%s' (got: %s)" presetId expectedNamePrefix rName) (rName.Contains(expectedNamePrefix))
    assertEq (sprintf "Preset %s step count" presetId) expectedSteps currentSteps
    let ps = getProp scriptVm scriptVmType "ScriptContent" :?> string
    assertTrue (sprintf "Preset %s generates valid PowerShell script" presetId) (ps.Contains(rName))


// -------------------------------------------------------------
// SECTION 5: Recipe File I/O & Standalone Export
// -------------------------------------------------------------
printfn "\n--- Section 5: Recipe Serialization, File I/O & Standalone Export ---"

// Apply Exam Prep preset first
applyPresetMethod.Invoke(scriptVm, [| box "preset-exam-prep" |]) |> ignore

let tempRecipeJson = Path.Combine(Path.GetTempPath(), sprintf "test_recipe_%s.json" (Guid.NewGuid().ToString("N")))
let tempBatchBat = Path.Combine(Path.GetTempPath(), sprintf "test_export_%s.bat" (Guid.NewGuid().ToString("N")))

// 5.1 Save Recipe with custom SaveRecipeFileDialogHandler delegate
let saveDialogFunc = new Func<string>(fun () -> tempRecipeJson)
scriptVmType.GetProperty("SaveRecipeFileDialogHandler").SetValue(scriptVm, saveDialogFunc)

let saveRecipeTask = scriptVmType.GetMethod("SaveRecipeAsync").Invoke(scriptVm, [||]) :?> Task
saveRecipeTask.Wait()

assertTrue "Saved recipe JSON file exists on disk" (File.Exists(tempRecipeJson))
let jsonContent = File.ReadAllText(tempRecipeJson)
assertTrue "Recipe JSON contains exam prep title" (jsonContent.Contains("Persiapan Ujian"))

// Verify roundtrip deserialization using ScriptRecipe.FromJson
let fromJsonMethod = scriptRecipeType.GetMethod("FromJson", [| typeof<string> |])
let deserialized = fromJsonMethod.Invoke(null, [| box jsonContent |])
assertTrue "FromJson deserialized successfully" (deserialized <> null)
let desSteps = deserialized.GetType().GetProperty("Steps").GetValue(deserialized) :?> System.Collections.IList
assertEq "Deserialized step count matches 5" 5 desSteps.Count

// 5.2 Save with handler returning null (Cancel)
let saveCancelFunc = new Func<string>(fun () -> null)
scriptVmType.GetProperty("SaveRecipeFileDialogHandler").SetValue(scriptVm, saveCancelFunc)
let cancelTask = scriptVmType.GetMethod("SaveRecipeAsync").Invoke(scriptVm, [||]) :?> Task
cancelTask.Wait()
assertTrue "Save cancelled without throwing" true

// 5.3 Load Recipe with OpenRecipeFileDialogHandler
// Clear first
clearMethod.Invoke(scriptVm, [||]) |> ignore
assertEq "ActionSteps cleared before load" 0 stepsCollection.Count

let openDialogFunc = new Func<string>(fun () -> tempRecipeJson)
scriptVmType.GetProperty("OpenRecipeFileDialogHandler").SetValue(scriptVm, openDialogFunc)

let loadRecipeTask = scriptVmType.GetMethod("LoadRecipeAsync").Invoke(scriptVm, [||]) :?> Task
loadRecipeTask.Wait()

assertEq "ActionSteps restored from JSON (5 steps)" 5 stepsCollection.Count
let loadedName = getProp scriptVm scriptVmType "RecipeName" :?> string
assertTrue "Loaded RecipeName matches Persiapan Ujian" (loadedName.Contains("Persiapan Ujian"))
let loadedScript = getProp scriptVm scriptVmType "ScriptContent" :?> string
assertTrue "Loaded ScriptContent synchronized" (loadedScript.Contains("Persiapan Ujian"))

// 5.4 Load Recipe with Corrupt JSON
let corruptJsonFile = Path.Combine(Path.GetTempPath(), sprintf "corrupt_%s.json" (Guid.NewGuid().ToString("N")))
File.WriteAllText(corruptJsonFile, "{ invalid_json_syntax: true, unclosed")

let openCorruptFunc = new Func<string>(fun () -> corruptJsonFile)
scriptVmType.GetProperty("OpenRecipeFileDialogHandler").SetValue(scriptVm, openCorruptFunc)
let loadCorruptTask = scriptVmType.GetMethod("LoadRecipeAsync").Invoke(scriptVm, [||]) :?> Task
loadCorruptTask.Wait()

let statusAfterCorrupt = getProp scriptVm scriptVmType "StatusText" :?> string
assertTrue "Loading corrupt JSON sets error status" (statusAfterCorrupt.Contains("Gagal") || statusAfterCorrupt.Contains("tidak valid"))
assertEq "Corrupt JSON load did not clear existing steps" 5 stepsCollection.Count

// 5.5 Load cancelled
let openCancelFunc = new Func<string>(fun () -> null)
scriptVmType.GetProperty("OpenRecipeFileDialogHandler").SetValue(scriptVm, openCancelFunc)
let loadCancelTask = scriptVmType.GetMethod("LoadRecipeAsync").Invoke(scriptVm, [||]) :?> Task
loadCancelTask.Wait()
assertTrue "Load cancelled without throwing" true

// 5.6 Export Batch Script
let exportDialogFunc = new Func<string>(fun () -> tempBatchBat)
scriptVmType.GetProperty("ExportBatchFileDialogHandler").SetValue(scriptVm, exportDialogFunc)

let exportBatchTask = scriptVmType.GetMethod("ExportBatchScriptAsync").Invoke(scriptVm, [||]) :?> Task
exportBatchTask.Wait()

assertTrue "Exported .bat file exists on disk" (File.Exists(tempBatchBat))
let batContent = File.ReadAllText(tempBatchBat)
assertTrue "Batch script begins with @echo off" (batContent.Contains("@echo off"))
assertTrue "Batch script contains UAC elevation net session" (batContent.Contains("net session"))
assertTrue "Batch script contains Start-Process -Verb RunAs" (batContent.Contains("Start-Process") && batContent.Contains("RunAs"))
assertTrue "Batch script contains payload delimiter" (batContent.Contains(":::___LABSUPPORT_POWERSHELL_PAYLOAD___"))
assertTrue "Batch script contains recipe payload commands" (batContent.Contains("Persiapan Ujian"))

// Clean up temp files
try File.Delete(tempRecipeJson) with _ -> ()
try File.Delete(tempBatchBat) with _ -> ()
try File.Delete(corruptJsonFile) with _ -> ()


// -------------------------------------------------------------
// SECTION 6: AI Assistant Generation Integration
// -------------------------------------------------------------
printfn "\n--- Section 6: AI Assistant Recipe Generation & Error Handling ---"

// 6.1 Toggle AI prompt box
let toggleAiCmd = getProp scriptVm scriptVmType "ToggleAiPromptCommand" :?> System.Windows.Input.ICommand
assertEq "Initial IsAiPromptOpen is false" false (getProp scriptVm scriptVmType "IsAiPromptOpen")
toggleAiCmd.Execute(null)
assertEq "After Toggle, IsAiPromptOpen is true" true (getProp scriptVm scriptVmType "IsAiPromptOpen")
toggleAiCmd.Execute(null)
assertEq "After second Toggle, IsAiPromptOpen is false" false (getProp scriptVm scriptVmType "IsAiPromptOpen")

// 6.2 Generate with empty prompt
setProp scriptVm scriptVmType "AiPromptText" ""
let genTask1 = scriptVmType.GetMethod("GenerateWithAiAsync").Invoke(scriptVm, [||]) :?> Task
genTask1.Wait()
assertEq "Empty prompt triggers validation message" "Ketik instruksi lab terlebih dahulu." (getProp scriptVm scriptVmType "AiStatusMessage")

// 6.3 Generate with missing API Key
setProp scriptVm scriptVmType "AiPromptText" "Bersihkan cache semua PC lab"
// Ensure SQLite has empty API key
let clearKeyTask = dbType.GetMethod("SaveAiRouterSettingsAsync").Invoke(db2, [| box "https://api.9router.com/v1"; box ""; box "openai/gpt-4o-mini" |]) :?> Task
clearKeyTask.Wait()

let genTask2 = scriptVmType.GetMethod("GenerateWithAiAsync").Invoke(scriptVm, [||]) :?> Task
genTask2.Wait()
let noKeyMsg = getProp scriptVm scriptVmType "AiStatusMessage" :?> string
assertTrue "Missing API key warning displayed" (noKeyMsg.Contains("API Key 9router belum dikonfigurasi"))

// 6.4 Generate with mock AI service returning valid recipe
let saveKeyTask = dbType.GetMethod("SaveAiRouterSettingsAsync").Invoke(db2, [| box "https://api.9router.com/v1"; box "sk-valid-key"; box "openai/gpt-4o-mini" |]) :?> Task
saveKeyTask.Wait()

let aiRecipeContentJson = """{"name":"Resep Khusus AI: Lab Multimedia","description":"Dibuat otomatis oleh 9router AI","steps":[{"actionType":"ClearTempCache","title":"Bersihkan Cache Multimedia","isEnabled":true},{"actionType":"SetSystemVolume","title":"Atur Volume 40%","isEnabled":true,"volumePercent":40},{"actionType":"OpenUrlBrowser","title":"Buka Blender Tutorial","isEnabled":true,"targetUrl":"https://blender.org"}]}"""
let rawMsg = sprintf "```json\n%s\n```" aiRecipeContentJson
let mockResponseObj = dict [
    ("choices", box [|
        dict [
            ("message", box (dict [
                ("content", box rawMsg)
            ]))
        ]
    |])
]
let aiChatCompletionResponse = System.Text.Json.JsonSerializer.Serialize(mockResponseObj)

let aiSuccessHandler = new MockHttpMessageHandler(fun req ->
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(aiChatCompletionResponse, Encoding.UTF8, "application/json")
    resp
)
let mockAiGenSuccess = new LabSupport.Shared.Services.AiScriptGeneratorService(new HttpClient(aiSuccessHandler))
let scriptVmAi = Activator.CreateInstance(scriptVmType, [| cmdService; db2; compList; mockAiGenSuccess |])

setProp scriptVmAi scriptVmType "IsAiPromptOpen" true
setProp scriptVmAi scriptVmType "AiPromptText" "Buat persiapan lab multimedia untuk desain 3D"

let genAiTask = scriptVmType.GetMethod("GenerateWithAiAsync").Invoke(scriptVmAi, [||]) :?> Task
genAiTask.Wait()

let aiSuccessStatus = getProp scriptVmAi scriptVmType "AiStatusMessage" :?> string
printfn "  DEBUG aiSuccessStatus: '%s'" aiSuccessStatus
assertTrue "AI generation success status contains ✅" (aiSuccessStatus.StartsWith("✅"))
assertTrue "AI generation success status contains recipe name" (aiSuccessStatus.Contains("Resep Khusus AI: Lab Multimedia"))
assertEq "AI generated recipe updated CurrentRecipe Name" "Resep Khusus AI: Lab Multimedia" (getProp scriptVmAi scriptVmType "RecipeName")
let aiSteps = getProp scriptVmAi scriptVmType "ActionSteps" :?> System.Collections.IList
assertEq "AI generated recipe updated ActionSteps count to 3" 3 aiSteps.Count
assertEq "AI prompt box closed automatically on success" false (getProp scriptVmAi scriptVmType "IsAiPromptOpen")
assertEq "AI prompt text cleared on success" "" (getProp scriptVmAi scriptVmType "AiPromptText")
let aiScriptContent = getProp scriptVmAi scriptVmType "ScriptContent" :?> string
assertTrue "ScriptContent contains AI recipe name" (aiScriptContent.Contains("Resep Khusus AI: Lab Multimedia"))

// 6.5 Generate with mock AI service returning failure
let aiFailHandler = new MockHttpMessageHandler(fun req ->
    let resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
    resp.Content <- new StringContent("""{"error":"Model 9router overload. Please retry."}""", Encoding.UTF8, "application/json")
    resp
)
let mockAiGenFail = new LabSupport.Shared.Services.AiScriptGeneratorService(new HttpClient(aiFailHandler))
let scriptVmAiFail = Activator.CreateInstance(scriptVmType, [| cmdService; db2; compList; mockAiGenFail |])
setProp scriptVmAiFail scriptVmType "AiPromptText" "Perbaiki koneksi wifi"

let genFailTask = scriptVmType.GetMethod("GenerateWithAiAsync").Invoke(scriptVmAiFail, [||]) :?> Task
genFailTask.Wait()

let failStatus = getProp scriptVmAiFail scriptVmType "AiStatusMessage" :?> string
assertTrue "AI failure message starts with ❌" (failStatus.StartsWith("❌"))
assertTrue "AI failure message contains overload" (failStatus.Contains("overload") || failStatus.Contains("429"))
assertEq "AI failure does not change initial preset" "Deep Clean" (getProp scriptVmAiFail scriptVmType "RecipeName")

// 6.6 Generate with mock throwing exception
let aiThrowHandler = new MockThrowingHttpMessageHandler(new HttpRequestException("Network timeout to 9router gateway"))
let mockAiGenThrow = new LabSupport.Shared.Services.AiScriptGeneratorService(new HttpClient(aiThrowHandler))
let scriptVmAiThrow = Activator.CreateInstance(scriptVmType, [| cmdService; db2; compList; mockAiGenThrow |])
setProp scriptVmAiThrow scriptVmType "AiPromptText" "Tes error jaringan"

let genThrowTask = scriptVmType.GetMethod("GenerateWithAiAsync").Invoke(scriptVmAiThrow, [||]) :?> Task
genThrowTask.Wait()

let throwStatus = getProp scriptVmAiThrow scriptVmType "AiStatusMessage" :?> string
assertTrue "AI exception message starts with ❌" (throwStatus.StartsWith("❌"))
assertTrue "AI exception message contains timeout" (throwStatus.Contains("Network timeout"))
assertEq "AI exception resets IsAiGenerating to false" false (getProp scriptVmAiThrow scriptVmType "IsAiGenerating")

// Clean DB 2
try File.Delete(dbPath2) with _ -> ()


// -------------------------------------------------------------
// SECTION 7: Execution Command & Target Selection
// -------------------------------------------------------------
printfn "\n--- Section 7: Script Execution Command & Selection Filters ---"

let (dbPath3, db3) = createTestDb()
let scriptVmExec = Activator.CreateInstance(scriptVmType, [| cmdService; db3; compList; null |])
let execCmd = getProp scriptVmExec scriptVmType "ExecuteCommand" :?> System.Windows.Input.ICommand

// 7.1 CanExecute disabled when ScriptContent is empty
setProp scriptVmExec scriptVmType "ScriptContent" ""
assertEq "ExecuteCommand CanExecute is false when ScriptContent empty" false (execCmd.CanExecute(null))

// 7.2 Execute with zero computers
setProp scriptVmExec scriptVmType "ScriptContent" "Write-Host 'Hello Lab'"
assertEq "ExecuteCommand CanExecute is true with content" true (execCmd.CanExecute(null))

let execTask1 = scriptVmType.GetMethod("ExecuteAsync", BindingFlags.NonPublic ||| BindingFlags.Instance).Invoke(scriptVmExec, [||]) :?> Task
execTask1.Wait()

let statusAfterZero = getProp scriptVmExec scriptVmType "StatusText" :?> string
assertEq "Status indicates no online target computers" "Tidak ada target komputer online yang dipilih." statusAfterZero

// 7.3 Add simulated online and offline computers
let comp1 = Activator.CreateInstance(labCompType)
labCompType.GetProperty("Hostname").SetValue(comp1, "LAB-PC-01")
labCompType.GetProperty("IpAddress").SetValue(comp1, "127.0.0.1")
labCompType.GetProperty("IsOnline").SetValue(comp1, true)
labCompType.GetProperty("IsSelected").SetValue(comp1, true)

let comp2 = Activator.CreateInstance(labCompType)
labCompType.GetProperty("Hostname").SetValue(comp2, "LAB-PC-02")
labCompType.GetProperty("IpAddress").SetValue(comp2, "127.0.0.2")
labCompType.GetProperty("IsOnline").SetValue(comp2, false)
labCompType.GetProperty("IsSelected").SetValue(comp2, true)

compList.GetType().GetMethod("Add").Invoke(compList, [| comp1 |]) |> ignore
compList.GetType().GetMethod("Add").Invoke(compList, [| comp2 |]) |> ignore

// Execute targets (TargetMode = AllOnline)
setProp scriptVmExec scriptVmType "SelectedTargetMode" "AllOnline"
let execTask2 = scriptVmType.GetMethod("ExecuteAsync", BindingFlags.NonPublic ||| BindingFlags.Instance).Invoke(scriptVmExec, [||]) :?> Task
execTask2.Wait()

let resultsList = getProp scriptVmExec scriptVmType "Results" :?> System.Collections.IList
assertEq "Results list populated with only online computer (1 target)" 1 resultsList.Count
let resultItem = resultsList.[0]
assertEq "Result hostname is LAB-PC-01" "LAB-PC-01" (resultItem.GetType().GetProperty("Hostname").GetValue(resultItem))

// Clean DB 3
try File.Delete(dbPath3) with _ -> ()


// -------------------------------------------------------------
// SUMMARY & VERDICT
// -------------------------------------------------------------
printfn "\n================================================================="
printfn "  EMPIRICAL CHALLENGE SUITE COMPLETED"
printfn "  Passed Assertions : %d" passedCount
printfn "  Failed Assertions : %d" failedCount
printfn "================================================================="

if failedCount = 0 then
    printfn "VERDICT: APPROVE (100%% of empirical assertions passed)"
    exit 0
else
    printfn "VERDICT: REJECT (%d assertions failed)" failedCount
    exit 1
