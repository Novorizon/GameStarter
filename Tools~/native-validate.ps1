param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet("ImportSamples", "Compile", "Editor", "Player", "SchemaPatch", "PatchPlayer")][string]$Phase = "Editor",
    [string]$ProjectPath = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = "Stop"
$project = (Resolve-Path $ProjectPath).Path
if (!(Test-Path $UnityEditor)) { throw "Unity Editor executable was not found: $UnityEditor" }
$report = Join-Path $project "Artifacts/NativeValidation"
New-Item -ItemType Directory -Force $report | Out-Null

function Invoke-Unity([string]$method, [string]$name, [bool]$quit) {
    Remove-Item (Join-Path $report ($name + ".json")) -ErrorAction SilentlyContinue
    if ($name -eq "import-samples") { Remove-Item (Join-Path $report "imported-samples.txt") -ErrorAction SilentlyContinue }
    $arguments = @("-batchmode", "-projectPath", "`"$project`"", "-buildTarget", "Win64", "-executeMethod", $method, "-logFile", "`"$(Join-Path $report ($name + '.log'))`"")
    if ($quit) { $arguments += "-quit" }
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Unity $name failed ($($process.ExitCode)); inspect $report" }
    if ($name -in @("compile", "editor")) {
        $result = Get-Content (Join-Path $report ($name + ".json")) -Raw | ConvertFrom-Json
        if (!$result.success) { throw "Unity $name did not report success" }
    }
    if ($name -eq "import-samples" -and !(Test-Path (Join-Path $report "imported-samples.txt"))) {
        throw "Sample import report is missing"
    }
}

switch ($Phase) {
    "ImportSamples" {
        Invoke-Unity "Game.Editor.SampleValidation.ImportSamples" "import-samples" $true
    }
    "Compile" {
        Invoke-Unity "Game.Editor.NativeValidation.CompileScripts" "compile" $true
    }
    "Editor" {
        Invoke-Unity "Game.Editor.NativeValidation.EditorSmoke" "editor" $false
    }
    "Player" {
        Invoke-Unity "Game.Editor.NativeValidation.BuildHybridPlayer" "player-build" $true
    }
    "SchemaPatch" {
        # Use an isolated validation checkout: this intentionally adds a NEW table/type after the Player was built.
        Copy-Item (Join-Path $project "NativeFixtures~/SchemaPatch/native_patch.xml") (Join-Path $project "Config/Luban/Defines/native_patch.xml") -Force
        Invoke-Unity "Game.Editor.NativeValidation.GenerateTables" "schema-generation" $false
        # A separate process imports/compiles the regenerated hot assembly before patch export.
        Invoke-Unity "Game.Editor.NativeValidation.ExportPatch" "patch-export" $true
        Write-Host "Upload Artifacts/Publish/assets and patches first, then manifests, to /srv/gameserver/updates/."
        Write-Host "Keep the original Player. Run -Phase PatchPlayer after publishing."
        exit 0
    }
}

if ($Phase -in @("Player", "PatchPlayer")) {
    $baseline = Get-Content (Join-Path $project "Assets/StreamingAssets/HotUpdate/builtin.json") -Raw | ConvertFrom-Json
    $build = Join-Path $project ("Builds/windows-x64/" + $baseline.baselineId)
    $player = Get-ChildItem $build -Filter *.exe | Select-Object -First 1
    if (!$player) { throw "Built Player executable is missing: $build" }
    $name = if ($Phase -eq "PatchPlayer") { "patch-player" } else { "player" }
    $log = Join-Path $report ($name + ".log")
    $arguments = @("-batchmode", "-nographics", "-logFile", "`"$log`"", "--novorizon-native-smoke")
    if ($Phase -eq "PatchPlayer") { $arguments += @("--expect-luban-table", "TbNativePatchProbe") }
    $process = Start-Process -FilePath $player.FullName -ArgumentList $arguments -PassThru
    if (!$process.WaitForExit(180000)) { $process.Kill(); throw "Player smoke timed out" }
    if ($process.ExitCode -ne 0) { throw "Player failed ($($process.ExitCode)); inspect $log" }
    $text = Get-Content $log -Raw
    if ($text -notmatch "NATIVE_PLAYER_READY") { throw "Player readiness marker is missing" }
    if ($Phase -eq "PatchPlayer" -and $text -notmatch "NATIVE_SCHEMA_READY TbNativePatchProbe") { throw "New hot table was not loaded by the original Player" }
    @{ phase=$name; success=$true; utc=[DateTime]::UtcNow.ToString("O"); log=$log } | ConvertTo-Json | Set-Content (Join-Path $report ($name + ".json"))
}
Write-Host "Native validation phase completed. Reports: $report"
