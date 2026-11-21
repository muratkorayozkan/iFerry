param(
    [Parameter(Mandatory)][string]$Target,
    [Parameter(Mandatory)][int]$AppPid
)
$ErrorActionPreference = 'Stop'
$files = @('iFerry.exe','iFerry.dll','iFerry.deps.json','iFerry.runtimeconfig.json','QRCoder.dll','Configure-SmbShare.ps1','New-SmbTransferAccount.ps1','Update-iFerry.ps1')
$exe = Join-Path $Target 'iFerry.exe'
$backup = Join-Path $PSScriptRoot 'backup'
$changed = [Collections.Generic.List[string]]::new()
try {
    $app = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
    if ($app -and $app.Path -ne $exe) { throw 'The application path does not match.' }
    if ($app -and !$app.WaitForExit(30000)) { throw 'The application did not exit.' }
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'iFerry was not found.' }
    New-Item -ItemType Directory -Path $backup | Out-Null
    foreach ($name in $files) {
        $destination = Join-Path $Target $name
        $existing = Get-Item -LiteralPath $destination -ErrorAction SilentlyContinue
        if ($existing) {
            if ($existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Unexpected entry in the installation directory.'
            }
            Copy-Item -LiteralPath $destination -Destination (Join-Path $backup $name)
        }
    }
    foreach ($name in $files) {
        $destination = Join-Path $Target $name
        $changed.Add($name)
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $destination -Force
    }
} catch {
    $failed = $_.Exception.Message
    foreach ($name in $changed) {
        $previous = Join-Path $backup $name
        $destination = Join-Path $Target $name
        if (Test-Path -LiteralPath $previous) { Copy-Item -LiteralPath $previous -Destination $destination -Force }
        elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination }
    }
    Set-Content -LiteralPath (Join-Path $Target 'iFerry-update-error.txt') -Value $failed -ErrorAction SilentlyContinue
}
if (!(Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $exe -WorkingDirectory $Target -WindowStyle Hidden
}
