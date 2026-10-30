using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace MemSweep.UI
{
    /// <summary>设置页:内部自绘 + 手动滚动,避免系统滚动条破坏深色主题。</summary>
    public class SettingsPage : Ctl
    {
        private class TextItem
        {
            public Rectangle Rect;
            public string Text;
            public float Size;
            public FontStyle Style;
            public Color Color;
            public bool Wrap;
            public bool Ellipsis;
        }

        private readonly MainForm main;
        private Ctl content;
        private readonly List<TextItem> texts = new List<TextItem>();
        private readonly List<Rectangle> cards = new List<Rectangle>();
        private readonly List<Rectangle> frames = new List<Rectangle>();

        private Toggle tgAuto, tgAutoStart, tgStartMin;
        private TextBox tbInterval, tbInit, tbMax;
        private Segmented segLevel, segLang, segTheme;
        private FlatButton btnRec, btnApply;

        private int scrollY;
        private int maxScroll;
        private Settings.PageFileState pfState;
        private int recInitial = 4096, recMax = 8192;
        private bool loading = true;

        private Settings St { get { return main.St; } }

        public SettingsPage(MainForm owner)
        {
            main = owner;
            BackColor = Theme.Bg;

            content = new Ctl();
            content.BackColor = Theme.Bg;
            content.Paint += delegate(object s, PaintEventArgs e) { PaintContent(e.Graphics); };
            Controls.Add(content);

            tgAuto = new Toggle(); content.Controls.Add(tgAuto);
            tgAuto.CheckedChanged += delegate
            {
                if (loading) return;
                main.SetAuto(tgAuto.Checked, ParseInt(tbInterval.Text, 30));
                Rebuild();
            };

            tbInterval = MakeInput();
            tbInterval.Leave += delegate
            {
                if (loading) return;
                int v = Math.Max(5, Math.Min(360, ParseInt(tbInterval.Text, 30)));
                tbInterval.Text = v.ToString(CultureInfo.InvariantCulture);
                main.SetAuto(tgAuto.Checked, v);
            };
            content.Controls.Add(tbInterval);

            tgAutoStart = new Toggle(); content.Controls.Add(tgAutoStart);
            tgAutoStart.CheckedChanged += delegate
            {
                if (loading) return;
                St.AutoStart = tgAutoStart.Checked;
                St.Save();
                Settings.SetAutoStart(tgAutoStart.Checked);
            };

            tgStartMin = new Toggle(); content.Controls.Add(tgStartMin);
            tgStartMin.CheckedChanged += delegate
            {
                if (loading) return;
                St.StartMinimized = tgStartMin.Checked;
                St.Save();
            };

            segLevel = new Segmented(); content.Controls.Add(segLevel);
            segLevel.Changed += delegate
            {
                if (loading) return;
                main.SetLevel((OptimizeLevel)segLevel.Selected);
            };

            tbInit = MakeInput(); content.Controls.Add(tbInit);
            tbMax = MakeInput(); content.Controls.Add(tbMax);

            btnRec = new FlatButton();
            btnRec.Primary = false;
            btnRec.FontPt = 9f;
            btnRec.Click += delegate
            {
                tbInit.Text = recInitial.ToString(CultureInfo.InvariantCulture);
                tbMax.Text = recMax.ToString(CultureInfo.InvariantCulture);
            };
            content.Controls.Add(btnRec);

            btnApply = new FlatButton();
            btnApply.Primary = true;
            btnApply.FontPt = 9f;
            btnApply.Click += delegate { ApplyPageFile(); };
            content.Controls.Add(btnApply);

            segLang = new Segmented(); content.Controls.Add(segLang);
            segLang.Changed += delegate
            {
                if (loading) return;
                main.SetLanguage(segLang.Selected == 1 ? "en" : "zh");
            };

            segTheme = new Segmented(); content.Controls.Add(segTheme);
            segTheme.Changed += delegate
            {
                if (loading) return;
                main.SetTheme(segTheme.Selected == 1);
            };

            // 子控件的滚轮统一转给页面
            foreach (Control c in new Control[] { content, tgAuto, tgAutoStart, tgStartMin, segLevel, segLang, segTheme, btnRec, btnApply })
                c.MouseWheel += OnChildWheel;
            tbInterval.MouseWheel += OnChildWheel;
            tbInit.MouseWheel += OnChildWheel;
            tbMax.MouseWheel += OnChildWheel;
        }

        private TextBox MakeInput()
        {
            var tb = new TextBox();
            tb.BorderStyle = BorderStyle.None;
            tb.Font = Theme.F(9.5f, FontStyle.Regular);
            tb.BackColor = Theme.Track;
            tb.ForeColor = Theme.Text;
            return tb;
        }

        private static int ParseInt(string s, int def)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        /// <summary>分段控件的选项与按钮文案随语言刷新(Items 不在构造时设置,否则切语言后仍是旧文字)。</summary>
        private void ApplyTexts()
        {
            segLevel.Items = new string[] { I18n.T("level.light"), I18n.T("level.standard"), I18n.T("level.deep") };
            segLang.Items = new string[] { "中文", "English" };
            segTheme.Items = new string[] { I18n.T("theme.light"), I18n.T("theme.dark") };
            segLevel.Invalidate();
            segLang.Invalidate();
            segTheme.Invalidate();
            btnRec.Text = I18n.T("setting.pagefile.use");
            btnApply.Text = I18n.T("setting.pagefile.apply");
        }

        private void OnChildWheel(object sender, MouseEventArgs e) { ScrollBy(-e.Delta); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta);
            base.OnMouseWheel(e);
        }

        /// <summary>直接处理滚轮消息,不依赖控件是否拥有焦点。</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A)
            {
                ScrollByWheel((short)(((long)m.WParam >> 16) & 0xFFFF));
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>按滚轮增量滚动(向上为正),供本页与主窗体转发共用。</summary>
        public void ScrollByWheel(int delta) { ScrollBy(-delta); }

        private void ScrollBy(int delta)
        {
            int v = scrollY + delta;
            if (v < 0) v = 0;
            if (v > maxScroll) v = maxScroll;
            if (v == scrollY) return;
            scrollY = v;
            content.Top = -scrollY;
            content.Invalidate();
            Invalidate();
        }

        // ---------------- 数据刷新 ----------------
        public void RefreshValues()
        {
            if (content == null) return;
            ApplyTexts();
            loading = true;
            try
            {
                tgAuto.SetSilent(St.AutoOptimize);
                tbInterval.Text = St.AutoInterval.ToString(CultureInfo.InvariantCulture);
                tgAutoStart.SetSilent(Settings.IsAutoStartOn());
                tgStartMin.SetSilent(St.StartMinimized);
                segLevel.SetSilent((int)St.Level);
                segLang.SetSilent(I18n.IsEn ? 1 : 0);
                segTheme.SetSilent(Theme.Dark ? 1 : 0);
                St.AutoStart = tgAutoStart.Checked;

                if (pfState != null)
                {
                    if (string.IsNullOrEmpty(tbInit.Text)) tbInit.Text = recInitial.ToString(CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(tbMax.Text)) tbMax.Text = recMax.ToString(CultureInfo.InvariantCulture);
                }
            }
            finally { loading = false; }
            Rebuild();
        }

        private void LoadPageFile()
        {
            int init, mx;
            Settings.Recommend(MemoryEngine.Sample(), out init, out mx);
            recInitial = init; recMax = mx;

            System.Threading.Tasks.Task.Factory.StartNew(delegate { return Settings.ReadPageFile(); })
                .ContinueWith(delegate(System.Threading.Tasks.Task<Settings.PageFileState> t)
                {
                    Settings.PageFileState s = null;
                    try { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) s = t.Result; }
                    catch { }
                    if (IsDisposed || s == null) return;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            pfState = s;
                            if (St.PageFileInitial > 0) tbInit.Text = St.PageFileInitial.ToString(CultureInfo.InvariantCulture);
                            else if (string.IsNullOrEmpty(tbInit.Text)) tbInit.Text = recInitial.ToString(CultureInfo.InvariantCulture);
                            if (St.PageFileMax > 0) tbMax.Text = St.PageFileMax.ToString(CultureInfo.InvariantCulture);
                            else if (string.IsNullOrEmpty(tbMax.Text)) tbMax.Text = recMax.ToString(CultureInfo.InvariantCulture);
                            Rebuild();
                        }));
                    }
                    catch { }
                });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LoadPageFile();
        }

        private void ApplyPageFile()
        {
            int i = ParseInt(tbInit.Text, recInitial);
            int m = ParseInt(tbMax.Text, recMax);
            if (i < 16) i = 16;
            if (m < i) m = i;
            St.PageFileInitial = i;
            St.PageFileMax = m;
            St.Save();

            if (!MemoryEngine.IsAdmin)
            {
                bool go = DialogBox.Ask(this, I18n.T("dialog.pagefile.title"), I18n.T("dialog.pagefile.body"),
                    I18n.T("dialog.restart"), I18n.T("dialog.cancel"));
                if (go) MemoryEngine.RestartElevated("");
                return;
            }

            bool ok = Settings.ApplyPageFile(i, m);
            if (ok) DialogBox.Info(this, I18n.T("dialog.done.title"), I18n.T("dialog.done.body"));
            else DialogBox.Info(this, I18n.T("dialog.fail.title"), I18n.T("dialog.fail.body"));
            LoadPageFile();
        }

        // ---------------- 主题 ----------------
        public void ApplyTheme()
        {
            BackColor = Theme.Bg;
            content.BackColor = Theme.Bg;
            foreach (Control c in content.Controls) c.BackColor = Theme.Track;
            foreach (TextBox tb in new TextBox[] { tbInterval, tbInit, tbMax })
            {
                tb.BackColor = Theme.Track;
                tb.ForeColor = Theme.Text;
                tb.Font = Theme.F(9.5f, FontStyle.Regular);
            }
            foreach (Control c in new Control[] { tgAuto, tgAutoStart, tgStartMin, segLevel, segLang, segTheme })
                c.BackColor = Theme.Card;
            btnRec.BackColor = Theme.Card;
            btnApply.BackColor = Theme.Card;
            Invalidate(true);
        }

        // ---------------- 布局 ----------------
        private void AddText(string text, int x, int y, int w, int h, float size, FontStyle style, Color c, bool wrap, bool ellipsis)
        {
            var it = new TextItem();
            it.Text = text; it.Size = size; it.Style = style; it.Color = c; it.Wrap = wrap; it.Ellipsis = ellipsis;
            it.Rect = new Rectangle(x, y, w, h);
            texts.Add(it);
        }

        public void Relayout()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            if (content == null || Width <= 0) return;
            texts.Clear();
            cards.Clear();
            frames.Clear();

            int pad = Ui.Px(24);
            int sbGap = maxScroll > 0 ? Ui.Px(12) : 0;
            int w = Width - pad * 2 - sbGap;
            int y = Ui.Px(6);
            int padIn = Ui.Px(18);

            // ---- 自动优化 ----
            int hA = Ui.Px(156);
            cards.Add(new Rectangle(pad, y, w, hA));
            AddText(I18n.T("setting.auto"), pad + padIn, y + Ui.Px(16), w - padIn * 2 - Ui.Px(120), Ui.Px(24), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            int tgX = pad + w - padIn - Ui.Px(44);
            tgAuto.SetBounds(tgX, y + Ui.Px(15), Ui.Px(44), Ui.Px(24));
            AddText(I18n.T("setting.enable"), tgX - Ui.Px(8) - Ui.Px(56), y + Ui.Px(15), Ui.Px(56), Ui.Px(24), 9f, FontStyle.Regular, Theme.Sub, false, false);
            AddText(I18n.T("setting.interval"), pad + padIn, y + Ui.Px(54), Ui.Px(120), Ui.Px(24), 9.5f, FontStyle.Regular, Theme.Sub, false, false);
            frames.Add(new Rectangle(pad + w - padIn - Ui.Px(110), y + Ui.Px(48), Ui.Px(110), Ui.Px(34)));
            tbInterval.SetBounds(pad + w - padIn - Ui.Px(110) + Ui.Px(10), y + Ui.Px(56), Ui.Px(90), Ui.Px(20));
            int next = main.MinutesToNextAuto;
            AddText(next < 0 ? I18n.T("setting.next.off") : I18n.T("setting.next", next),
                pad + padIn, y + Ui.Px(88), w - padIn * 2, Ui.Px(20), 9f, FontStyle.Regular,
                next < 0 ? Theme.Sub : Theme.Accent, false, true);
            AddText(I18n.T("setting.auto.desc"), pad + padIn, y + Ui.Px(112), w - padIn * 2, Ui.Px(34), 9f, FontStyle.Regular, Theme.Sub, true, false);
            y += hA + Ui.Px(14);

            // ---- 自启 / 最小化 ----
            int hB = Ui.Px(156);
            cards.Add(new Rectangle(pad, y, w, hB));
            AddText(I18n.T("setting.autostart"), pad + padIn, y + Ui.Px(16), w - padIn * 2 - Ui.Px(64), Ui.Px(22), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            AddText(I18n.T("setting.autostart.desc"), pad + padIn, y + Ui.Px(40), w - padIn * 2 - Ui.Px(70), Ui.Px(20), 9f, FontStyle.Regular, Theme.Sub, false, true);
            tgAutoStart.SetBounds(pad + w - padIn - Ui.Px(44), y + Ui.Px(15), Ui.Px(44), Ui.Px(24));
            AddText(I18n.T("setting.startmin"), pad + padIn, y + Ui.Px(84), w - padIn * 2 - Ui.Px(64), Ui.Px(22), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            AddText(I18n.T("setting.startmin.desc"), pad + padIn, y + Ui.Px(108), w - padIn * 2 - Ui.Px(70), Ui.Px(20), 9f, FontStyle.Regular, Theme.Sub, false, true);
            tgStartMin.SetBounds(pad + w - padIn - Ui.Px(44), y + Ui.Px(83), Ui.Px(44), Ui.Px(24));
            y += hB + Ui.Px(14);

            // ---- 默认档位 ----
            int hC = Ui.Px(84);
            cards.Add(new Rectangle(pad, y, w, hC));
            AddText(I18n.T("setting.defaultlevel"), pad + padIn, y, Ui.Px(200), hC, 10.5f, FontStyle.Bold, Theme.Text, false, false);
            segLevel.SetBounds(pad + w - padIn - Ui.Px(258), y + (hC - Ui.Px(34)) / 2, Ui.Px(258), Ui.Px(34));
            y += hC + Ui.Px(14);

            // ---- 虚拟内存 ----
            int hD = Ui.Px(308);
            cards.Add(new Rectangle(pad, y, w, hD));
            AddText(I18n.T("setting.pagefile"), pad + padIn, y + Ui.Px(16), w - padIn * 2, Ui.Px(24), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            string status = pfState == null ? I18n.T("pagefile.unknown") : pfState.Display;
            AddText(I18n.T("setting.pagefile.status", status), pad + padIn, y + Ui.Px(46), w - padIn * 2, Ui.Px(22), 9.5f, FontStyle.Regular, Theme.Sub, false, true);
            AddText(I18n.T("setting.pagefile.recommend", recInitial, recMax), pad + padIn, y + Ui.Px(70), w - padIn * 2, Ui.Px(22), 9f, FontStyle.Regular, Theme.Accent, false, false);
            AddText(I18n.T("setting.pagefile.initial"), pad + padIn, y + Ui.Px(102), Ui.Px(150), Ui.Px(20), 9f, FontStyle.Regular, Theme.Sub, false, false);
            frames.Add(new Rectangle(pad + padIn, y + Ui.Px(126), Ui.Px(150), Ui.Px(36)));
            tbInit.SetBounds(pad + padIn + Ui.Px(10), y + Ui.Px(135), Ui.Px(130), Ui.Px(20));
            AddText(I18n.T("setting.pagefile.maximum"), pad + padIn + Ui.Px(170), y + Ui.Px(102), Ui.Px(150), Ui.Px(20), 9f, FontStyle.Regular, Theme.Sub, false, false);
            frames.Add(new Rectangle(pad + padIn + Ui.Px(170), y + Ui.Px(126), Ui.Px(150), Ui.Px(36)));
            tbMax.SetBounds(pad + padIn + Ui.Px(180), y + Ui.Px(135), Ui.Px(130), Ui.Px(20));
            btnRec.SetBounds(pad + padIn, y + Ui.Px(180), Ui.Px(132), Ui.Px(36));
            btnApply.SetBounds(pad + padIn + Ui.Px(144), y + Ui.Px(180), Ui.Px(100), Ui.Px(36));
            AddText(I18n.T("setting.pagefile.note"), pad + padIn, y + Ui.Px(230), w - padIn * 2, Ui.Px(62), 9f, FontStyle.Regular, Theme.Sub, true, false);
            y += hD + Ui.Px(14);

            // ---- 语言 / 主题 ----
            int hE = Ui.Px(150);
            cards.Add(new Rectangle(pad, y, w, hE));
            AddText(I18n.T("setting.language"), pad + padIn, y, Ui.Px(200), Ui.Px(76), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            segLang.SetBounds(pad + w - padIn - Ui.Px(200), y + Ui.Px(21), Ui.Px(200), Ui.Px(34));
            AddText(I18n.T("setting.theme"), pad + padIn, y + Ui.Px(76), Ui.Px(200), Ui.Px(74), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            segTheme.SetBounds(pad + w - padIn - Ui.Px(200), y + Ui.Px(97), Ui.Px(200), Ui.Px(34));
            y += hE + Ui.Px(14);

            // ---- 关于 ----
            int hF = Ui.Px(216);
            cards.Add(new Rectangle(pad, y, w, hF));
            AddText(I18n.T("setting.about"), pad + padIn, y + Ui.Px(16), w - padIn * 2, Ui.Px(24), 10.5f, FontStyle.Bold, Theme.Text, false, false);
            AddText(I18n.T("about.version"), pad + padIn, y + Ui.Px(44), w - padIn * 2, Ui.Px(20), 9.5f, FontStyle.Regular, Theme.Text, false, false);
            AddText(I18n.T("about.tagline"), pad + padIn, y + Ui.Px(66), w - padIn * 2, Ui.Px(18), 9f, FontStyle.Regular, Theme.Faint, false, true);
            AddText(MemoryEngine.IsAdmin ? I18n.T("about.admin") : I18n.T("about.user"),
                pad + padIn, y + Ui.Px(88), w - padIn * 2, Ui.Px(18), 9f, FontStyle.Regular, Theme.Sub, false, true);
            string sess = I18n.T("about.session", Fmt.Bytes(main.SessionReleased));
            if (!string.IsNullOrEmpty(St.LastTime)) sess += " · " + I18n.T("about.last", St.LastTime);
            AddText(sess, pad + padIn, y + Ui.Px(108), w - padIn * 2, Ui.Px(18), 9f, FontStyle.Regular, Theme.Sub, false, true);
            AddText(I18n.T("about.engine"), pad + padIn, y + Ui.Px(134), w - padIn * 2, Ui.Px(36), 9f, FontStyle.Regular, Theme.Sub, true, false);
            AddText(I18n.T("about.disclaimer"), pad + padIn, y + Ui.Px(174), w - padIn * 2, Ui.Px(30), 9f, FontStyle.Regular, Theme.Faint, true, false);
            y += hF + Ui.Px(20);

            int totalH = y;
            maxScroll = Math.Max(0, totalH - Height);
            if (scrollY > maxScroll) scrollY = maxScroll;
            content.SetBounds(0, -scrollY, Width, totalH);

            content.Invalidate();
            Invalidate();
        }

        // ---------------- 绘制 ----------------
        // 卡片/文字/滚动条全部画在 content 表面(子控件之上),否则会被 content 的不透明背景盖住。
        // content 的原点随 scrollY 上移,所以滚动条要补偿 scrollY 才能始终贴在页面右侧。
        private void PaintContent(Graphics g)
        {
            foreach (Rectangle r in cards)
                Ui.Fill(g, new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), Ui.Fp(14), Theme.Card);
            foreach (Rectangle r in cards)
                Ui.Stroke(g, new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), Ui.Fp(14), Theme.Border, 1f);
            foreach (Rectangle r in frames)
                Ui.Fill(g, new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), Ui.Fp(8), Theme.Track);

            foreach (TextItem it in texts)
            {
                TextFormatFlags f = Ui.Flags(ContentAlignment.MiddleLeft, it.Wrap);
                if (it.Ellipsis) f |= TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(g, it.Text, Theme.F(it.Size, it.Style), it.Rect, it.Color, f);
            }

            // 自绘滚动条(content 坐标系:顶部随 scrollY 下移,视觉上保持不动)
            if (maxScroll > 0)
            {
                float trackX = Width - Ui.Fp(9);
                float trackW = Ui.Fp(4);
                int trackTop = scrollY + Ui.Px(8);
                int trackH = Height - Ui.Px(16);
                Ui.Fill(g, new RectangleF(trackX, trackTop, trackW, trackH), trackW / 2f, Theme.Track);
                float ratio = (float)Height / (Height + maxScroll);
                float thumbH = Math.Max(Ui.Fp(40), trackH * ratio);
                float thumbY = trackTop + (trackH - thumbH) * ((float)scrollY / maxScroll);
                Ui.Fill(g, new RectangleF(trackX, thumbY, trackW, thumbH), trackW / 2f, Theme.Mix(Theme.Sub, Theme.Bg, 0.35));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Prep(e.Graphics);
        }
    }
}