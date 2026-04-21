$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-ExecutionPolicy Bypass -NoProfile -File C:\@Tools\Check-WindowsActivation.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "07:00"
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest
Register-ScheduledTask -TaskName "N24_CheckWindowsActivation" -Action $action -Trigger $trigger -Principal $principal -Description "Prueft Windows-Aktivierung, sendet Mail bei Verlust"