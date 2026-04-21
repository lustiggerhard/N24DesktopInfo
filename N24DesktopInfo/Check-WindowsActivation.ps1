<#
.SYNOPSIS
    Check-WindowsActivation.ps1
.DESCRIPTION
    Prueft den Windows-Aktivierungsstatus und sendet eine E-Mail-Warnung
    wenn die Lizenz nicht aktiviert oder abgelaufen ist.
    Wird als Scheduled Task taeglich ausgefuehrt.
.AUTHOR
    Gerhard Lustig (gerhard@lustig.at)
.VERSION
    1.0.0
.DATE
    2026-04-21
.HISTORY
    2026-04-21  v1.0.0  Initiale Version
#>

# ============================================================
#  KONFIGURATION
# ============================================================

# SMTP-Server (Zerberus Postfix)
$SmtpServer = "192.168.0.70"
$SmtpPort = 25
$MailFrom = "activation-check@netz24.at"
$MailTo = "gerhard@lustig.at"

# Log-Datei (im Script-Verzeichnis)
$LogFile = Join-Path $PSScriptRoot "Check-WindowsActivation.log"

# ============================================================
#  FUNKTIONEN
# ============================================================

function Write-Log($msg) {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$ts] $msg"
    Add-Content -Path $LogFile -Value $line -ErrorAction SilentlyContinue
}

function Get-ActivationStatus {
    try {
        # SoftwareLicensingProduct: LicenseStatus
        # 0 = Unlicensed, 1 = Licensed, 2 = OOBGrace, 3 = OOTGrace
        # 4 = NonGenuineGrace, 5 = Notification, 6 = ExtendedGrace
        $product = Get-CimInstance -ClassName SoftwareLicensingProduct -ErrorAction Stop |
            Where-Object { $_.PartialProductKey -and $_.Name -like "Windows*" } |
            Select-Object -First 1

        if (-not $product) {
            return @{ Status = -1; Text = "Kein Windows-Lizenzprodukt gefunden"; Licensed = $false }
        }

        $statusText = switch ($product.LicenseStatus) {
            0 { "Nicht lizenziert" }
            1 { "Lizenziert (aktiviert)" }
            2 { "OOB Grace Period" }
            3 { "OOT Grace Period" }
            4 { "Non-Genuine Grace Period" }
            5 { "Benachrichtigung (Notification)" }
            6 { "Extended Grace Period" }
            default { "Unbekannt ($($product.LicenseStatus))" }
        }

        $graceRemaining = ""
        if ($product.LicenseStatus -ne 1 -and $product.GracePeriodRemaining -gt 0) {
            $days = [math]::Floor($product.GracePeriodRemaining / 1440)
            $graceRemaining = " ($days Tage verbleibend)"
        }

        return @{
            Status    = $product.LicenseStatus
            Text      = $statusText + $graceRemaining
            Licensed  = ($product.LicenseStatus -eq 1)
            Name      = $product.Name
            ProductKey = "...-" + $product.PartialProductKey
        }
    }
    catch {
        return @{ Status = -1; Text = "Fehler: $($_.Exception.Message)"; Licensed = $false }
    }
}

function Send-AlertMail($activation) {
    $hostname = $env:COMPUTERNAME
    $subject = "WARNUNG: Windows-Aktivierung verloren auf $hostname"
    $body = @"
Windows-Aktivierungsproblem auf Server $hostname
=====================================================

Hostname:    $hostname
Zeitpunkt:   $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Status:      $($activation.Text)
Produkt:     $($activation.Name)
Schluessel:  $($activation.ProductKey)

Bitte Aktivierung pruefen:
  slmgr /xpr
  slmgr /ato

-- 
Check-WindowsActivation.ps1 v1.0.0 | netz24.at
"@

    try {
        Send-MailMessage -From $MailFrom -To $MailTo -Subject $subject `
            -Body $body -SmtpServer $SmtpServer -Port $SmtpPort -ErrorAction Stop
        Write-Log "Alert-Mail gesendet an $MailTo"
        return $true
    }
    catch {
        Write-Log "FEHLER beim Mailversand: $($_.Exception.Message)"
        return $false
    }
}

# ============================================================
#  HAUPTPROGRAMM
# ============================================================

Write-Log "=== Aktivierungs-Check gestartet ==="

$activation = Get-ActivationStatus

Write-Log "Status: $($activation.Text)"

if ($activation.Licensed) {
    Write-Log "OK - Windows ist aktiviert"
}
else {
    Write-Log "WARNUNG - Aktivierung verloren!"
    Send-AlertMail $activation
}

Write-Log "=== Check beendet ==="