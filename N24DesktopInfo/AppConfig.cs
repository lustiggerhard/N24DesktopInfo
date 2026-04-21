using System;
using System.IO;
using System.Text.Json;

namespace N24DesktopInfo
{
    public class AppConfig
    {
        public PositionConfig Position { get; set; } = new();
        public DisplayConfig Display { get; set; } = new();
        public RefreshConfig Refresh { get; set; } = new();
        public SectionsConfig Sections { get; set; } = new();
        public NetworkConfig Network { get; set; } = new();
        public TrafficConfig Traffic { get; set; } = new();
        public DiskConfig Disk { get; set; } = new();
        public UptimeConfig Uptime { get; set; } = new();

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static string GetConfigPath() => ConfigPath;

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    return JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
                }
            }
            catch { }
            return new AppConfig();
        }
    }

    public class PositionConfig
    {
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public string Anchor { get; set; } = "BottomRight";
        public int MarginTop { get; set; } = 30;
        public int MarginRight { get; set; } = 30;
        public int MarginBottom { get; set; } = 60;
        public int MarginLeft { get; set; } = 30;
    }

    public class DisplayConfig
    {
        public string FontFamily { get; set; } = "Consolas";
        public double FontSize { get; set; } = 12.5;
        public double BackgroundOpacity { get; set; } = 0.72;
        public string BackgroundColor { get; set; } = "#1a1a2e";
        public string AccentColor { get; set; } = "#00d4aa";
        public string LabelColor { get; set; } = "#8892b0";
        public string ValueColor { get; set; } = "#e6f1ff";
        public string WarningColor { get; set; } = "#ffb347";
        public string CriticalColor { get; set; } = "#ff6b6b";
        public string SeparatorColor { get; set; } = "#2d3561";
        public string TitleColor { get; set; } = "#00d4aa";
        public string BarFillColor { get; set; } = "#00d4aa";
        public string BarEmptyColor { get; set; } = "#2d3561";
        public string BarBorderColor { get; set; } = "#4a5080";
        public double BarHeight { get; set; } = 12;
        public int LabelWidth { get; set; } = 90;
        public int ValueWidth { get; set; } = 210;
        public int CornerRadius { get; set; } = 8;
        public int Padding { get; set; } = 18;
        public int Width { get; set; } = 400;
    }

    public class RefreshConfig
    {
        public int IntervalSeconds { get; set; } = 1;
        public int ExternalIpIntervalMinutes { get; set; } = 10;
    }

    public class SectionsConfig
    {
        public bool ShowHostname { get; set; } = true;
        public bool ShowOS { get; set; } = true;
        public bool ShowUptime { get; set; } = true;
        public bool ShowCPU { get; set; } = true;
        public bool ShowRAM { get; set; } = true;
        public bool ShowDisks { get; set; } = true;
        public bool ShowNetwork { get; set; } = true;
        public bool ShowExternalIP { get; set; } = true;
        public bool ShowTrafficText { get; set; } = true;
        public bool ShowTrafficChart { get; set; } = true;
        public bool ShowVersion { get; set; } = true;
    }

    public class NetworkConfig
    {
        public string ExternalIpUrl { get; set; } = "https://api.ipify.org";
        public string[] IgnoreAdapters { get; set; } = new[]
            { "Loopback", "vEthernet", "VMware", "VirtualBox", "Hyper-V" };
    }

    public class TrafficConfig
    {
        public string InColor { get; set; } = "#00d4aa";
        public string OutColor { get; set; } = "#ff6b6b";
        public string GridColor { get; set; } = "#2d3561";
        public string BorderColor { get; set; } = "#4a5080";
        public string LabelColor { get; set; } = "#8892b0";
        public int ChartHeight { get; set; } = 60;
        public int ChartSeconds { get; set; } = 60;
        public double LineThickness { get; set; } = 1.5;
        /// <summary>
        /// Unit: "auto", "Bps", "KBps", "MBps", "GBps", "Kbit", "Mbit", "Gbit"
        /// </summary>
        public string Unit { get; set; } = "auto";
        /// <summary>Write n24debug.log with adapter details on start.</summary>
        public bool EnableDebugLog { get; set; } = false;
    }

    public class DiskConfig
    {
        public int WarningPercent { get; set; } = 80;
        public int CriticalPercent { get; set; } = 95;
        public bool OnlyFixedDrives { get; set; } = true;
    }

    public class UptimeConfig
    {
        /// <summary>
        /// Tokens: {D}, {TAGE}, {HH}, {H}, {MM}, {M}, {SS}, {S}
        /// </summary>
        public string Format { get; set; } = "{D} {TAGE}, {HH}:{MM}:{SS}";
    }
}
