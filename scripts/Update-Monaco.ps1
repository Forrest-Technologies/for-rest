[CmdletBinding()]
param(
	[string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$assetRoot = Join-Path $repoRoot 'src\ForRest.Maui\Resources\Raw\monaco'
$versionFile = Join-Path $assetRoot 'VERSION.txt'

if ([string]::IsNullOrWhiteSpace($Version)) {
	if (Test-Path $versionFile) {
		$Version = (Get-Content $versionFile -Raw).Trim()
	}
	else {
		$Version = '0.38.0'
	}
}

$vsTarget = Join-Path $assetRoot 'vs'
$scratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("for-rest-monaco-" + [Guid]::NewGuid().ToString('N'))

function Copy-MonacoTree {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Source,
		[Parameter(Mandatory = $true)]
		[string]$Destination
	)

	New-Item -ItemType Directory -Force -Path (Split-Path $Destination -Parent) | Out-Null
	Copy-Item $Source $Destination -Recurse
}

try {
	New-Item -ItemType Directory -Force -Path $scratchRoot | Out-Null
	Push-Location $scratchRoot

	$archiveName = npm pack "monaco-editor@$Version" --silent
	if ([string]::IsNullOrWhiteSpace($archiveName)) {
		throw "npm pack did not return an archive name for monaco-editor@$Version."
	}

	tar -xf $archiveName

	$packageVsRoot = Join-Path $scratchRoot 'package\min\vs'
	if (-not (Test-Path $packageVsRoot)) {
		throw "Expected Monaco asset folder was not found at $packageVsRoot."
	}

	New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
	if (Test-Path $vsTarget) {
		Remove-Item $vsTarget -Recurse -Force
	}

	New-Item -ItemType Directory -Force -Path $vsTarget | Out-Null

	Copy-Item (Join-Path $packageVsRoot 'loader.js') (Join-Path $vsTarget 'loader.js')
	Copy-MonacoTree -Source (Join-Path $packageVsRoot 'base') -Destination (Join-Path $vsTarget 'base')
	Copy-MonacoTree -Source (Join-Path $packageVsRoot 'editor') -Destination (Join-Path $vsTarget 'editor')
	Copy-MonacoTree -Source (Join-Path $packageVsRoot 'language\json') -Destination (Join-Path $vsTarget 'language\json')

	Get-ChildItem -Path $vsTarget -Recurse -File |
		Where-Object { $_.Name -like '*.nls.*.js' } |
		Remove-Item -Force

	Set-Content -Path $versionFile -Value $Version

	Write-Host "Monaco editor $Version restored to $vsTarget"
}
finally {
	if ((Get-Location).Path -eq $scratchRoot) {
		Pop-Location
	}

	if (Test-Path $scratchRoot) {
		Remove-Item $scratchRoot -Recurse -Force
	}
}
