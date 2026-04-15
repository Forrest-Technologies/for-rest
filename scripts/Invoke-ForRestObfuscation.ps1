param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,

    [string]$OutputDirectory = '',

    [string]$ToolPath = '',

    [switch]$KeepPublicApi,

    [string[]]$Assemblies = @('ForRest.Licensing.dll', 'ForRest.Domain.dll', 'ForRest.Services.dll'),

    [string]$MappingOutputPath = '',

    [string[]]$ExtraSearchPaths = @()
)

$ErrorActionPreference = 'Stop'

$resolvedInputDirectory = (Resolve-Path $InputDirectory).Path
if (-not (Test-Path $resolvedInputDirectory)) {
    throw "Input directory '$InputDirectory' was not found."
}

$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $resolvedInputDirectory
}
else {
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    (Resolve-Path $OutputDirectory).Path
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('forrest-obfuscar-' + [guid]::NewGuid().ToString('N'))
$toolOutputDirectory = Join-Path $tempDirectory 'out'
New-Item -ItemType Directory -Force -Path $toolOutputDirectory | Out-Null

$availableAssemblies = @()
foreach ($assembly in $Assemblies) {
    $assemblyPath = Join-Path $resolvedInputDirectory $assembly
    if (Test-Path $assemblyPath) {
        $availableAssemblies += $assembly
    }
}

if ($availableAssemblies.Count -eq 0) {
    throw "None of the requested assemblies were found under '$resolvedInputDirectory'."
}

$configPath = Join-Path $tempDirectory 'obfuscar.xml'
$configLines = @(
    '<?xml version="1.0" encoding="utf-8"?>',
    '<Obfuscator>',
    ('  <Var name="InPath" value="' + $resolvedInputDirectory + '" />'),
    ('  <Var name="OutPath" value="' + $toolOutputDirectory + '" />'),
    ('  <Var name="KeepPublicApi" value="' + $KeepPublicApi.IsPresent.ToString().ToLowerInvariant() + '" />'),
    '  <Var name="HideStrings" value="true" />',
    '  <Var name="ReuseNames" value="false" />',
    '  <Var name="UseUnicodeNames" value="false" />',
    '  <Var name="XmlMapping" value="true" />',
    ('  <AssemblySearchPath path="' + $resolvedInputDirectory + '" />')
)

foreach ($searchPath in $ExtraSearchPaths) {
    if (Test-Path $searchPath) {
        $configLines += '  <AssemblySearchPath path="' + (Resolve-Path $searchPath).Path + '" />'
    }
}

foreach ($assembly in $availableAssemblies) {
    $configLines += '  <Module file="' + (Join-Path $resolvedInputDirectory $assembly) + '" />'
}

$configLines += '</Obfuscator>'
[System.IO.File]::WriteAllLines($configPath, $configLines)

if ([string]::IsNullOrWhiteSpace($ToolPath)) {
    dotnet tool restore | Out-Null
    dotnet tool run obfuscar.console -- $configPath
}
else {
    $resolvedToolPath = (Resolve-Path $ToolPath).Path
    & $resolvedToolPath $configPath
}

foreach ($assembly in $availableAssemblies) {
    $obfuscatedAssemblyPath = Join-Path $toolOutputDirectory $assembly
    if (-not (Test-Path $obfuscatedAssemblyPath)) {
        throw "Expected obfuscated assembly '$obfuscatedAssemblyPath' was not produced."
    }

    Copy-Item $obfuscatedAssemblyPath (Join-Path $resolvedOutputDirectory $assembly) -Force

    $pdbName = [System.IO.Path]::ChangeExtension($assembly, '.pdb')
    $obfuscatedPdbPath = Join-Path $toolOutputDirectory $pdbName
    if (Test-Path $obfuscatedPdbPath) {
        Copy-Item $obfuscatedPdbPath (Join-Path $resolvedOutputDirectory $pdbName) -Force
    }
}

$mappingSourcePath = Join-Path $toolOutputDirectory 'Mapping.xml'
if ((Test-Path $mappingSourcePath) -and -not [string]::IsNullOrWhiteSpace($MappingOutputPath)) {
    $mappingDirectory = Split-Path -Parent $MappingOutputPath
    if (-not [string]::IsNullOrWhiteSpace($mappingDirectory)) {
        New-Item -ItemType Directory -Force -Path $mappingDirectory | Out-Null
    }

    Copy-Item $mappingSourcePath $MappingOutputPath -Force
}

Write-Host "Obfuscation completed for: $($availableAssemblies -join ', ')"
