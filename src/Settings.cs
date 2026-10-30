using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace MemSweep
{
    /// <summary>设置持久化:%APPDATA%\MemSweep\settings.ini,纯 key=value。</summary>
    public class Settings
    {
        public static string Dir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemSweep");
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public string Language = "zh";
        public string ThemeName = "light";
        public OptimizeLevel Level = OptimizeLevel.Standard;
        public bool AutoOptimize = false;
        public int AutoInterval = 30;
        /// <summary>true = 智能触发(按内存压力),false = 固定间隔。</summary>
        public bool SmartTrigger = false;
        public int TriggerCommitPercent = 85;
        public int TriggerLoadPercent = 85;
        public int TriggerStandbyPercent = 25;
        public bool AutoStart = false;
        public bool StartMinimized = false;
        public long TotalReleased = 0;
        public long LastReleased = 0;
        public string LastTime = "";
        public int PageFileInitial = 0;
        public int PageFileMax = 0;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        // 兼容旧版键名,写出时统一用短键
                        case "lang":
                        case "language": s.Language = v == "en" ? "en" : "zh"; break;
                        case "theme": s.ThemeName = v == "dark" ? "dark" : "light"; break;
                        case "level":
                            if (v == "light") s.Level = OptimizeLevel.Light;
                            else if (v == "deep") s.Level = OptimizeLevel.Deep;
                            else s.Level = OptimizeLevel.Standard;
                            break;
                        case "auto":
                        case "auto_enabled":
                        case "auto_optimize": s.AutoOptimize = v == "1" || v == "true"; break;
                        case "interval":
                        case "interval_min":
                        case "auto_interval": s.AutoInterval = Math.Max(5, Math.Min(360, Int(v, 30))); break;
                        case "smart":
                        case "smart_trigger": s.SmartTrigger = v == "1" || v == "true"; break;
                        case "trig_commit": s.TriggerCommitPercent = Math.Max(50, Math.Min(99, Int(v, 85))); break;
                        case "trig_load": s.TriggerLoadPercent = Math.Max(50, Math.Min(99, Int(v, 85))); break;
                        case "trig_standby": s.TriggerStandbyPercent = Math.Max(3, Math.Min(60, Int(v, 25))); break;
                        case "autostart": s.AutoStart = v == "1" || v == "true"; break;
                        case "startmin":
                        case "start_minimized": s.StartMinimized = v == "1" || v == "true"; break;
                        case "total_freed":
                        case "total_released": s.TotalReleased = Long(v, 0); break;
                        case "last_freed": s.LastReleased = Long(v, 0); break;
                        case "last_time": s.LastTime = v; break;
                        case "pagefile_initial": s.PageFileInitial = Int(v, 0); break;
                        case "pagefile_max": s.PageFileMax = Int(v, 0); break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                lines.Add("# MemSweep settings");
                lines.Add("lang=" + Language);
                lines.Add("theme=" + ThemeName);
                lines.Add("level=" + (Level == OptimizeLevel.Light ? "light" : Level == OptimizeLevel.Deep ? "deep" : "standard"));
                lines.Add("auto=" + (AutoOptimize ? "1" : "0"));
                lines.Add("interval=" + AutoInterval.ToString(CultureInfo.InvariantCulture));
                lines.Add("smart=" + (SmartTrigger ? "1" : "0"));
                lines.Add("trig_commit=" + TriggerCommitPercent.ToString(CultureInfo.InvariantCulture));
                lines.Add("trig_load=" + TriggerLoadPercent.ToString(CultureInfo.InvariantCulture));
                lines.Add("trig_standby=" + TriggerStandbyPercent.ToString(CultureInfo.InvariantCulture));
                lines.Add("autostart=" + (AutoStart ? "1" : "0"));
                lines.Add("startmin=" + (StartMinimized ? "1" : "0"));
                lines.Add("total_freed=" + TotalReleased.ToString(CultureInfo.InvariantCulture));
                lines.Add("last_freed=" + LastReleased.ToString(CultureInfo.InvariantCulture));
                lines.Add("last_time=" + LastTime);
                lines.Add("pagefile_initial=" + PageFileInitial.ToString(CultureInfo.InvariantCulture));
                lines.Add("pagefile_max=" + PageFileMax.ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }

        private static int Int(string v, int def)
        {
            int r;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static long Long(string v, long def)
        {
            long r;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        // ---------------- 开机自启 ----------------
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "MemSweep";

        public static bool IsAutoStartOn()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(RunName) != null;
                }
            }
            catch { return false; }
        }

        public static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        k.SetValue(RunName, "\"" + exe + "\" --minimized");
                    }
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        // ---------------- 虚拟内存 ----------------
        private const string MmKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

        public class PageFileState
        {
            public bool Automatic;
            public string[] Entries;
            public string Display;
            /// <summary>是否存在系统盘以外的分页文件。写入时必须保留它们。</summary>
            public bool HasOtherDrive;
        }

        /// <summary>系统盘符,形如 "C:"。</summary>
        private static string SystemDrive
        {
            get { return (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").ToUpperInvariant(); }
        }

        /// <summary>判断一条 PagingFiles 记录是否属于系统盘。</summary>
        public static bool IsSystemDriveEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return false;
            string t = entry.Trim();
            if (t.Length < 2 || t[1] != ':') return false;
            // "?:\pagefile.sys" 表示由系统选择卷,实际就是系统盘
            if (t[0] == '?') return true;
            return t.Substring(0, 2).ToUpperInvariant() == SystemDrive;
        }

        /// <summary>把一条记录格式化成 "C: 4096–8192 MB" 这种可读形式。</summary>
        private static string Describe(string entry)
        {
            string[] parts = entry.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return entry;
            // "?:\pagefile.sys" 里的 "?" 指系统盘,显示时换成真实盘符
            string drive = parts[0][0] == '?' ? SystemDrive + parts[0].Substring(1) : parts[0];
            if (parts.Length >= 3)
            {
                int init, mx;
                if (int.TryParse(parts[parts.Length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out init) &&
                    int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mx))
                {
                    if (drive.Length >= 2)
                        return drive.Substring(0, 2) + "  " + init + " – " + mx + " MB";
                    return init + " – " + mx + " MB";
                }
            }
            // 没有大小字段:由系统按需分配
            if (drive.Length >= 2 && drive[1] == ':')
                return drive.Substring(0, 2) + "  " + I18n.T("pagefile.managed");
            return entry;
        }

        /// <summary>读取虚拟内存状态:优先用 WMI 判断是否"系统自动管理",失败则退回注册表推断。</summary>
        public static PageFileState ReadPageFile()
        {
            var st = new PageFileState();
            st.Entries = new string[0];
            bool automatic = true;
            try
            {
                using (var mc = new ManagementClass("Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in mc.GetInstances())
                    {
                        object v = mo["AutomaticManagedPagefile"];
                        if (v is bool) automatic = (bool)v;
                        break;
                    }
                }
            }
            catch
            {
                automatic = false;
            }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(MmKey, false))
                {
                    if (k != null)
                    {
                        string[] arr = k.GetValue("PagingFiles") as string[];
                        if (arr != null) st.Entries = arr;
                    }
                }
            }
            catch { }

            if (st.Entries.Length == 0) automatic = true;

            st.Automatic = automatic;
            if (st.Entries.Length > 0)
            {
                var parts = new List<string>();
                foreach (string e in st.Entries)
                {
                    if (!IsSystemDriveEntry(e)) st.HasOtherDrive = true;
                    if (!string.IsNullOrEmpty(e)) parts.Add(Describe(e));
                }
                st.Display = string.Join("  ·  ", parts.ToArray());
            }

            // 系统自动管理时,注册表里的 "?:\pagefile.sys" 不代表固定大小,直接说明状态
            if (automatic)
                st.Display = I18n.T("pagefile.auto");
            else if (st.Entries.Length == 0)
                st.Display = I18n.T("pagefile.unknown");
            return st;
        }

        /// <summary>按物理内存计算推荐值并取整到 1GB:16GB → 4096 / 8192。</summary>
        public static void Recommend(MemSample sample, out int initial, out int maximum)
        {
            int physMb = (int)(sample.TotalPhys / 1048576UL);
            int raw = physMb / 4;
            initial = ((raw + 512) / 1024) * 1024;
            if (initial < 2048) initial = 2048;
            if (initial > 8192) initial = 8192;
            maximum = initial * 2;
            if (maximum < 4096) maximum = 4096;
            if (maximum > 16384) maximum = 16384;
        }

        /// <summary>
        /// 写入系统盘分页文件并关闭"自动管理"。需要管理员权限。
        /// 只替换系统盘那一条,其它磁盘上的分页文件原样保留——否则会删掉用户自己的配置。
        /// </summary>
        public static bool ApplyPageFile(int initialMb, int maximumMb)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(MmKey, true))
                {
                    if (k == null) return false;

                    var kept = new List<string>();
                    string[] old = k.GetValue("PagingFiles") as string[];
                    if (old != null)
                    {
                        foreach (string raw in old)
                        {
                            if (string.IsNullOrEmpty(raw)) continue;
                            string t = raw.Trim();
                            if (t.Length == 0) continue;
                            if (IsSystemDriveEntry(t)) continue; // 只丢掉系统盘旧值
                            kept.Add(t);                         // 其它磁盘保留
                        }
                    }

                    string entry = SystemDrive + "\\pagefile.sys "
                                   + initialMb.ToString(CultureInfo.InvariantCulture)
                                   + " " + maximumMb.ToString(CultureInfo.InvariantCulture);
                    kept.Insert(0, entry);
                    k.SetValue("PagingFiles", kept.ToArray(), RegistryValueKind.MultiString);
                    Log.Write("pagefile apply " + entry + " kept=" + (kept.Count - 1));
                }
                try
                {
                    using (var mc = new ManagementClass("Win32_ComputerSystem"))
                    {
                        foreach (ManagementObject mo in mc.GetInstances())
                        {
                            mo["AutomaticManagedPagefile"] = false;
                            mo.Put();
                            break;
                        }
                    }
                }
                catch { }
                return true;
            }
            catch { return false; }
        }
    }
}