using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemSweep.UI
{
    /// <summary>统计卡片:标题 + 数值。</summary>
    public class StatCard : Card
    {
        public string Title = "";
        public string Value = "";
        public Color ValueColor = Color.Empty;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            // 固定内边距 16:标签在上、数值在下
            int pad = Ui.Px(16);
            var tRect = new Rectangle(pad, pad, Width - pad * 2, Ui.Px(16));
            TextRenderer.DrawText(g, Title, Theme.F(10f, FontStyle.Regular), tRect, Theme.Sub,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            var vRect = new Rectangle(pad, pad + Ui.Px(16), Width - pad * 2, Height - pad * 2 - Ui.Px(16));
            TextRenderer.DrawText(g, Value, Theme.Num(20f), vRect,
                ValueColor.IsEmpty ? Theme.Text : ValueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>两个页面切换的 Tab 条。</summary>
    public class TabsBar : Ctl
    {
        public string[] Items = new string[0];
        public event EventHandler Changed;
        private int sel;
        private float anim;
        private System.Windows.Forms.Timer t;

        public int Selected
        {
            get { return sel; }
            set
            {
                if (value < 0) value = 0;
                if (Items.Length > 0 && value >= Items.Length) value = Items.Length - 1;
                if (sel == value) return;
                sel = value;
                if (t == null)
                {
                    t = new System.Windows.Forms.Timer();
                    t.Interval = 15;
                    t.Tick += delegate
                    {
                        float d = sel - anim;
                        if (Math.Abs(d) < 0.03f) { anim = sel; t.Stop(); }
                        else anim += d * 0.3f;
                        Invalidate();
                    };
                }
                t.Start();
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }

        public void SetSilent(int v) { sel = v; anim = v; Invalidate(); }

        private float TabWidth(Graphics g, int i)
        {
            Size sz = TextRenderer.MeasureText(g, Items[i], Theme.F(10f, FontStyle.Bold),
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return sz.Width + Ui.Fp(30);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Graphics g = CreateGraphics();
            try
            {
                float x = Ui.Fp(14);
                for (int i = 0; i < Items.Length; i++)
                {
                    float w = TabWidth(g, i);
                    if (e.X >= x && e.X <= x + w) { Selected = i; break; }
                    x += w;
                }
            }
            finally { g.Dispose(); }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Prep(g);
            float x = Ui.Fp(14);
            for (int i = 0; i < Items.Length; i++)
            {
                float w = TabWidth(g, i);
                bool on = i == sel;
                Color fg = on ? Theme.Text : Theme.Sub;
                TextRenderer.DrawText(g, Items[i], Theme.F(10f, on ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle((int)x, 0, (int)w, Height - (int)Ui.Fp(4)), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (on)
                {
                    float indW = w * 0.55f;
                    var bar = new RectangleF(x + (w - indW) / 2f, Height - Ui.Fp(6), indW, Ui.Fp(3));
                    Ui.Fill(g, bar, Ui.Fp(1.5f), Theme.Accent);
                }
                x += w;
            }
        }
    }

    public class MainForm : Form
    {
        private readonly Settings st;
        private readonly bool startMinimized;
        private MemSample sample = new MemSample();
        private bool busy;
        private long lastReleased = -1;
        private ulong sessionReleased;
        private DateTime lastAuto = DateTime.Now;
        private bool allowVisible;
        private bool exiting;

        private TabsBar tabs;
        private IconButton btnLang, btnTheme, btnMin, btnClose;
        private Ctl pageOpt;
        private SettingsPage pageSet;
        private RingGauge gauge;
        private StatCard cTotal, cAvail, cCommit, cCache;
        private TextLabel lblLevel, lblLast, lblStatus, lblTotal, lblChart;
        private Segmented segLevel;
        private FlatButton btnOptimize;
        private Card chartCard;
        private Sparkline spark;
        private Toast toast;

        private NotifyIcon tray;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem miOpen, miOptimize, miPause, miExit;
        private System.Windows.Forms.Timer sampleTimer;

        public Settings St { get { return st; } }

        public ulong SessionReleased { get { return sessionReleased; } }

        public MainForm(Settings settings, bool startMin)
        {
            st = settings;
            startMinimized = startMin;
            I18n.Lang = st.Language;
            Theme.Set(st.ThemeName == "dark");

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = I18n.T("app.name");
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;

            IntPtr h = Handle; // 强制创建句柄,以便读取 DPI
            uint dpi = Native.GetDpiForWindow(h);
            Ui.S = dpi > 0 ? dpi / 96f : 1f;

            ClientSize = new Size(Ui.Px(900), Ui.Px(620));
            MinimumSize = new Size(Ui.Px(780), Ui.Px(540));

            BuildUi();
            ApplyTheme();
            ApplyTexts();

            sample = MemoryEngine.Sample();
            lastReleased = st.LastReleased > 0 ? st.LastReleased : -1;
            UpdateSampleUi();

            sampleTimer = new System.Windows.Forms.Timer();
            sampleTimer.Interval = 1000;
            sampleTimer.Tick += delegate { OnTick(); };
            sampleTimer.Start();

            CreateTray();
            ApplyShape();
            LayoutUi(); // 窗口尺寸在本机可能不发生变化,必须主动布局一次

            if (!startMinimized) lastAuto = DateTime.Now;
        }

        // ---------------- 构建 ----------------
        private void BuildUi()
        {
            tabs = new TabsBar();
            tabs.Items = new string[] { "", "" };
            tabs.Changed += delegate { SwitchPage(tabs.Selected); };
            Controls.Add(tabs);

            btnLang = new IconButton(); btnLang.Kind = Glyph.Lang;
            btnLang.Click += delegate { ToggleLanguage(); };
            btnTheme = new IconButton(); btnTheme.Kind = Glyph.Theme;
            btnTheme.Click += delegate { ToggleTheme(); };
            btnMin = new IconButton(); btnMin.Kind = Glyph.Minimize;
            btnMin.Click += delegate { DoMinimize(); };
            btnClose = new IconButton(); btnClose.Kind = Glyph.Close;
            btnClose.Click += delegate { HideToTray(); };
            Controls.Add(btnLang); Controls.Add(btnTheme); Controls.Add(btnMin); Controls.Add(btnClose);

            pageOpt = new Ctl();
            pageOpt.BackColor = Theme.Bg;
            Controls.Add(pageOpt);

            gauge = new RingGauge();
            gauge.BackColor = Theme.Bg;
            pageOpt.Controls.Add(gauge);

            lblStatus = new TextLabel();
            lblStatus.Size = 9f;
            lblStatus.ForeColor = Theme.Faint;
            lblStatus.BackColor = Theme.Bg;
            lblStatus.Align = ContentAlignment.MiddleCenter;
            pageOpt.Controls.Add(lblStatus);

            cTotal = new StatCard(); cAvail = new StatCard(); cCommit = new StatCard(); cCache = new StatCard();
            cAvail.ValueColor = Theme.Accent;
            foreach (StatCard c in new StatCard[] { cTotal, cAvail, cCommit, cCache })
            {
                c.BackColor = Theme.Bg;
                pageOpt.Controls.Add(c);
            }

            lblLevel = new TextLabel();
            lblLevel.Size = 9.5f;
            lblLevel.ForeColor = Theme.Sub;
            lblLevel.BackColor = Theme.Bg;
            lblLevel.Align = ContentAlignment.MiddleLeft;
            pageOpt.Controls.Add(lblLevel);

            segLevel = new Segmented();
            segLevel.BackColor = Theme.Bg;
            segLevel.Changed += delegate
            {
                st.Level = (OptimizeLevel)segLevel.Selected;
                st.Save();
            };
            pageOpt.Controls.Add(segLevel);

            btnOptimize = new FlatButton();
            btnOptimize.Primary = true;
            btnOptimize.FontPt = 10.5f;
            btnOptimize.Click += delegate { RunOptimize((OptimizeLevel)segLevel.Selected, false); };
            pageOpt.Controls.Add(btnOptimize);

            lblLast = new TextLabel();
            lblLast.Size = 9.5f;
            lblLast.ForeColor = Theme.Sub;
            lblLast.BackColor = Theme.Bg;
            lblLast.Align = ContentAlignment.MiddleRight;
            pageOpt.Controls.Add(lblLast);

            chartCard = new Card();
            chartCard.BackColor = Theme.Bg;
            pageOpt.Controls.Add(chartCard);

            lblChart = new TextLabel();
            lblChart.Size = 9f;
            lblChart.ForeColor = Theme.Sub;
            lblChart.BackColor = Theme.Card;
            chartCard.Controls.Add(lblChart);

            lblTotal = new TextLabel();
            lblTotal.Size = 8.5f;
            lblTotal.ForeColor = Theme.Sub;
            lblTotal.BackColor = Theme.Card;
            lblTotal.Align = ContentAlignment.MiddleRight;
            chartCard.Controls.Add(lblTotal);

            spark = new Sparkline();
            spark.BackColor = Theme.Card;
            chartCard.Controls.Add(spark);

            pageSet = new SettingsPage(this);
            pageSet.Visible = false;
            Controls.Add(pageSet);

            toast = new Toast();
            toast.BackColor = Theme.Bg;
            toast.Visible = false;
            Controls.Add(toast);
        }

        private void SwitchPage(int i)
        {
            pageOpt.Visible = i == 0;
            pageSet.Visible = i == 1;
            if (i == 1) pageSet.RefreshValues();
        }

        // ---------------- 布局 ----------------
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
        }

        private void LayoutUi()
        {
            if (tabs == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int header = Ui.Px(44);
            int tabsH = Ui.Px(44);

            int bs = Ui.Px(30);
            int gap = Ui.Px(4);
            int right = w - Ui.Px(8);
            btnClose.SetBounds(right - bs, (header - bs) / 2, bs, bs);
            btnMin.SetBounds(right - bs * 2 - gap, (header - bs) / 2, bs, bs);
            btnTheme.SetBounds(right - bs * 3 - gap * 2, (header - bs) / 2, bs, bs);
            btnLang.SetBounds(right - bs * 4 - gap * 3, (header - bs) / 2, bs, bs);

            tabs.SetBounds(0, header, w, tabsH);

            int top = header + tabsH;
            int bodyH = h - top;
            pageOpt.SetBounds(0, top, w, bodyH);
            pageSet.SetBounds(0, top, w, bodyH);

            // 优化页内部:左侧 220x220 圆环 + 状态文字,右侧 2x2 统计卡(高 84)
            int pad = Ui.Px(24);
            int pw = w - pad * 2;
            int gaugeSize = Ui.Px(220);
            int gaugeTop = Ui.Px(20);
            gauge.SetBounds(pad, gaugeTop, gaugeSize, gaugeSize);
            lblStatus.SetBounds(pad, gaugeTop + Ui.Px(194), gaugeSize, Ui.Px(24));

            int cx = pad + gaugeSize + Ui.Px(20);
            int cw = w - pad - cx;
            int g = Ui.Px(12);
            int cardW = (cw - g) / 2;
            int cardH = Ui.Px(84);
            int gridH = cardH * 2 + g;
            int gridTop = gaugeTop + (gaugeSize - gridH) / 2;
            cTotal.SetBounds(cx, gridTop, cardW, cardH);
            cAvail.SetBounds(cx + cardW + g, gridTop, cardW, cardH);
            cCommit.SetBounds(cx, gridTop + cardH + g, cardW, cardH);
            cCache.SetBounds(cx + cardW + g, gridTop + cardH + g, cardW, cardH);

            // 档位行:标签宽度按实际文字测量,避免英文被裁切;右侧显示上次释放
            int y = gaugeTop + gaugeSize + Ui.Px(24);
            Size lsz = TextRenderer.MeasureText(I18n.T("level.label"), Theme.F(9.5f, FontStyle.Regular));
            int lblW = Math.Max(Ui.Px(70), lsz.Width + Ui.Px(12));
            int segX = pad + lblW + Ui.Px(8);
            lblLevel.SetBounds(pad, y, lblW, Ui.Px(34));
            segLevel.SetBounds(segX, y, Ui.Px(258), Ui.Px(34));
            int lastX = segX + Ui.Px(258) + Ui.Px(16);
            lblLast.SetBounds(lastX, y, pad + pw - lastX, Ui.Px(34));

            y += Ui.Px(34) + Ui.Px(16);
            btnOptimize.SetBounds(pad, y, pw, Ui.Px(46));

            // 底部折线图:锚定在页面底部,高度自适应并限制在 100–160
            int chartH = bodyH - pad - (y + Ui.Px(46) + Ui.Px(20));
            if (chartH > Ui.Px(160)) chartH = Ui.Px(160);
            if (chartH < Ui.Px(100)) chartH = Ui.Px(100);
            int chartY = bodyH - pad - chartH;
            chartCard.SetBounds(pad, chartY, pw, chartH);
            lblChart.SetBounds(Ui.Px(16), Ui.Px(10), pw - Ui.Px(32), Ui.Px(18));
            spark.SetBounds(Ui.Px(16), Ui.Px(32), pw - Ui.Px(32), Math.Max(Ui.Px(30), chartH - Ui.Px(58)));
            lblTotal.SetBounds(Ui.Px(16), chartH - Ui.Px(26), pw - Ui.Px(32), Ui.Px(20));

            toast.SetBounds((w - Ui.Px(250)) / 2, h - Ui.Px(72), Ui.Px(250), Ui.Px(40));

            pageSet.Relayout();
        }

        // ---------------- 主题 / 语言 ----------------
        private void ApplyTheme()
        {
            BackColor = Theme.Bg;
            pageOpt.BackColor = Theme.Bg;
            cTotal.BackColor = Theme.Bg; cAvail.BackColor = Theme.Bg;
            cCommit.BackColor = Theme.Bg; cCache.BackColor = Theme.Bg;
            gauge.BackColor = Theme.Bg;
            chartCard.BackColor = Theme.Bg;
            lblChart.BackColor = Theme.Card;
            lblTotal.BackColor = Theme.Card;
            spark.BackColor = Theme.Card;
            lblLevel.BackColor = Theme.Bg;
            lblLast.BackColor = Theme.Bg;
            lblStatus.BackColor = Theme.Bg;
            segLevel.BackColor = Theme.Bg;
            btnOptimize.BackColor = Theme.Bg;
            btnLang.BackColor = Theme.Bg; btnTheme.BackColor = Theme.Bg;
            btnMin.BackColor = Theme.Bg; btnClose.BackColor = Theme.Bg;
            tabs.BackColor = Theme.Bg;
            toast.BackColor = Theme.Bg;
            cAvail.ValueColor = Theme.Accent;
            if (pageSet != null) pageSet.ApplyTheme();
            if (trayMenu != null)
            {
                trayMenu.BackColor = Theme.Card;
                trayMenu.ForeColor = Theme.Text;
            }
            Invalidate(true);
        }

        private void ApplyTexts()
        {
            Text = I18n.T("app.name");
            tabs.Items = new string[] { I18n.T("nav.optimize"), I18n.T("nav.settings") };
            tabs.SetSilent(tabs.Selected);
            lblLevel.Text = I18n.T("level.label");
            segLevel.Items = new string[] { I18n.T("level.light"), I18n.T("level.standard"), I18n.T("level.deep") };
            segLevel.SetSilent((int)st.Level);
            btnOptimize.Text = busy ? I18n.T("btn.optimizing") : I18n.T("btn.optimize");
            lblChart.Text = I18n.T("chart.title");
            btnLang.LangText = I18n.IsEn ? "中" : "EN";
            btnLang.Invalidate();
            UpdateLastLabel();
            if (pageSet != null) pageSet.RefreshValues();
            RebuildTrayMenu();
            Invalidate(true);
        }

        public void SetLanguage(string lang)
        {
            st.Language = lang;
            st.Save();
            I18n.Lang = lang;
            Theme.ResetFonts();
            ApplyTexts();
            LayoutUi();
        }

        public void ToggleLanguage()
        {
            SetLanguage(I18n.IsEn ? "zh" : "en");
        }

        public void SetTheme(bool dark)
        {
            st.ThemeName = dark ? "dark" : "light";
            st.Save();
            Theme.Set(dark);
            ApplyTheme();
        }

        public void ToggleTheme() { SetTheme(!Theme.Dark); }

        public void SetAuto(bool on, int interval)
        {
            st.AutoOptimize = on;
            st.AutoInterval = Math.Max(5, Math.Min(360, interval));
            st.Save();
            lastAuto = DateTime.Now;
        }

        public void SetLevel(OptimizeLevel lv)
        {
            st.Level = lv;
            st.Save();
            segLevel.SetSilent((int)lv);
        }

        // ---------------- 采样 ----------------
        private void OnTick()
        {
            var s = MemoryEngine.Sample();
            if (s.TotalPhys > 0) sample = s;
            UpdateSampleUi();

            if (st.AutoOptimize && !busy &&
                (DateTime.Now - lastAuto).TotalMinutes >= st.AutoInterval)
            {
                lastAuto = DateTime.Now;
                RunOptimize(st.Level, true);
            }
        }

        private void UpdateSampleUi()
        {
            ulong total = sample.TotalPhys;
            ulong used = sample.UsedPhys;
            double ratio = total > 0 ? (double)used / total : 0;
            gauge.SetValue(ratio, I18n.T("gauge.ratio", Fmt.Bytes(used), Fmt.Bytes(total)));
            if (ratio >= 0.80) { lblStatus.Text = I18n.T("gauge.status.tight"); lblStatus.ForeColor = Theme.Danger; }
            else if (ratio >= 0.60) { lblStatus.Text = I18n.T("gauge.status.high"); lblStatus.ForeColor = Theme.Warn; }
            else { lblStatus.Text = I18n.T("gauge.status.good"); lblStatus.ForeColor = Theme.Faint; }
            cTotal.Title = I18n.T("stat.total");
            cTotal.Value = Fmt.Bytes(total);
            cAvail.Title = I18n.T("stat.avail");
            cAvail.Value = Fmt.Bytes(sample.AvailPhys);
            cCommit.Title = I18n.T("stat.committed");
            cCommit.Value = sample.CommitLimit > 0
                ? Fmt.Bytes(sample.Committed) + " / " + Fmt.Bytes(sample.CommitLimit)
                : Fmt.Bytes(sample.Committed);
            cCache.Title = I18n.T("stat.cache");
            cCache.Value = Fmt.Bytes(sample.SystemCache);
            lblTotal.Text = I18n.T("chart.avail", Fmt.Bytes(sample.AvailPhys));
            if (total > 0) spark.Push((float)((double)sample.AvailPhys / total));
            UpdateLastLabel();
        }

        private void UpdateLastLabel()
        {
            lblLast.Text = lastReleased < 0 ? I18n.T("last.none") : I18n.T("last.released", Fmt.Bytes3((ulong)lastReleased));
        }

        // ---------------- 优化 ----------------
        public void RunOptimize(OptimizeLevel level, bool silent)
        {
            if (busy) return;
            if (level == OptimizeLevel.Deep && !MemoryEngine.IsAdmin && !silent)
            {
                bool go = DialogBox.Ask(this, I18n.T("dialog.admin.title"), I18n.T("dialog.admin.body"),
                    I18n.T("dialog.restart"), I18n.T("dialog.cancel"));
                if (go) { MemoryEngine.RestartElevated(""); return; }
                return;
            }

            busy = true;
            btnOptimize.Busy = true;
            btnOptimize.Text = I18n.T("btn.optimizing");
            btnOptimize.Enabled = false;

            OptimizeLevel lv = level;
            Task.Factory.StartNew(delegate { return MemoryEngine.Optimize(lv, null); })
                .ContinueWith(delegate(Task<OptimizeResult> t)
                {
                    if (t.Status != TaskStatus.RanToCompletion) return;
                    try { BeginInvoke(new Action<OptimizeResult>(OnOptimized), new object[] { t.Result }); }
                    catch { }
                });
        }

        private void OnOptimized(OptimizeResult r)
        {
            if (IsDisposed) return;
            busy = false;
            btnOptimize.Busy = false;
            btnOptimize.Enabled = true;
            btnOptimize.Text = I18n.T("btn.optimize");

            if (r.Released > 0)
            {
                lastReleased = r.Released;
                sessionReleased += (ulong)r.Released;
                st.TotalReleased += r.Released;
                st.LastReleased = r.Released;
                st.LastTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                st.Save();
            }

            sample = MemoryEngine.Sample();
            UpdateSampleUi();

            if (r.Released >= 1048576)
                toast.Show(I18n.T("toast.released", Fmt.Bytes3((ulong)r.Released)));
            else if (r.AdminRequired)
                toast.Show(I18n.T("toast.admin"));
            else
                toast.Show(I18n.T("toast.optimized"));
        }

        // ---------------- 窗口行为 ----------------
        /// <summary>关闭按钮与 Alt+F4 都隐藏到托盘,真正退出走托盘菜单。</summary>
        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        /// <summary>距下次自动优化的分钟数;自动优化关闭时返回 -1。</summary>
        public int MinutesToNextAuto
        {
            get
            {
                if (!st.AutoOptimize) return -1;
                double m = st.AutoInterval - (DateTime.Now - lastAuto).TotalMinutes;
                if (m < 0) m = 0;
                return (int)Math.Ceiling(m);
            }
        }

        private void DoMinimize()
        {
            if (st.StartMinimized)
            {
                Hide();
                ShowInTaskbar = false;
                if (tray != null)
                    tray.ShowBalloonTip(1500, I18n.T("app.name"), I18n.T("tray.tip", (int)Math.Round((double)sample.LoadPercent)), ToolTipIcon.None);
            }
            else WindowState = FormWindowState.Minimized;
        }

        public void ShowMain()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            SetForegroundWindow(Handle);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr h);

        protected override void SetVisibleCore(bool value)
        {
            if (value && !allowVisible)
            {
                allowVisible = true;
                if (startMinimized) { base.SetVisibleCore(false); return; }
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyShape();
        }

        private void ApplyShape()
        {
            if (!IsHandleCreated) return;
            int pref = 2; // DWMWCP_ROUND
            if (Native.DwmSetWindowAttribute(Handle, 33, ref pref, 4) != 0)
            {
                using (GraphicsPath p = Ui.Round(new RectangleF(0, 0, Width, Height), Ui.Fp(12)))
                    Region = new Region(p);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);

            float s = Ui.S;
            // 图标:薄荷色圆角方块 + 白色内存条(与 app.ico 一致)
            var logo = new RectangleF(Ui.Fp(20), Ui.Fp(14), Ui.Fp(16), Ui.Fp(16));
            Ui.Fill(g, logo, Ui.Fp(5), Theme.Accent);
            var bar = new RectangleF(logo.X + Ui.Fp(3.2f), logo.Y + Ui.Fp(5.6f), Ui.Fp(9.6f), Ui.Fp(4.6f));
            Ui.Fill(g, bar, Ui.Fp(1.1f), Color.White);
            using (var sb = new SolidBrush(Theme.Accent))
                g.FillRectangle(sb, logo.X + Ui.Fp(7.4f), logo.Y + Ui.Fp(8.2f), Ui.Fp(1.4f), Ui.Fp(2.2f));

            float tx = Ui.Fp(44);
            var nameRect = new Rectangle((int)tx, 0, (int)Ui.Fp(220), Ui.Px(44));
            TextRenderer.DrawText(g, "MemSweep", Theme.F(11f, FontStyle.Bold), nameRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            Size nsz = TextRenderer.MeasureText(g, "MemSweep", Theme.F(11f, FontStyle.Bold),
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var cnRect = new Rectangle(nameRect.X + nsz.Width + Ui.Px(8), 0, (int)Ui.Fp(60), Ui.Px(44));
            TextRenderer.DrawText(g, "清忆", Theme.F(10f, FontStyle.Regular), cnRect, Theme.Sub,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            Size csz = TextRenderer.MeasureText(g, "清忆", Theme.F(10f, FontStyle.Regular),
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var verRect = new Rectangle(cnRect.X + csz.Width + Ui.Px(8), 0, (int)Ui.Fp(58), Ui.Px(44));
            TextRenderer.DrawText(g, "v1.0.0", Theme.F(8f, FontStyle.Regular), verRect, Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // 标题栏下分隔线
            using (var pen = new Pen(Theme.Border, 1f))
                g.DrawLine(pen, 0, Ui.Fp(44), Width, Ui.Fp(44));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_SHOWME)
            {
                ShowMain();
                return;
            }
            if (m.Msg == Native.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result != Native.HTCLIENT) return;

                int lp = m.LParam.ToInt32();
                int x = (short)(lp & 0xFFFF);
                int y = (short)((lp >> 16) & 0xFFFF);
                Point p = PointToClient(new Point(x, y));
                int edge = Ui.Px(6);
                bool left = p.X <= edge, right = p.X >= ClientSize.Width - edge;
                bool top = p.Y <= edge, bottom = p.Y >= ClientSize.Height - edge;
                if (top && left) { m.Result = (IntPtr)Native.HTTOPLEFT; return; }
                if (top && right) { m.Result = (IntPtr)Native.HTTOPRIGHT; return; }
                if (bottom && left) { m.Result = (IntPtr)Native.HTBOTTOMLEFT; return; }
                if (bottom && right) { m.Result = (IntPtr)Native.HTBOTTOMRIGHT; return; }
                if (left) { m.Result = (IntPtr)Native.HTLEFT; return; }
                if (right) { m.Result = (IntPtr)Native.HTRIGHT; return; }
                if (top) { m.Result = (IntPtr)Native.HTTOP; return; }
                if (bottom) { m.Result = (IntPtr)Native.HTBOTTOM; return; }
                if (p.Y < Ui.Px(44)) { m.Result = (IntPtr)Native.HTCAPTION; return; }
                return;
            }
            if (m.Msg == 0x020A && pageSet != null && pageSet.Visible)
            {
                int lp = m.LParam.ToInt32();
                Point sp = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                if (pageSet.Bounds.Contains(PointToClient(sp)))
                {
                    pageSet.ScrollByWheel((short)(((long)m.WParam >> 16) & 0xFFFF));
                    return;
                }
            }
            base.WndProc(ref m);
        }

        // ---------------- 托盘 ----------------
        private void CreateTray()
        {
            tray = new NotifyIcon();
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            tray.Icon = Icon;
            tray.Text = I18n.T("app.name");
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowMain(); };

            trayMenu = new ContextMenuStrip();
            trayMenu.ShowImageMargin = false;

            miOpen = new ToolStripMenuItem();
            miOpen.Click += delegate { ShowMain(); };
            miOptimize = new ToolStripMenuItem();
            miOptimize.Click += delegate { RunOptimize(st.Level, true); };
            miPause = new ToolStripMenuItem();
            miPause.Click += delegate
            {
                st.AutoOptimize = !st.AutoOptimize;
                st.Save();
                lastAuto = DateTime.Now;
                RebuildTrayMenu();
                pageSet.RefreshValues();
            };
            miExit = new ToolStripMenuItem();
            miExit.Click += delegate { ExitApp(); };

            trayMenu.Items.Add(miOpen);
            trayMenu.Items.Add(miOptimize);
            trayMenu.Items.Add(miPause);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(miExit);
            trayMenu.BackColor = Theme.Card;
            trayMenu.ForeColor = Theme.Text;
            tray.ContextMenuStrip = trayMenu;
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (trayMenu == null) return;
            miOpen.Text = I18n.T("tray.open");
            miOptimize.Text = I18n.T("tray.optimize");
            miPause.Text = st.AutoOptimize ? I18n.T("tray.auto.on") : I18n.T("tray.auto.off");
            miPause.Checked = st.AutoOptimize;
            miExit.Text = I18n.T("tray.exit");
        }

        public void ExitApp()
        {
            exiting = true;
            if (sampleTimer != null) sampleTimer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Close();
            Application.Exit();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            base.OnFormClosed(e);
        }
    }
}