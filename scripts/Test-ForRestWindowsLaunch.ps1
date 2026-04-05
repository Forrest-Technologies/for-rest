param(
    [string]$ExePath = "src\ForRest.Maui\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\ForRest.Maui.exe",
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'

$resolvedExe = (Resolve-Path $ExePath).Path
if (-not (Test-Path $resolvedExe)) {
    throw "Executable '$ExePath' was not found."
}

$process = Start-Process -FilePath $resolvedExe -PassThru
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Milliseconds 250
        $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($null -eq $running) {
            throw "ForRest exited before creating a window."
        }

        if ($running.MainWindowHandle -ne 0) {
            $result = [pscustomobject]@{
                ProcessId = $running.Id
                WindowTitle = $running.MainWindowTitle
                MainWindowHandle = $running.MainWindowHandle
                WindowAppearedAfterMs = $stopwatch.ElapsedMilliseconds
            }
            $result | Format-List * | Out-Host
            return
        }
    }

    throw "ForRest did not create a visible window within $TimeoutSeconds seconds."
}
finally {
    try {
        $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($running) {
            Stop-Process -Id $process.Id -Force
        }
    }
    catch {
    }
}
