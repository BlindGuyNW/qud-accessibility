#!/usr/bin/env pwsh
# Creates a release zip: QudAccessibility-v<version>.zip
# containing QudAccessibility/*.cs and QudAccessibility/manifest.json

$ErrorActionPreference = 'Stop'

$manifest = Get-Content -Raw src/manifest.json | ConvertFrom-Json
$version = $manifest.version
$modName = $manifest.id
$zipName = "$modName-v$version.zip"

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "release-$modName"
$modDir = Join-Path $staging $modName

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $modDir | Out-Null

Copy-Item src/*.cs $modDir
Copy-Item src/manifest.json $modDir

$outPath = Join-Path $PSScriptRoot $zipName
if (Test-Path $outPath) { Remove-Item $outPath }

$count = (Get-ChildItem $modDir -File).Count

Compress-Archive -Path "$staging/*" -DestinationPath $outPath

Remove-Item $staging -Recurse -Force

Write-Host "Created $zipName ($count files, version $version)"
