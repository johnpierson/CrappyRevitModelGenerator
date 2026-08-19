<#
.SYNOPSIS
  Runs the generator headless inside a real Revit session and reports what happened.

.DESCRIPTION
  Writes generation parameters to a JSON file and points the CRMG_AUTOMATION environment
  variable at it, then launches Revit with a journal that creates a new project from a default
  template (Revit's own journal syntax for directly invoking an external command by AddInId is
  not recognised by the interpreter, so this script does not attempt it). The add-in's
  App.OnStartup sees the environment variable, runs the generation - and optional cleanup - on
  the first Idling event once a document is active, writes the report, and exits Revit itself.
  This script then waits for the report JSON, prints a summary, and exits non-zero when the run
  aborted, produced no report, or timed out.

  Requires the add-in to be installed for that Revit year (a `dotnet build -c "Debug R<yy>"`
  publishes it to %AppData%\Autodesk\Revit\Addins\<year>).

.EXAMPLE
  tools\revit-smoke.ps1 -RevitYear 2026 -Seed 42 -Severity Medium -Cleanup

.EXAMPLE
  tools\revit-smoke.ps1 -RevitYear 2027 -Scenarios baseline,rooms -Mode Template
#>
[CmdletBinding()]
param(
    [int]$RevitYear = 2026,
    [int]$Seed = 42,
    [ValidateSet('Low', 'Medium', 'High')] [string]$Severity = 'Medium',
    [string[]]$Scenarios = @(),
    [switch]$Cleanup,
    [switch]$DryRun,
    # NewProjectDialog: the journal drives Revit's New Project dialog (template name from Revit.ini).
    # Template: the command itself creates the project from -TemplatePath (needs zero-doc command availability).
    [ValidateSet('NewProjectDialog', 'Template')] [string]$Mode = 'NewProjectDialog',
    [string]$TemplateName = 'Metric Multi-discipline',
    [string]$TemplatePath = '',
    [string]$WorkDir = '',
    [int]$TimeoutSec = 600,
    [switch]$KeepRevitOpenOnTimeout
)

$ErrorActionPreference = 'Stop'

$revitExe = "C:\Program Files\Autodesk\Revit $RevitYear\Revit.exe"
if (-not (Test-Path $revitExe)) { throw "Revit $RevitYear is not installed at $revitExe" }

$addin = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\CrappyRevitModelGenerator.addin"
if (-not (Test-Path $addin)) { throw "Add-in manifest not found: $addin. Build with: dotnet build -c ""Debug R$($RevitYear.ToString().Substring(2))""" }

