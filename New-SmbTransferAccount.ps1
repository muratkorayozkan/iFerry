#Requires -RunAsAdministrator
param([string]$Folder = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'iFerry'))
$ErrorActionPreference = 'Stop'
$name = 'iPhoneTransfer'
$share = Get-SmbShare -Name 'iFerry'
if ($share.Path -ne $Folder) { throw 'Beklenmeyen paylasim yolu.' }
$description = 'iPhone SMB dosya aktarimi'
$user = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
$created = -not $user
if ($created) {
    $credential = Get-Credential -UserName "$env:COMPUTERNAME\$name" -Message 'iPhone SMB aktarimi icin yeni bir parola belirleyin. Ayni parolayi iPhone ekraninda kullanin.'
    if (-not $credential) { throw 'Parola girilmedi.' }
    $user = New-LocalUser -Name $name -Password $credential.Password -Description $description
} elseif ($user.Description -ne $description -or -not $user.Enabled) {
    throw 'The existing account is not an enabled iFerry transfer account.'
}
$previousAcl = Get-Acl -LiteralPath $share.Path
try {
    $acl = Get-Acl -LiteralPath $share.Path
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($user.SID,'Modify','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $share.Path -AclObject $acl
    Grant-SmbShareAccess -Name $share.Name -AccountName "$env:COMPUTERNAME\$name" -AccessRight Change -Force | Out-Null
} catch {
    Set-Acl -LiteralPath $share.Path -AclObject $previousAcl
    if ($created) { Remove-LocalUser -SID $user.SID }
    throw
}
