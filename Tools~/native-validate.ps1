param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet("ImportSamples", "Compile", "Editor", "TowerDefense", "StartupCycles", "LocalSource", "Player", "SchemaPatch", "PatchPlayer")][string]$Phase = "Editor",
    [string]$ProjectPath = (Split-Path $PSScriptRoot -Parent),
    [switch]$PhoneLayout
)
$ErrorActionPreference = "Stop"
$project = (Resolve-Path $ProjectPath).Path
if (!(Test-Path $UnityEditor)) { throw "Unity Editor executable was not found: $UnityEditor" }

function Assert-ValidationCheckoutClosed {
    if (Test-Path -LiteralPath (Join-Path $project "Temp/UnityLockfile")) {
        throw "Unity is open in $project. Use a separate, closed checkout for native validation."
    }
}

function Invoke-WithValidationEditor([scriptblock]$action) {
    Assert-ValidationCheckoutClosed
    $source = Join-Path $project "NativeFixtures~/Editor/NativeValidation"
    $destination = Join-Path $project "Assets/Game/Editor/NativeValidation"
    if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath ($destination + ".meta"))) {
        throw "Validation Editor assets already exist: $destination. Check the validation checkout before retrying."
    }

    $staged = [System.Collections.Generic.List[string]]::new()
    $created = $false
    try {
        New-Item -ItemType Directory -Path $destination | Out-Null
        $created = $true
        foreach ($file in Get-ChildItem -LiteralPath $source -File) {
            $asset = Join-Path $destination $file.Name
            $staged.Add($asset)
            [System.IO.File]::Copy($file.FullName, $asset, $false)
        }
        [System.IO.File]::Copy(($source + ".meta"), ($destination + ".meta"), $false)
        $staged.Add($destination + ".meta")
        & $action
    }
    finally {
        if ($created) {
            if (Test-Path -LiteralPath (Join-Path $project "Temp/UnityLockfile")) {
                Write-Warning "The validation checkout is still locked. Clean up the staged Editor assets after Unity has closed."
            }
            else {
                foreach ($asset in $staged) {
                    if (Test-Path -LiteralPath $asset) { Remove-Item -LiteralPath $asset }
                }
                [System.IO.Directory]::Delete($destination)
            }
        }
    }
}

$report = Join-Path $project "Artifacts/NativeValidation"
New-Item -ItemType Directory -Force $report | Out-Null

function Invoke-Unity([string]$method, [string]$name, [bool]$quit, [string[]]$extraArguments = @()) {
    Remove-Item (Join-Path $report ($name + ".json")) -ErrorAction SilentlyContinue
    if ($name -eq "import-samples") { Remove-Item (Join-Path $report "imported-samples.txt") -ErrorAction SilentlyContinue }
    if ($name -eq "compile") { Remove-Item (Join-Path $report "sample-assemblies.json") -ErrorAction SilentlyContinue }
    $arguments = @("-batchmode", "-projectPath", "`"$project`"", "-buildTarget", "Win64", "-executeMethod", $method, "-logFile", "`"$(Join-Path $report ($name + '.log'))`"")
    if ($quit) { $arguments += "-quit" }
    if ($extraArguments.Count -gt 0) { $arguments += $extraArguments }
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity $name failed ($($process.ExitCode)); inspect $report" }
    if ($name -in @("compile", "editor")) {
        $result = Get-Content (Join-Path $report ($name + ".json")) -Raw | ConvertFrom-Json
        if (!$result.success) { throw "Unity $name did not report success" }
    }
    if ($name -eq "compile") {
        $samples = Get-Content (Join-Path $report "sample-assemblies.json") -Raw | ConvertFrom-Json
        if (!$samples.success -or $samples.sampleCount -eq 0 -or $samples.assemblies.Count -eq 0) {
            throw "Sample assembly verification did not report success"
        }
    }
    if ($name -eq "import-samples" -and !(Test-Path (Join-Path $report "imported-samples.txt"))) {
        throw "Sample import report is missing"
    }
}

function Invoke-NativeProbeBuild([string]$method, [string]$name) {
    Assert-ValidationCheckoutClosed
    $probeAssets = [ordered]@{
        "GameEntry.NativeValidation.cs" = "Assets/Game/Runtime/GameEntry.NativeValidation.cs"
        "HotGame.NativeValidation.cs" = "Assets/HotUpdate/HotGame.NativeValidation.cs"
    }
    $staged = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($probe in $probeAssets.GetEnumerator()) {
            $source = Join-Path $project ("NativeFixtures~/RuntimeProbes/" + $probe.Key)
            $asset = Join-Path $project $probe.Value
            if ((Test-Path -LiteralPath $asset) -or (Test-Path -LiteralPath ($asset + ".meta"))) {
                throw "Probe asset or metadata already exists: $asset. Check the validation checkout before retrying."
            }
            [System.IO.File]::Copy($source, $asset, $false)
            $staged.Add($asset)
        }

        Invoke-Unity $method $name $true
    }
    finally {
        if (Test-Path -LiteralPath (Join-Path $project "Temp/UnityLockfile")) {
            Write-Warning "The validation checkout is still locked. Clean up staged probe files after Unity has closed."
        }
        else {
            foreach ($asset in $staged) {
                foreach ($path in @($asset, ($asset + ".meta"))) {
                    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
                }
            }
        }
    }
}

if ($Phase -ne "PatchPlayer") {
    Invoke-WithValidationEditor {
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
            "TowerDefense" {
                $layout = @()
                if ($PhoneLayout) { $layout += "--novorizon-phone-layout" }
                Invoke-Unity "Game.Editor.TowerDefenseValidation.Run" "tower-defense" $false $layout
            }
            "StartupCycles" {
                Invoke-Unity "Game.Editor.GameEntryValidation.Run" "startup-cycles" $false
            }
            "LocalSource" {
                Invoke-Unity "Game.Editor.NativeValidation.BuildLocalPlayer" "local-source" $true
            }
            "Player" {
                Invoke-NativeProbeBuild "Game.Editor.NativeValidation.BuildHybridPlayer" "player-build"
            }
            "SchemaPatch" {
                # Use an isolated validation checkout: this adds a new table/type after the Player was built.
                Copy-Item (Join-Path $project "NativeFixtures~/SchemaPatch/native_patch.xml") (Join-Path $project "Config/Luban/Defines/native_patch.xml") -Force
                Invoke-Unity "Game.Editor.NativeValidation.GenerateTables" "schema-generation" $false
                # A separate process imports the regenerated hot assembly before patch export.
                Invoke-NativeProbeBuild "Game.Editor.NativeValidation.ExportPatch" "patch-export"
                Write-Host "Validation-only payload: Artifacts/NativeValidation/Publish. Deploy assets and patches before manifests to the validation baseline."
                Write-Host "Keep the original Player. Run -Phase PatchPlayer after publishing."
            }
        }
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
