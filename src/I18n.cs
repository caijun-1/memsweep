using System;
using System.Collections.Generic;
using System.Globalization;

namespace MemSweep
{
    /// <summary>中英文文案字典。所有界面可见文字都必须走这里。</summary>
    public static class I18n
    {
        public static string Lang = "zh";

        public static bool IsEn { get { return Lang == "en"; } }

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            {"app.name", "MemSweep 清忆"},
            {"app.sub", "内存优化"},

            {"nav.optimize", "优化"},
            {"nav.settings", "设置"},

            {"title.lang", "切换语言"},
            {"title.theme", "切换主题"},
            {"title.min", "最小化"},
            {"title.close", "关闭"},

            {"gauge.ratio", "{0} / {1}"},
            {"gauge.status.good", "内存充足"},
            {"gauge.status.high", "内存偏高"},
            {"gauge.status.tight", "内存紧张"},
            {"gauge.used", "已用"},
            {"stat.total", "总内存"},
            {"stat.avail", "可用内存"},
            {"stat.committed", "已提交 / 上限"},
            {"stat.cache", "缓存"},

            {"level.label", "优化档位"},
            {"level.light", "轻量"},
            {"level.standard", "标准"},
            {"level.deep", "深度"},

            {"btn.optimize", "立即优化"},
            {"btn.optimizing", "优化中…"},
            {"last.released", "上次释放 {0}"},
            {"last.none", "尚未优化"},
            {"total.released", "累计释放 {0}"},
            {"chart.title", "最近 60 秒可用内存"},
            {"chart.avail", "可用 {0}"},
            {"list.title", "占用最高的进程"},
            {"list.mem", "工作集"},
            {"list.release", "释放"},
            {"list.empty", "暂无数据"},

            {"toast.released", "已释放 {0}"},
            {"toast.optimized", "优化完成"},
            {"toast.good", "内存已处于良好状态"},
            {"toast.proc", "已释放 {0} · {1}"},
            {"toast.admin", "深度档需要管理员权限"},

            {"setting.auto", "自动优化"},
            {"setting.enable", "启用"},
            {"setting.auto.desc", "推荐 30 分钟，过短无额外收益。"},
            {"setting.interval", "间隔(分钟)"},
            {"setting.next", "距下次自动优化还有 {0} 分钟"},
            {"setting.next.off", "自动优化已关闭"},
            {"setting.autostart", "开机自启"},
            {"setting.autostart.desc", "登录 Windows 后自动启动,并最小化到托盘。"},
            {"setting.startmin", "启动时最小化到托盘"},
            {"setting.startmin.desc", "开机自启时不显示窗口,直接驻留托盘。"},
            {"setting.closeexit", "关闭时退出"},
            {"setting.closeexit.desc", "关闭后结束进程;关闭此项则关闭窗口时隐藏到托盘。"},
            {"setting.defaultlevel", "默认档位"},
            {"setting.pagefile", "虚拟内存"},
            {"setting.pagefile.status", "当前状态:{0}"},
            {"setting.pagefile.recommend", "推荐值:初始 {0} MB / 最大 {1} MB"},
            {"setting.pagefile.initial", "初始大小(MB)"},
            {"setting.pagefile.maximum", "最大大小(MB)"},
            {"setting.pagefile.use", "使用推荐值"},
            {"setting.pagefile.apply", "应用"},
            {"setting.pagefile.note", "应用需要管理员权限，重启电脑后生效。内存 ≥16GB 且无大型软件需求时，也可保持系统自动管理。"},
            {"pagefile.auto", "系统自动管理"},
            {"pagefile.custom", "自定义 {0} – {1} MB"},
            {"pagefile.unknown", "读取中…"},
            {"setting.language", "界面语言"},
            {"theme.light", "浅色"},
            {"theme.dark", "深色"},
            {"setting.theme", "主题"},
            {"setting.about", "关于"},
            {"about.version", "版本 1.0.0"},
            {"about.tagline", "单文件绿色版 · 不联网 · 无遥测"},
            {"about.admin", "当前权限：管理员"},
            {"about.user", "当前权限：标准用户"},
            {"about.session", "本次运行已释放 {0}"},
            {"about.last", "上次优化 {0}"},
            {"about.never", "尚未优化"},
            {"about.engine", "内存引擎:EmptyWorkingSet + NtSetSystemInformation(文件缓存 / 工作集 / 修改页 / 备用列表)。"},
            {"about.disclaimer", "免责声明:本工具只释放可回收的内存缓存,不会结束任何进程,不影响系统稳定性。"},

            {"tray.open", "打开主界面"},
            {"tray.optimize", "立即优化"},
            {"tray.auto.on", "自动优化：开"},
            {"tray.auto.off", "自动优化：关"},
            {"tray.pause", "暂停自动优化"},
            {"tray.resume", "恢复自动优化"},
            {"tray.exit", "退出"},
            {"tray.tip", "MemSweep 清忆"},

            {"dialog.admin.title", "需要管理员权限"},
            {"dialog.admin.body", "深度优化需要管理员权限才能清理系统工作集、修改页和备用内存列表。是否以管理员身份重启?"},
            {"dialog.restart", "以管理员身份重启"},
            {"dialog.cancel", "取消"},
            {"dialog.pagefile.title", "需要管理员权限"},
            {"dialog.pagefile.body", "修改虚拟内存需要管理员权限,是否以管理员身份重启?"},
            {"dialog.ok", "确定"},
            {"dialog.done.title", "已应用"},
            {"dialog.done.body", "虚拟内存设置已写入,重启电脑后生效。"},
            {"dialog.fail.title", "操作失败"},
            {"dialog.fail.body", "写入失败,请确认程序以管理员身份运行。"}
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            {"app.name", "MemSweep"},
            {"app.sub", "Memory Optimizer"},

