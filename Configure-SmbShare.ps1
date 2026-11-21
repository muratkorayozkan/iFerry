#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Account,
    [Parameter(Mandatory)][string]$Folder,
    [Parameter(Mandatory)][ipaddress]$PhoneAddress,
    [Parameter(Mandatory)][string]$InterfaceAlias
)
$ErrorActionPreference = 'Stop'
$shareName = 'iFerry'
$existing = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
if ($existing -and $existing.Path -ne $Folder) { throw 'Paylasim adi baska bir klasore ait.' }
if (-not (Get-SmbServerConfiguration).EnableSMB2Protocol) { throw 'SMB2/3 kapali.' }
New-Item -ItemType Directory -Path $Folder -Force | Out-Null
if (-not $existing) { New-SmbShare -Name $shareName -Path $Folder -ChangeAccess $Account -EncryptData $true | Out-Null }
$ruleName = 'iFerry-SMB'
$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
if ($rule) {
    Set-NetFirewallRule -Name $ruleName -RemoteAddress $PhoneAddress.IPAddressToString -InterfaceAlias $InterfaceAlias -Enabled True | Out-Null
} else {
    New-NetFirewallRule -Name $ruleName -DisplayName 'iPhone aktarim SMB' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 445 -RemoteAddress $PhoneAddress.IPAddressToString -InterfaceAlias $InterfaceAlias -Profile Any | Out-Null
}
Get-SmbShare -Name $shareName | Select-Object Name,Path,EncryptData
