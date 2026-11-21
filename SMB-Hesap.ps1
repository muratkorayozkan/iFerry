#Requires -RunAsAdministrator
param([string]$Folder = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'iPhoneAktarim'))
$ErrorActionPreference = 'Stop'
$name = 'iPhoneTransfer'
$share = Get-SmbShare -Name 'iPhoneAktarim'
if ($share.Path -ne $Folder) { throw 'Beklenmeyen paylasim yolu.' }
if (Get-LocalUser -Name $name -ErrorAction SilentlyContinue) { throw 'Bu hesap zaten var.' }
$credential = Get-Credential -UserName "$env:COMPUTERNAME\$name" -Message 'iPhone SMB aktarimi icin yeni bir parola belirleyin. Ayni parolayi iPhone ekraninda kullanin.'
if (-not $credential) { throw 'Parola girilmedi.' }
$user = New-LocalUser -Name $name -Password $credential.Password -Description 'iPhone SMB dosya aktarimi'
try {
    $acl = Get-Acl -LiteralPath $share.Path
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($user.SID,'Modify','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $share.Path -AclObject $acl
    Grant-SmbShareAccess -Name $share.Name -AccountName "$env:COMPUTERNAME\$name" -AccessRight Change -Force | Out-Null
} catch {
    if ($rule) {
        $currentAcl = Get-Acl -LiteralPath $share.Path
        $currentAcl.RemoveAccessRuleSpecific($rule)
        Set-Acl -LiteralPath $share.Path -AclObject $currentAcl
    }
    Remove-LocalUser -SID $user.SID
    throw
}
