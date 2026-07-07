# Auto-test inner-loop gate (loop engineering).
#
# Fired by a PostToolUse(Write|Edit) hook after Claude edits a C# file. Runs the
# solution's tests in the background; on failure it exits 2 so the `asyncRewake`
# hook feeds the failure straight back to Claude to correct. On success it is silent.
#
# Invoked as:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/auto-test.ps1
# The tool payload arrives as JSON on stdin.

# --- parse the hook payload ------------------------------------------------
try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
} catch {
    exit 0   # unparseable stdin -> do nothing, never block the edit
}

$file = $payload.tool_input.file_path
if (-not $file) { $file = $payload.tool_response.filePath }

# Only react to C# source (covers both source/ and tests/). Anything else: no-op.
if (-not $file -or $file -notmatch '\.cs$') { exit 0 }

# --- single-flight lock ----------------------------------------------------
# A test run takes ~50s; rapid edits must not spawn overlapping `dotnet test`
# runs (they collide on obj/bin files and report false failures). Skip if a run
# is already live; self-heal a stale lock older than 5 minutes.
$lock = Join-Path $env:TEMP 'aitemplate-autotest.lock'
if (Test-Path $lock) {
    if (((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 5) { exit 0 }
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType File -Path $lock -Force | Out-Null

# --- run the gate ----------------------------------------------------------
# repo root = parent of .claude/  (this script lives in .claude/hooks/)
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
try {
    Push-Location $repo
    $output = & dotnet test -c Release --nologo 2>&1 | Out-String
    $code = $LASTEXITCODE
} finally {
    Pop-Location -ErrorAction SilentlyContinue
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    $tail = ($output -split "`r?`n" | Where-Object { $_ -ne '' } | Select-Object -Last 30) -join "`n"
    Write-Output "dotnet test FAILED after editing $file`n`n$tail"
    exit 2   # asyncRewake -> feed this back to Claude
}
exit 0
