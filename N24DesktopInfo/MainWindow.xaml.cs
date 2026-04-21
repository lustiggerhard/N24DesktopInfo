using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace N24DesktopInfo
{
    public partial class MainWindow : Window
    {
        private const string N24_VERSION = "1.6.1";
        private const string N24_DATE = "2026-04-21";

        private AppConfig _config = null!;
        private SystemInfoService _infoService = null!;
        private DispatcherTimer _timer = null!;
        private FileSystemWatcher? _configWatcher;
        private DispatcherTimer? _reloadDebounce;

        // Brushes
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private SolidColorBrush _accentBrush = null!, _labelBrush = null!, _valueBrush = null!;
        private SolidColorBrush _warningBrush = null!, _criticalBrush = null!, _separatorBrush = null!;
        private SolidColorBrush _titleBrush = null!, _barFillBrush = null!, _barEmptyBrush = null!, _barBorderBrush = null!;
        private SolidColorBrush _chartInBrush = null!, _chartOutBrush = null!, _chartGridBrush = null!;
        private SolidColorBrush _chartBorderBrush = null!, _chartLabelBrush = null!;
        private FontFamily _monoFont = null!;

        // Layout (all pixel-based)
        private double _labelWidthPx, _valueWidthPx, _barHeight, _barPixelWidth, _contentWidthPx;

        // Build-Once UI refs
        private Run? _rUptime, _rRamInfo, _rRamPercent, _rExternIp;
        private Run? _rCpuLoadPercent, _rTrafficIn, _rTrafficOut;
        private Rectangle? _rectCpuFill, _rectRamFill;
        private int _lastDiskCount = -1, _lastAdapterCount = -1;
        private readonly List<Run> _diskValueRuns = new(), _diskPercentRuns = new();
        private readonly List<Rectangle> _diskFillRects = new();
        private readonly List<Run> _netValueRuns = new();
        private Run? _rFooterVersion;
        private bool _needsRebuild = true;

        // Chart
        private Polyline? _chartLineIn, _chartLineOut;
        private TextBlock? _chartLabelIn, _chartLabelOut, _chartLabelScale;
        private readonly List<TrafficSample> _trafficBuffer = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            ApplyTheme();
            InitTrayIcon();
            InitConfigWatcher();
            _infoService = new SystemInfoService(_config);
            UpdateDisplay();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_config.Refresh.IntervalSeconds) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            Loaded += OnLoaded;
        }

        // ========================================================
        //  CONFIG & THEME
        // ========================================================

        private void LoadConfig()
        {
            _config = AppConfig.Load();
            Width = _config.Display.Width;
        }

        private void ApplyTheme()
        {
            _monoFont = new FontFamily(_config.Display.FontFamily);
            double fs = _config.Display.FontSize;

            _accentBrush    = Brush(_config.Display.AccentColor);
            _labelBrush     = Brush(_config.Display.LabelColor);
            _valueBrush     = Brush(_config.Display.ValueColor);
            _warningBrush   = Brush(_config.Display.WarningColor);
            _criticalBrush  = Brush(_config.Display.CriticalColor);
            _separatorBrush = Brush(_config.Display.SeparatorColor);
            _titleBrush     = Brush(_config.Display.TitleColor);
            _barFillBrush   = Brush(_config.Display.BarFillColor);
            _barEmptyBrush  = Brush(_config.Display.BarEmptyColor);
            _barBorderBrush = Brush(_config.Display.BarBorderColor);
            _chartInBrush     = Brush(_config.Traffic.InColor);
            _chartOutBrush    = Brush(_config.Traffic.OutColor);
            _chartGridBrush   = Brush(_config.Traffic.GridColor);
            _chartBorderBrush = Brush(_config.Traffic.BorderColor);
            _chartLabelBrush  = Brush(_config.Traffic.LabelColor);

            BgBrush.Color = Clr(_config.Display.BackgroundColor);
            BgBrush.Opacity = _config.Display.BackgroundOpacity;
            MainBorder.CornerRadius = new CornerRadius(_config.Display.CornerRadius);
            MainBorder.Padding = new Thickness(_config.Display.Padding);
            TitleSeparator.Fill = _separatorBrush;
            FooterSeparator.Fill = _separatorBrush;
            TitleText.FontFamily = _monoFont; TitleText.FontSize = fs + 1;
            TitleText.Foreground = _titleBrush; TitleText.Text = "⚡ N24 Desktop Info";
            InfoText.FontFamily = _monoFont; InfoText.FontSize = fs;
            FooterText.FontFamily = _monoFont; FooterText.FontSize = fs - 1;

            _barHeight = _config.Display.BarHeight;
            _labelWidthPx = _config.Display.LabelWidth;
            _valueWidthPx = _config.Display.ValueWidth;
            _contentWidthPx = _config.Display.Width - _config.Display.Padding * 2 - 4;

            double pctTextPx = 40; // space for " 81%" text
            _barPixelWidth = Math.Max(50, _contentWidthPx - _labelWidthPx - pctTextPx);

            ChartBorder.Width = _contentWidthPx; ChartBorder.Height = _config.Traffic.ChartHeight;
            ChartBorder.BorderBrush = _chartBorderBrush;
            ChartCanvas.Width = _contentWidthPx - 2; ChartCanvas.Height = _config.Traffic.ChartHeight - 2;
        }

        private static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush(Clr(hex)); b.Freeze(); return b; }
        private static Color Clr(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        // ========================================================
        //  CONFIG FILE WATCHER
        // ========================================================

        private void InitConfigWatcher()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(AppConfig.GetConfigPath()) ?? "";
                string file = System.IO.Path.GetFileName(AppConfig.GetConfigPath());
                _configWatcher = new FileSystemWatcher(dir, file)
                    { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
                _configWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(() => { _reloadDebounce?.Stop(); _reloadDebounce?.Start(); });
                _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); DoReloadConfig(); };
            }
            catch { }
        }

        private void DoReloadConfig()
        {
            try
            {
                LoadConfig(); ApplyTheme();
                _needsRebuild = true; _lastDiskCount = -1; _lastAdapterCount = -1;
                _timer.Interval = TimeSpan.FromSeconds(_config.Refresh.IntervalSeconds);
                _infoService?.Dispose(); _infoService = new SystemInfoService(_config);
                UpdateDisplay(); PositionWindow();
            }
            catch { }
        }

        // ========================================================
        //  WINDOW OVERLAY
        // ========================================================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)?.AddHook(WndProc);
            int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            ex &= ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE, ex);
            SendToBottom(); PositionWindow();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
            {
                var p = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                p.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                p.flags |= NativeMethods.SWP_NOACTIVATE;
                Marshal.StructureToPtr(p, lParam, true);
            }
            else if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_SETTINGCHANGE) PositionWindow();
            return IntPtr.Zero;
        }

        private void SendToBottom()
        {
            var h = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(h, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING);
        }

        private void PositionWindow()
        {
            var p = _config.Position;
            if (p.X >= 0 && p.Y >= 0) { Left = p.X; Top = p.Y; return; }
            var wa = SystemParameters.WorkArea;
            switch (p.Anchor?.ToLower())
            {
                case "topleft":     Left = wa.Left + p.MarginLeft;                   Top = wa.Top + p.MarginTop; break;
                case "topright":    Left = wa.Right - ActualWidth - p.MarginRight;    Top = wa.Top + p.MarginTop; break;
                case "bottomleft":  Left = wa.Left + p.MarginLeft;                   Top = wa.Bottom - ActualHeight - p.MarginBottom; break;
                default:            Left = wa.Right - ActualWidth - p.MarginRight;    Top = wa.Bottom - ActualHeight - p.MarginBottom; break;
            }
        }

        // ========================================================
        //  TRAY ICON
        // ========================================================

        private void InitTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
                { Icon = IconHelper.CreateTrayIcon(), Text = "N24 Desktop Info", Visible = true };
            var m = new System.Windows.Forms.ContextMenuStrip();
            m.Items.Add("N24 Desktop Info v" + N24_VERSION).Enabled = false;
            m.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            m.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Sichtbar", null, (_, _) => ToggleVisibility()) { Checked = true });
            m.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Click-Through", null,
                (s, _) => { var i = (System.Windows.Forms.ToolStripMenuItem)s!; i.Checked = !i.Checked; SetClickThrough(i.Checked); }) { Checked = true });
            m.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            m.Items.Add("Config öffnen", null, (_, _) => { try { System.Diagnostics.Process.Start("notepad.exe", AppConfig.GetConfigPath()); } catch { } });
            m.Items.Add("Position zurücksetzen", null, (_, _) => PositionWindow());
            m.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            m.Items.Add("Beenden", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = m;
            _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
        }

        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
            { Visibility = Visibility.Hidden; if (_trayIcon?.ContextMenuStrip?.Items[2] is System.Windows.Forms.ToolStripMenuItem i) i.Checked = false; }
            else
            { Visibility = Visibility.Visible; SendToBottom(); if (_trayIcon?.ContextMenuStrip?.Items[2] is System.Windows.Forms.ToolStripMenuItem i) i.Checked = true; }
        }

        private void SetClickThrough(bool on)
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE, on ? (ex | NativeMethods.WS_EX_TRANSPARENT) : (ex & ~NativeMethods.WS_EX_TRANSPARENT));
        }

        private void ExitApp()
        {
            try { _timer?.Stop(); } catch { }
            try { _configWatcher?.Dispose(); } catch { }
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;  // remove from tray immediately
                    _trayIcon.Icon?.Dispose();
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch { }
            try { _infoService?.Dispose(); } catch { }

            Application.Current.Shutdown();

            // Fallback: if Shutdown doesn't kill the process within 2s, force it
            System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                Environment.Exit(0));
        }

        // ========================================================
        //  DISPLAY - Build Once, Update Many
        // ========================================================

        private bool _updating;
        private int _errorCount;

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (_updating) return;
            _updating = true;
            try
            {
                // Collect data on background thread to prevent UI freeze
                // Timeout after 5s to handle WMI/PerformanceCounter hangs
                SystemInfoData? data = null;
                Exception? collectError = null;
                var collectTask = Task.Run(() =>
                {
                    try { return _infoService.Collect(); }
                    catch (Exception ex) { collectError = ex; return null; }
                });

                var completed = await Task.WhenAny(collectTask, Task.Delay(5000));
                if (completed != collectTask)
                {
                    _errorCount++;
                    if (_errorCount <= 3)
                        SystemInfoService.WriteDebugLog($"Collect TIMEOUT #{_errorCount}: took >5s, skipping");
                    return;
                }

                data = collectTask.Result;
                if (data == null)
                {
                    _errorCount++;
                    if (_errorCount <= 3 && collectError != null)
                        SystemInfoService.WriteDebugLog($"Collect ERROR #{_errorCount}: {collectError.Message}");
                    return;
                }
                _errorCount = 0;

                // UI update on dispatcher thread
                bool structChanged = data.Disks.Count != _lastDiskCount || data.NetworkAdapters.Count != _lastAdapterCount;
                if (_needsRebuild || structChanged)
                {
                    BuildLayout(data);
                    _needsRebuild = false;
                    _lastDiskCount = data.Disks.Count; _lastAdapterCount = data.NetworkAdapters.Count;
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PositionWindow);
                }
                else RefreshValues(data);

                if (_config.Sections.ShowTrafficChart) UpdateTrafficChart(data);
            }
            catch (Exception ex)
            {
                _errorCount++;
                if (_errorCount <= 3)
                    SystemInfoService.WriteDebugLog($"UpdateDisplay ERROR #{_errorCount}: {ex.Message}");
            }
            finally
            {
                _updating = false;
            }
        }

        private void UpdateDisplay()
        {
            var data = _infoService.Collect();
            bool structChanged = data.Disks.Count != _lastDiskCount || data.NetworkAdapters.Count != _lastAdapterCount;

            if (_needsRebuild || structChanged)
            {
                BuildLayout(data);
                _needsRebuild = false;
                _lastDiskCount = data.Disks.Count; _lastAdapterCount = data.NetworkAdapters.Count;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PositionWindow);
            }
            else RefreshValues(data);

            if (_config.Sections.ShowTrafficChart) UpdateTrafficChart(data);
        }

        private void BuildLayout(SystemInfoData data)
        {
            InfoText.Inlines.Clear();
            _diskValueRuns.Clear(); _diskPercentRuns.Clear(); _diskFillRects.Clear(); _netValueRuns.Clear();
            _rTrafficIn = null; _rTrafficOut = null;

            var sec = _config.Sections;

            if (sec.ShowHostname) StaticLine("HOSTNAME", data.Hostname);
            if (sec.ShowOS) { StaticLine("OS", data.OsName); if (!string.IsNullOrEmpty(data.OsBuild)) StaticLine("BUILD", data.OsBuild); }
            if (sec.ShowUptime) { Label("UPTIME"); _rUptime = DynVal(FormatUptime(data.Uptime)); }

            if (sec.ShowCPU)
            {
                Sep(); StaticLine("CPU", data.CpuName);
                if (data.CpuCores > 0) StaticLine("CORES", $"{data.CpuCores}C / {Environment.ProcessorCount}T");
                Label("LOAD"); var cb = PctBrush(data.CpuUsagePercent, 75, 90);
                _rectCpuFill = Bar(data.CpuUsagePercent, cb); _rCpuLoadPercent = DynPct(data.CpuUsagePercent, cb);
            }

            if (sec.ShowRAM)
            {
                Sep(); Label("MEMORY"); _rRamInfo = DynVal($"{FmtB(data.RamUsedBytes)} / {FmtB(data.RamTotalBytes)}");
                Label("USAGE"); var rb = PctBrush(data.RamUsedPercent, 75, 90);
                _rectRamFill = Bar(data.RamUsedPercent, rb); _rRamPercent = DynPct(data.RamUsedPercent, rb);
            }

            if (sec.ShowDisks && data.Disks.Count > 0)
            {
                Sep(); BoldLabel("DISKS");
                foreach (var d in data.Disks)
                {
                    long u = d.TotalBytes - d.FreeBytes;
                    var db = PctBrush(d.UsedPercent, _config.Disk.WarningPercent, _config.Disk.CriticalPercent);
                    Label($"  {d.Name}"); _diskValueRuns.Add(DynVal($"{FmtB(u)} / {FmtB(d.TotalBytes)}"));
                    LabelSpacer();
                    _diskFillRects.Add(Bar(d.UsedPercent, db)); _diskPercentRuns.Add(DynPct(d.UsedPercent, db));
                }
            }

            if (sec.ShowNetwork && data.NetworkAdapters.Count > 0)
            {
                Sep(); BoldLabel("NETWORK");
                foreach (var a in data.NetworkAdapters)
                {
                    string sp = string.IsNullOrEmpty(a.Speed) ? "" : $" ({a.Speed})";
                    Label($"  {a.Name}"); _netValueRuns.Add(DynVal($"{a.IpAddress}{sp}"));
                }
            }

            if (sec.ShowExternalIP)
            {
                if (!sec.ShowNetwork || data.NetworkAdapters.Count == 0) Sep();
                Label("  EXTERN"); _rExternIp = DynVal(data.ExternalIp);
            }

            // Traffic text (independent of chart)
            if (sec.ShowTrafficText)
            {
                Sep();
                Label("TRAFFIC IN");  _rTrafficIn = DynVal(FmtRate(data.TrafficInBps));
                Label("TRAFFIC OUT"); _rTrafficOut = DynVal(FmtRate(data.TrafficOutBps));
            }

            // Traffic chart (independent of text)
            ChartBorder.Visibility = sec.ShowTrafficChart ? Visibility.Visible : Visibility.Collapsed;
            if (sec.ShowTrafficChart) BuildChart();

            BuildFooter();
        }

        private void RefreshValues(SystemInfoData data)
        {
            var sec = _config.Sections;

            if (sec.ShowUptime && _rUptime != null)
                _rUptime.Text = FormatUptime(data.Uptime) + "\n";

            if (sec.ShowCPU && _rectCpuFill != null)
            {
                var b = PctBrush(data.CpuUsagePercent, 75, 90);
                _rectCpuFill.Fill = b; _rectCpuFill.Width = Math.Max(0, _barPixelWidth * Math.Clamp(data.CpuUsagePercent, 0, 100) / 100.0);
                if (_rCpuLoadPercent != null) { _rCpuLoadPercent.Text = $" {data.CpuUsagePercent,3:F0}%\n"; _rCpuLoadPercent.Foreground = b; }
            }

            if (sec.ShowRAM)
            {
                if (_rRamInfo != null) _rRamInfo.Text = $"{FmtB(data.RamUsedBytes)} / {FmtB(data.RamTotalBytes)}" + "\n";
                if (_rectRamFill != null)
                {
                    var b = PctBrush(data.RamUsedPercent, 75, 90);
                    _rectRamFill.Fill = b; _rectRamFill.Width = Math.Max(0, _barPixelWidth * Math.Clamp(data.RamUsedPercent, 0, 100) / 100.0);
                    if (_rRamPercent != null) { _rRamPercent.Text = $" {data.RamUsedPercent,3:F0}%\n"; _rRamPercent.Foreground = b; }
                }
            }

            if (sec.ShowDisks)
                for (int i = 0; i < data.Disks.Count && i < _diskValueRuns.Count; i++)
                {
                    var d = data.Disks[i]; long u = d.TotalBytes - d.FreeBytes;
                    var db = PctBrush(d.UsedPercent, _config.Disk.WarningPercent, _config.Disk.CriticalPercent);
                    _diskValueRuns[i].Text = $"{FmtB(u)} / {FmtB(d.TotalBytes)}" + "\n";
                    if (i < _diskFillRects.Count) { _diskFillRects[i].Fill = db; _diskFillRects[i].Width = Math.Max(0, _barPixelWidth * Math.Clamp(d.UsedPercent, 0, 100) / 100.0); }
                    if (i < _diskPercentRuns.Count) { _diskPercentRuns[i].Text = $" {d.UsedPercent,3:F0}%\n"; _diskPercentRuns[i].Foreground = db; }
                }

            if (sec.ShowNetwork)
                for (int i = 0; i < data.NetworkAdapters.Count && i < _netValueRuns.Count; i++)
                {
                    var a = data.NetworkAdapters[i]; string sp = string.IsNullOrEmpty(a.Speed) ? "" : $" ({a.Speed})";
                    _netValueRuns[i].Text = $"{a.IpAddress}{sp}" + "\n";
                }

            if (sec.ShowExternalIP && _rExternIp != null)
                _rExternIp.Text = data.ExternalIp + "\n";

            if (sec.ShowTrafficText)
            {
                if (_rTrafficIn != null) _rTrafficIn.Text = FmtRate(data.TrafficInBps) + "\n";
                if (_rTrafficOut != null) _rTrafficOut.Text = FmtRate(data.TrafficOutBps) + "\n";
            }
        }

        // ========================================================
        //  TRAFFIC CHART
        // ========================================================

        private void BuildChart()
        {
            ChartCanvas.Children.Clear(); ChartLegend.Children.Clear();
            double w = ChartCanvas.Width, h = ChartCanvas.Height;

            for (int i = 1; i <= 3; i++)
            {
                double y = h * i / 4.0;
                ChartCanvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y,
                    Stroke = _chartGridBrush, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 4, 4 } });
            }

            _chartLineOut = new Polyline { Stroke = _chartOutBrush, StrokeThickness = _config.Traffic.LineThickness, StrokeLineJoin = PenLineJoin.Round };
            _chartLineIn = new Polyline { Stroke = _chartInBrush, StrokeThickness = _config.Traffic.LineThickness, StrokeLineJoin = PenLineJoin.Round };
            ChartCanvas.Children.Add(_chartLineOut); ChartCanvas.Children.Add(_chartLineIn);

            _chartLabelScale = new TextBlock { Foreground = _chartLabelBrush, FontFamily = _monoFont, FontSize = _config.Display.FontSize - 2.5, Margin = new Thickness(4, 2, 0, 0) };
            ChartLegend.Children.Add(_chartLabelScale);

            AddLegendItem(_chartInBrush, "IN: ---", out _chartLabelIn);
            AddLegendItem(_chartOutBrush, "OUT: ---", out _chartLabelOut);
        }

        private void AddLegendItem(SolidColorBrush color, string text, out TextBlock label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            sp.Children.Add(new Rectangle { Width = 10, Height = 3, Fill = color, Margin = new Thickness(0, 4, 4, 0) });
            label = new TextBlock { Foreground = color, FontFamily = _monoFont, FontSize = _config.Display.FontSize - 2.5, Text = text };
            sp.Children.Add(label);
            ChartLegend.Children.Add(sp);
        }

        private void UpdateTrafficChart(SystemInfoData data)
        {
            if (_chartLineIn == null || _chartLineOut == null) return;
            _infoService.GetTrafficHistory(_trafficBuffer);
            double w = ChartCanvas.Width, h = ChartCanvas.Height;
            int max = _config.Traffic.ChartSeconds;

            double maxVal = 1024;
            foreach (var s in _trafficBuffer) { if (s.InBytesPerSec > maxVal) maxVal = s.InBytesPerSec; if (s.OutBytesPerSec > maxVal) maxVal = s.OutBytesPerSec; }
            maxVal *= 1.1;

            int cnt = _trafficBuffer.Count, ofs = max - cnt;
            var ip = new PointCollection(cnt); var op = new PointCollection(cnt);
            for (int i = 0; i < cnt; i++)
            {
                double x = (ofs + i) * w / max;
                ip.Add(new Point(x, Math.Clamp(h - _trafficBuffer[i].InBytesPerSec / maxVal * h, 0, h)));
                op.Add(new Point(x, Math.Clamp(h - _trafficBuffer[i].OutBytesPerSec / maxVal * h, 0, h)));
            }
            _chartLineIn.Points = ip; _chartLineOut.Points = op;
            if (_chartLabelIn != null) _chartLabelIn.Text = $"IN: {FmtRate(data.TrafficInBps)}";
            if (_chartLabelOut != null) _chartLabelOut.Text = $"OUT: {FmtRate(data.TrafficOutBps)}";
            if (_chartLabelScale != null) _chartLabelScale.Text = $"max: {FmtRate(maxVal)}";
        }

        // ========================================================
        //  BUILD HELPERS (pixel-based)
        // ========================================================

        private void StaticLine(string lbl, string val) { LabelBox(lbl); Raw(val + "\n", _valueBrush); }
        private void Label(string lbl) { LabelBox(lbl); }
        private void LabelBox(string lbl)
        {
            var tb = new TextBlock { Text = lbl, Width = _labelWidthPx, FontFamily = _monoFont,
                FontSize = _config.Display.FontSize, Foreground = _labelBrush, TextTrimming = TextTrimming.CharacterEllipsis };
            InfoText.Inlines.Add(new InlineUIContainer(tb) { BaselineAlignment = BaselineAlignment.Center });
        }
        private void LabelSpacer()
        {
            var tb = new TextBlock { Text = "", Width = _labelWidthPx };
            InfoText.Inlines.Add(new InlineUIContainer(tb) { BaselineAlignment = BaselineAlignment.Center });
        }
        private void Sep()
        {
            var line = new System.Windows.Shapes.Rectangle { Width = _contentWidthPx, Height = 1, Fill = _separatorBrush };
            InfoText.Inlines.Add(new InlineUIContainer(line) { BaselineAlignment = BaselineAlignment.Center });
            Raw("\n", _separatorBrush);
        }
        private void BoldLabel(string t) { InfoText.Inlines.Add(new Run(t + "\n") { Foreground = _accentBrush, FontFamily = _monoFont, FontSize = _config.Display.FontSize, FontWeight = FontWeights.Bold }); }
        private void Raw(string t, SolidColorBrush b) { InfoText.Inlines.Add(new Run(t) { Foreground = b, FontFamily = _monoFont, FontSize = _config.Display.FontSize }); }

        private Run DynVal(string v)
        { var r = new Run(v + "\n") { Foreground = _valueBrush, FontFamily = _monoFont, FontSize = _config.Display.FontSize }; InfoText.Inlines.Add(r); return r; }

        private Run DynPct(double p, SolidColorBrush b)
        { var r = new Run($" {p,3:F0}%\n") { Foreground = b, FontFamily = _monoFont, FontSize = _config.Display.FontSize }; InfoText.Inlines.Add(r); return r; }

        private Rectangle Bar(double pct, SolidColorBrush fill)
        {
            double fw = Math.Max(0, _barPixelWidth * Math.Clamp(pct, 0, 100) / 100.0);
            var bg = new Rectangle { Fill = _barEmptyBrush, HorizontalAlignment = HorizontalAlignment.Stretch };
            var fr = new Rectangle { Fill = fill, Width = fw, HorizontalAlignment = HorizontalAlignment.Left };
            var g = new Grid { Width = _barPixelWidth, Height = _barHeight, ClipToBounds = true };
            g.Children.Add(bg); g.Children.Add(fr);
            InfoText.Inlines.Add(new InlineUIContainer(new Border { BorderBrush = _barBorderBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Width = _barPixelWidth, Height = _barHeight, Child = g })
                { BaselineAlignment = BaselineAlignment.Center });
            return fr;
        }

        private void BuildFooter()
        {
            FooterText.Inlines.Clear();
            if (_config.Sections.ShowVersion)
            {
                _rFooterVersion = new Run($"v{N24_VERSION} ({N24_DATE}) · netz24.at") { Foreground = _separatorBrush, FontFamily = _monoFont, FontSize = _config.Display.FontSize - 2 };
                FooterText.Inlines.Add(_rFooterVersion); FooterSeparator.Visibility = Visibility.Visible;
            }
            else FooterSeparator.Visibility = Visibility.Collapsed;
        }

        // ========================================================
        //  FORMATTING
        // ========================================================

        private SolidColorBrush PctBrush(double p, int w, int c) => p >= c ? _criticalBrush : p >= w ? _warningBrush : _accentBrush;

        private static string FmtB(long b) => b switch
        {
            >= 1L << 40 => $"{b / (double)(1L << 40):F1} TB", >= 1L << 30 => $"{b / (double)(1L << 30):F1} GB",
            >= 1L << 20 => $"{b / (double)(1L << 20):F1} MB", >= 1L << 10 => $"{b / (double)(1L << 10):F1} KB", _ => $"{b} B"
        };
        private static string FmtB(ulong b) => FmtB((long)b);

        private string FormatUptime(TimeSpan ts)
        {
            string tage = ts.Days == 1 ? "Tag" : "Tage";
            return _config.Uptime.Format
                .Replace("{D}", ts.Days.ToString()).Replace("{TAGE}", tage)
                .Replace("{HH}", ts.Hours.ToString("D2")).Replace("{H}", ts.Hours.ToString())
                .Replace("{MM}", ts.Minutes.ToString("D2")).Replace("{M}", ts.Minutes.ToString())
                .Replace("{SS}", ts.Seconds.ToString("D2")).Replace("{S}", ts.Seconds.ToString());
        }

        private string FmtRate(double bps) => _config.Traffic.Unit.ToLowerInvariant() switch
        {
            "bps"  => $"{bps:F0} Bps",
            "kbps" => $"{bps / 1024:F1} KBps",
            "mbps" => $"{bps / (1024 * 1024):F2} MBps",
            "gbps" => $"{bps / (1024.0 * 1024 * 1024):F3} GBps",
            "kbit" => $"{bps * 8 / 1000:F1} Kbit/s",
            "mbit" => $"{bps * 8 / 1_000_000:F2} Mbit/s",
            "gbit" => $"{bps * 8 / 1_000_000_000:F3} Gbit/s",
            _      => bps switch { >= 1024 * 1024 * 1024 => $"{bps / (1024.0 * 1024 * 1024):F2} GBps",
                >= 1024 * 1024 => $"{bps / (1024.0 * 1024):F1} MBps", >= 1024 => $"{bps / 1024.0:F1} KBps", _ => $"{bps:F0} Bps" }
        };

        // ========================================================
        //  CLEANUP
        // ========================================================

        protected override void OnClosed(EventArgs e)
        {
            try { _timer?.Stop(); } catch { }
            try { _configWatcher?.Dispose(); } catch { }
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Icon?.Dispose();
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch { }
            try { _infoService?.Dispose(); } catch { }
            base.OnClosed(e);
        }
    }
}
