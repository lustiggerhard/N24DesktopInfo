# N24 Desktop Info v1.0.0

**DesktopInfo-Ersatz für Windows 10/11** – Transparentes, click-through Overlay-Fenster mit Systeminformationen.

## Features

- **Transparent & Click-Through**: Fenster liegt am Desktop-Hintergrund, Mausklicks gehen durch
- **Always on Bottom**: Bleibt unter allen Fenstern, direkt über dem Desktop
- **System Tray Icon**: Rechtsklick für Menü (Sichtbarkeit, Click-Through Toggle, Config laden, Beenden)
- **Konfigurierbar**: Alle Farben, Schriftarten, Position, Sektionen über `appsettings.json`
- **Single Instance**: Nur eine Instanz gleichzeitig möglich
- **DPI-Aware**: Korrekte Darstellung auf HiDPI-Displays

## Angezeigte Informationen

| Sektion   | Details                                 |
|-----------|-----------------------------------------|
| Hostname  | Computername                            |
| OS        | Windows Edition + Build-Nummer          |
| Uptime    | Betriebszeit seit letztem Start         |
| CPU       | Prozessor-Name, Cores/Threads, Auslastung mit Balkenanzeige |
| RAM       | Verwendet/Gesamt, Auslastung mit Balkenanzeige |
| Disks     | Pro Laufwerk: Verwendet/Gesamt mit farbiger Balkenanzeige |
| Network   | Alle aktiven Adapter mit IP und Geschwindigkeit |
| Extern IP | Externe IP-Adresse (via api.ipify.org)  |

## Voraussetzungen

- **Windows 10/11**
- **.NET 8 SDK** zum Kompilieren: https://dotnet.microsoft.com/download/dotnet/8.0

## Build

```cmd
build.bat
```

Output: `.\publish\N24DesktopInfo.exe` (Self-Contained, keine .NET Runtime nötig)

## Autostart

```cmd
autostart.bat          REM Autostart einrichten
autostart.bat remove   REM Autostart entfernen
```

## Konfiguration

Datei: `appsettings.json` im selben Ordner wie die EXE.

### Position

```json
"Position": {
    "X": -1,           // -1 = automatisch nach Anchor
    "Y": -1,
    "Anchor": "BottomRight",  // TopLeft, TopRight, BottomLeft, BottomRight
    "MarginRight": 30,
    "MarginBottom": 60
}
```

### Farben (Dark Theme Defaults)

```json
"Display": {
    "FontFamily": "Consolas",
    "FontSize": 12.5,
    "BackgroundOpacity": 0.72,
    "BackgroundColor": "#1a1a2e",
    "AccentColor": "#00d4aa",
    "LabelColor": "#8892b0",
    "ValueColor": "#e6f1ff",
    "WarningColor": "#ffb347",
    "CriticalColor": "#ff6b6b"
}
```

### Sektionen ein/ausschalten

```json
"Sections": {
    "ShowHostname": true,
    "ShowOS": true,
    "ShowUptime": true,
    "ShowCPU": true,
    "ShowRAM": true,
    "ShowDisks": true,
    "ShowNetwork": true,
    "ShowExternalIP": true,
    "ShowTimestamp": true,
    "ShowVersion": true
}
```

### Disk-Schwellwerte

```json
"Disk": {
    "WarningPercent": 80,   // Gelb ab 80%
    "CriticalPercent": 95,  // Rot ab 95%
    "OnlyFixedDrives": true
}
```

### Network-Filter

```json
"Network": {
    "ExternalIpUrl": "https://api.ipify.org",
    "IgnoreAdapters": ["Loopback", "vEthernet", "VMware", "VirtualBox", "Hyper-V"]
}
```

## System Tray Menü

| Option               | Beschreibung                              |
|----------------------|-------------------------------------------|
| Sichtbar             | Overlay ein/ausblenden                    |
| Click-Through        | Mausklicks durchlassen an/aus             |
| Config neu laden     | `appsettings.json` neu einlesen           |
| Position zurücksetzen| Fenster auf konfigurierte Position setzen |
| Beenden              | Programm beenden                          |

## Technische Details

- **WPF** (.NET 8, Windows Forms für NotifyIcon)
- **Win32 Interop**: `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`, `HWND_BOTTOM`
- **WMI**: CPU-Name, OS-Info via `System.Management`
- **PerformanceCounter**: CPU-Auslastung
- **Self-Contained Publish**: Keine .NET Runtime Installation nötig

---

*v1.0.0 (2026-02-15) · netz24.at · Gerhard Lustig*