            {"nav.optimize", "Optimize"},
            {"nav.settings", "Settings"},

            {"title.lang", "Switch language"},
            {"title.theme", "Switch theme"},
            {"title.min", "Minimize"},
            {"title.close", "Close"},

            {"gauge.ratio", "{0} / {1}"},
            {"gauge.status.good", "Memory is healthy"},
            {"gauge.status.high", "Memory usage is high"},
            {"gauge.status.tight", "Memory is tight"},
            {"gauge.used", "Used"},
            {"stat.total", "Total memory"},
            {"stat.avail", "Available"},
            {"stat.committed", "Committed / limit"},
            {"stat.cache", "Cache"},

            {"level.label", "Optimize level"},
            {"level.light", "Light"},
            {"level.standard", "Standard"},
            {"level.deep", "Deep"},

            {"btn.optimize", "Optimize now"},
            {"btn.optimizing", "Optimizing…"},
            {"last.released", "Last released {0}"},
            {"last.none", "Not optimized yet"},
            {"total.released", "Total released {0}"},
            {"chart.title", "Available memory · last 60s"},
            {"chart.avail", "Available {0}"},
            {"list.title", "Top processes"},
            {"list.mem", "Working set"},
            {"list.release", "Release"},
            {"list.empty", "No data"},

            {"toast.released", "Released {0}"},
            {"toast.optimized", "Optimization complete"},
            {"toast.good", "Memory is already in good shape"},
            {"toast.proc", "Released {0} · {1}"},
            {"toast.admin", "Deep level needs administrator"},

            {"setting.auto", "Auto optimize"},
            {"setting.enable", "Enabled"},
            {"setting.auto.desc", "30 minutes recommended. Running it more often brings no extra gain."},
            {"setting.interval", "Interval (min)"},
            {"setting.next", "Next auto optimize in {0} min"},
            {"setting.next.off", "Auto optimize is off"},
            {"setting.autostart", "Start with Windows"},
            {"setting.autostart.desc", "Launch automatically after sign-in and minimize to tray."},
            {"setting.startmin", "Minimize to tray"},
            {"setting.startmin.desc", "The title bar minimize button hides the window to the tray instead of the taskbar."},
            {"setting.defaultlevel", "Default level"},
            {"setting.pagefile", "Virtual memory"},
            {"setting.pagefile.status", "Current: {0}"},
            {"setting.pagefile.recommend", "Recommended: initial {0} MB / max {1} MB"},
            {"setting.pagefile.initial", "Initial size (MB)"},
            {"setting.pagefile.maximum", "Maximum size (MB)"},
            {"setting.pagefile.use", "Use recommended"},
            {"setting.pagefile.apply", "Apply"},
            {"setting.pagefile.note", "Applying requires administrator rights and takes effect after a reboot. With 16GB+ RAM and no heavy workloads, system-managed paging is fine too."},
            {"pagefile.auto", "System managed"},
            {"pagefile.custom", "Custom {0} – {1} MB"},
            {"pagefile.unknown", "Reading…"},
            {"setting.language", "Language"},
            {"theme.light", "Light"},
            {"theme.dark", "Dark"},
            {"setting.theme", "Theme"},
            {"setting.about", "About"},
            {"about.version", "Version 1.0.0"},
            {"about.tagline", "Single-file portable · No network · No telemetry"},
            {"about.admin", "Current rights: Administrator"},
            {"about.user", "Current rights: Standard user"},
            {"about.session", "{0} released this session"},
            {"about.last", "Last optimized {0}"},
            {"about.never", "Not optimized yet"},
            {"about.engine", "Engine: EmptyWorkingSet + NtSetSystemInformation (file cache / working sets / modified pages / standby list)."},
            {"about.disclaimer", "Disclaimer: only reclaimable memory caches are released. No process is terminated."},

            {"tray.open", "Open window"},
            {"tray.optimize", "Optimize now"},
            {"tray.auto.on", "Auto optimize: On"},
            {"tray.auto.off", "Auto optimize: Off"},
            {"tray.pause", "Pause auto optimize"},
            {"tray.resume", "Resume auto optimize"},
            {"tray.exit", "Exit"},
            {"tray.tip", "MemSweep 清忆"},

            {"dialog.admin.title", "Administrator required"},
            {"dialog.admin.body", "Deep optimization needs administrator rights to trim system working sets, flush modified pages and purge the standby list. Restart as administrator?"},
            {"dialog.restart", "Restart as administrator"},
            {"dialog.cancel", "Cancel"},
            {"dialog.pagefile.title", "Administrator required"},
            {"dialog.pagefile.body", "Changing virtual memory requires administrator rights. Restart as administrator?"},
            {"dialog.ok", "OK"},
            {"dialog.done.title", "Applied"},
            {"dialog.done.body", "Virtual memory settings have been written. They take effect after a reboot."},
            {"dialog.fail.title", "Failed"},
            {"dialog.fail.body", "Write failed. Please run MemSweep as administrator."}
        };

        public static string T(string key)
        {
            Dictionary<string, string> d = IsEn ? En : Zh;
            string v;
            if (d.TryGetValue(key, out v)) return v;
            if (Zh.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            string s = T(key);
            try { return string.Format(CultureInfo.InvariantCulture, s, args); }
            catch { return s; }
        }
    }
}