if (-not $TemplatePath) {
    $candidates = @(
        "C:\ProgramData\Autodesk\RVT $RevitYear\Templates\English\Default-Multi-Discipline_Metric.rte",
        "C:\ProgramData\Autodesk\RVT $RevitYear\Templates\English\DefaultMetric.rte",
        "C:\ProgramData\Autodesk\RVT $RevitYear\Templates\Default_M_ENU.rte"
    )
    $TemplatePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($Mode -eq 'Template' -and -not (Test-Path $TemplatePath)) { throw "Template not found: $TemplatePath" }

if (-not $WorkDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $WorkDir = Join-Path $env:LOCALAPPDATA "CrappyRevitModelGenerator\smoke\$RevitYear-$stamp"
}
New-Item -ItemType Directory -Force $WorkDir | Out-Null

$reportPath = Join-Path $WorkDir 'report.json'
$cleanupPath = Join-Path $WorkDir 'report.cleanup.json'
$errorPath = Join-Path $WorkDir 'report.error.txt'
$savePath = Join-Path $WorkDir 'smoke.rvt'
$journalPath = Join-Path $WorkDir 'smoke.journal.txt'
$paramsPath = Join-Path $WorkDir 'automation-params.json'

# ---- Automation parameters (read by App.OnStartup via CRMG_AUTOMATION) --------------------

$pairs = [ordered]@{
    report   = $reportPath
    saveAs   = $savePath
    seed     = "$Seed"
    severity = $Severity
    cleanup  = $(if ($Cleanup) { 'true' } else { 'false' })
    dryRun   = $(if ($DryRun) { 'true' } else { 'false' })
}
if ($Scenarios.Count -gt 0) { $pairs['scenarios'] = ($Scenarios -join ',') }
if ($Mode -eq 'Template') { $pairs['template'] = $TemplatePath }
($pairs | ConvertTo-Json) | Set-Content -Path $paramsPath -Encoding utf8

# ---- Journal: only responsible for getting a document open (or none, for -Mode Template) --

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("' Crappy Revit Model Generator smoke test - generated $(Get-Date -Format u) by tools/revit-smoke.ps1")
$lines.Add('Dim Jrn')
$lines.Add('Set Jrn = CrsJournalScript')
$lines.Add('Jrn.Directive "DebugMode", "PerformAutomaticActionInErrorDialog", 1')
$lines.Add('Jrn.Directive "DebugMode", "PermissiveJournal", 1')
if ($Mode -eq 'NewProjectDialog') {
    $lines.Add('Jrn.Command "Ribbon" , "Create a new project , ID_FILE_NEW_CHOOSE_TEMPLATE"')
    $lines.Add(('Jrn.ComboBox "Modal , New Project , Dialog_Revit_NewProject" , "Control_Revit_TemplateCombo" , "SelEndOk" , "{0}"' -f $TemplateName))
    $lines.Add(('Jrn.ComboBox "Modal , New Project , Dialog_Revit_NewProject" , "Control_Revit_TemplateCombo" , "Select" , "{0}"' -f $TemplateName))
    $lines.Add('Jrn.PushButton "Modal , New Project , Dialog_Revit_NewProject" , "OK, IDOK"')
}
# Safety net only: App.OnStartup posts ExitRevit itself once automation finishes. This fires
# if automation never runs at all (e.g. the environment variable failed to reach the process).
$lines.Add('Jrn.Command "Internal" , "Flush undo and redo stacks , ID_FLUSH_UNDO"')
$lines.Add('Jrn.Command "Internal" , "Quit the application; prompts to save projects , ID_APP_EXIT"')
[System.IO.File]::WriteAllLines($journalPath, $lines, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Work dir : $WorkDir"
Write-Host "Params   : $paramsPath"
Write-Host "Journal  : $journalPath"
Write-Host "Revit    : $revitExe"
Write-Host "Mode     : $Mode  seed=$Seed severity=$Severity cleanup=$($Cleanup.IsPresent) scenarios=$(if ($Scenarios) { $Scenarios -join ',' } else { '(defaults)' })"

# ---- Run ----------------------------------------------------------------------------------

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $revitExe
$psi.Arguments = "`"$journalPath`""
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['CRMG_AUTOMATION'] = $paramsPath
$proc = [System.Diagnostics.Process]::Start($psi)
Write-Host "Started Revit PID $($proc.Id); waiting up to $TimeoutSec s ..."

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$reportSeen = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    if (Test-Path $reportPath) { $reportSeen = $true }
    if (Test-Path $errorPath) { break }
    if ($proc.HasExited) { break }
    if ($reportSeen -and (-not $Cleanup -or (Test-Path $cleanupPath))) {
        # Give the journal a moment to quit on its own; do not wait the whole timeout for that.
        $graceDeadline = (Get-Date).AddSeconds(90)
        while (-not $proc.HasExited -and (Get-Date) -lt $graceDeadline) { Start-Sleep -Seconds 3 }
        break
    }
}

if (-not $proc.HasExited) {
    if ($KeepRevitOpenOnTimeout) {
        Write-Warning "Revit is still running (PID $($proc.Id)); leaving it open as requested."
    } else {
        Write-Warning "Revit did not exit in time; stopping PID $($proc.Id)."
        try { Stop-Process -Id $proc.Id -Force -Confirm:$false } catch { }
    }
}

# ---- Summary ------------------------------------------------------------------------------

if (Test-Path $errorPath) {
    Write-Host "`n=== ERROR (report.error.txt) ===" -ForegroundColor Red
    Get-Content $errorPath | Select-Object -First 40
}

if (-not (Test-Path $reportPath)) {
    Write-Host "`nNo report was written. Check the journal Revit produced under %LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit $RevitYear\Journals for the last lines." -ForegroundColor Red
    exit 3
}

$report = Get-Content $reportPath -Raw | ConvertFrom-Json
Write-Host "`n=== Generation report ===" -ForegroundColor Cyan
Write-Host ("Run {0}  seed {1}  Revit {2}  generator {3}  aborted={4}  dryRun={5}" -f $report.RunId, $report.Seed, $report.RevitVersion, $report.GeneratorVersion, $report.Aborted, $report.DryRun)
if ($report.Aborted) { Write-Host ("ABORTED: {0}" -f $report.AbortReason) -ForegroundColor Red }
Write-Host 'Counts:'
$report.Counts.PSObject.Properties | ForEach-Object { Write-Host ("  {0,-22}{1,6}" -f $_.Name, $_.Value) }
Write-Host 'Scenarios:'
$report.Scenarios | ForEach-Object { Write-Host ("  {0,-11} {1,-20} elements={2,-4} {3}" -f $_.Status, $_.ScenarioId, $_.ElementsCreated, $_.Message) }
$defects = @($report.Notes | Where-Object { $_.Kind -eq 'Defect' })
$fallbacks = @($report.Notes | Where-Object { $_.Kind -eq 'Fallback' })
Write-Host ("Defects recorded: {0}   Fallbacks: {1}   Expected warnings dismissed: {2}   Unexpected failures: {3}" -f $defects.Count, $fallbacks.Count, @($report.ExpectedWarnings).Count, @($report.Failures).Count)
if ($fallbacks.Count -gt 0) { Write-Host 'Fallbacks:' -ForegroundColor Yellow; $fallbacks | ForEach-Object { Write-Host ("  [{0}] {1}" -f $_.ScenarioId, $_.Message) } }
if (@($report.Failures).Count -gt 0) {
    Write-Host 'Unexpected failures:' -ForegroundColor Yellow
    $report.Failures | ForEach-Object { Write-Host ("  [{0}] {1}: {2}  op={3}" -f $_.ScenarioId, $_.Severity, $_.Message, $_.Operation) }
}

if ($Cleanup) {
    if (Test-Path $cleanupPath) {
        $c = Get-Content $cleanupPath -Raw | ConvertFrom-Json
        Write-Host "`n=== Cleanup ===" -ForegroundColor Cyan
        Write-Host ("Deleted {0}  Kept {1}  AlreadyGone {2}  RunRecordsRemoved {3}  RemainingTagged {4}  RemainingRunRecords {5}  RemainingRecordedIds {6}" -f $c.Deleted, $c.Kept, $c.AlreadyGone, $c.RunRecordsRemoved, $c.RemainingTaggedElements, $c.RemainingRunRecords, $c.RemainingRecordedIds)
        if (@($c.KeptDetails).Count -gt 0) { $c.KeptDetails | ForEach-Object { Write-Host ("  kept {0}: {1}" -f $_.ElementId, $_.Reason) } }
        if (@($c.Failures).Count -gt 0) { $c.Failures | ForEach-Object { Write-Host ("  failure: {0}" -f $_.Message) -ForegroundColor Yellow } }
    } else {
        Write-Host "`nCleanup was requested but no cleanup report was written." -ForegroundColor Yellow
    }
}

Write-Host "`nFiles: $WorkDir"
if ($report.Aborted) { exit 2 }
exit 0
