using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace MemSweep
{
    public enum OptimizeLevel
    {
        Light = 0,
        Standard = 1,
        Deep = 2
    }

    /// <summary>智能触发的判定来源,便于写日志和自检。</summary>
    public enum TriggerReason
    {
        None = 0,
        Commit = 1,
        Standby = 2,
        Load = 3
    }

    /// <summary>一次内存采样结果,所有数值统一为字节。</summary>
    public class MemSample
    {
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong CommitTotal;
        public ulong CommitLimit;
        public ulong PhysicalTotal;
        public ulong SystemCache;
        public ulong StandbyBytes;
        public uint LoadPercent;
        public int ProcessCount;

        public ulong UsedPhys { get { return TotalPhys > AvailPhys ? TotalPhys - AvailPhys : 0; } }
        public ulong Committed { get { return CommitTotal; } }

        /// <summary>提交率(0-100)。提交额度吃紧才是真的会失败的地方。</summary>
        public double CommitPercent
        {
            get { return CommitLimit > 0 ? CommitTotal * 100.0 / CommitLimit : 0; }
        }

        /// <summary>备用列表占物理内存的比例(0-100)。</summary>
        public double StandbyPercent
        {
            get { return TotalPhys > 0 ? StandbyBytes * 100.0 / TotalPhys : 0; }
        }
    }

    public class OptimizeResult
    {
        public OptimizeLevel Level;
        public bool Smart;
        public TriggerReason Reason;
        public ulong Before;
        public ulong After;
        public long Released;
        // 三项来源:备用列表 / 文件缓存 / 其余(工作集裁剪 + 系统回收)
        public long StandbyFreed;
        public long CacheFreed;
        public long WorkingSetFreed;
        public int Touched;
        public int Skipped;
        public bool AdminRequired;
        public bool CacheCleared;
        public bool StandbyPurged;
    }

    /// <summary>内存采样与三档优化。全部动作真实调用系统 API。</summary>
    public static class MemoryEngine
    {
        // 系统关键进程:绝不动
        private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "Memory Compression", "csrss", "wininit", "winlogon",
            "services", "lsass", "smss", "svchost", "MsMpEng", "MpDefenderCoreService",
            "dwm", "MemSweep", "audiodg", "SecurityHealthService", "WUDFHost", "fontdrvhost"
        };

        // 低于这个工作集的进程清了也没收益,直接跳过
        private const ulong MinWorkingSetBytes = 20UL << 20;
        // 轻量档只动小进程
        private const ulong LightMaxWorkingSet = 300UL << 20;

        public static bool IsAdmin
        {
            get
            {
                try
                {
                    using (var id = WindowsIdentity.GetCurrent())
                        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        // ---------------- 采样 ----------------
        public static MemSample Sample()
        {
            var s = new MemSample();
            var ms = new Native.MEMORYSTATUSEX();
            ms.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms))
            {
                s.TotalPhys = ms.ullTotalPhys;
                s.AvailPhys = ms.ullAvailPhys;
                s.TotalPageFile = ms.ullTotalPageFile;
                s.AvailPageFile = ms.ullAvailPageFile;
                s.LoadPercent = ms.dwMemoryLoad;
            }

            ulong pageSize = 4096;
            var pi = new Native.PERFORMANCE_INFORMATION();
            int size = Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION));
            if (Native.GetPerformanceInfo(out pi, size))
            {
                if (pi.PageSize != IntPtr.Zero) pageSize = (ulong)pi.PageSize.ToInt64();
                s.CommitTotal = Native.PagesToBytes(pi.CommitTotal, pageSize);
                s.CommitLimit = Native.PagesToBytes(pi.CommitLimit, pageSize);
                s.PhysicalTotal = Native.PagesToBytes(pi.PhysicalTotal, pageSize);
                s.SystemCache = Native.PagesToBytes(pi.SystemCache, pageSize);
                s.ProcessCount = pi.ProcessCount;
                if (s.TotalPhys == 0) s.TotalPhys = s.PhysicalTotal;
                if (s.AvailPhys == 0) s.AvailPhys = Native.PagesToBytes(pi.PhysicalAvailable, pageSize);
            }

            s.StandbyBytes = ReadStandbyBytes(pageSize);
            return s;
        }

        /// <summary>
        /// 读备用列表(standby)总字节数。SYSTEM_MEMORY_LIST_INFORMATION 前 5 个字段是
        /// Zero/Free/Modified/ModifiedNoWrite/Bad 页数,紧接 8 个优先级桶就是 standby 页数。
        /// 只取前 13 个 SIZE_T,后面的 RepurposedPagesByPriority 等字段随系统版本变化,不读更安全。
        /// </summary>
        private static ulong ReadStandbyBytes(ulong pageSize)
        {
            int count = 13;
            int cb = IntPtr.Size * count;
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(cb);
                for (int i = 0; i < count; i++) Marshal.WriteIntPtr(buf, i * IntPtr.Size, IntPtr.Zero);
                int ret;
                int len;
                ret = Native.NtQuerySystemInformation(Native.SystemMemoryListInformation, buf, cb, out len);
                if (ret == Native.StatusInfoLengthMismatch && len > cb)
                {
                    // 结构体后续字段随系统版本增长,按系统回报的长度重试一次
                    Marshal.FreeHGlobal(buf);
                    buf = Marshal.AllocHGlobal(len);
                    for (int i = 0; i < len / IntPtr.Size; i++) Marshal.WriteIntPtr(buf, i * IntPtr.Size, IntPtr.Zero);
                    ret = Native.NtQuerySystemInformation(Native.SystemMemoryListInformation, buf, len, out len);
                }
                if (ret != 0) { Log.Write("standby query failed 0x" + ret.ToString("X8")); return 0; }
                ulong pages = 0;
                for (int i = 5; i < 13; i++)
                {
                    long v = Marshal.ReadIntPtr(buf, i * IntPtr.Size).ToInt64();
                    if (v > 0) pages += (ulong)v;
                }
                return pages * pageSize;
            }
            catch { return 0; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        // ---------------- 决策 ----------------
        /// <summary>
        /// 纯函数:只根据样本和设置判断是否需要自动维护。不触碰系统,便于自检断言。
        /// 提交额度 / 备用列表 / 物理负载任一超阈值即触发;不使用 ullAvailPhys,
        /// 因为它已包含可回收的备用页,几乎不会先于前三个指标告警。
        /// </summary>
        public static TriggerReason Decide(MemSample s, Settings st)
        {
            if (s == null || st == null || s.TotalPhys == 0) return TriggerReason.None;
            if (s.CommitLimit > 0 && s.CommitPercent >= st.TriggerCommitPercent) return TriggerReason.Commit;
            if (s.StandbyBytes > 0 && s.StandbyPercent >= st.TriggerStandbyPercent) return TriggerReason.Standby;
            if (s.LoadPercent >= st.TriggerLoadPercent) return TriggerReason.Load;
            return TriggerReason.None;
        }

        // ---------------- 提权 ----------------
        /// <summary>为当前进程启用指定特权,失败返回 false(不抛异常)。</summary>
        public static bool EnablePrivilege(string name)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!Native.OpenProcessToken(Native.GetCurrentProcess(),
                        Native.TOKEN_ADJUST_PRIVILEGES | Native.TOKEN_QUERY, out token))
                    return false;

                Native.LUID luid;
                if (!Native.LookupPrivilegeValue(null, name, out luid)) return false;

                var tp = new Native.TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Privileges = new Native.LUID_AND_ATTRIBUTES();
                tp.Privileges.Luid = luid;
                tp.Privileges.Attributes = Native.SE_PRIVILEGE_ENABLED;

                if (!Native.AdjustTokenPrivileges(token, false, ref tp,
                        Marshal.SizeOf(typeof(Native.TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero))
                    return false;
                // AdjustTokenPrivileges 即使返回 true 也可能 ERROR_NOT_ALL_ASSIGNED
                return Marshal.GetLastWin32Error() == 0;
            }
            catch { return false; }
            finally { if (token != IntPtr.Zero) Native.CloseHandle(token); }
        }

        // ---------------- 单进程工作集 ----------------
        private static bool EmptyOne(int pid)
        {
            // EmptyWorkingSet 需要 PROCESS_QUERY_INFORMATION;受限进程退回 LIMITED 再试一次
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION | Native.PROCESS_SET_QUOTA, false, pid);
            if (h == IntPtr.Zero)
                h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_QUOTA, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return Native.EmptyWorkingSet(h); }
            finally { Native.CloseHandle(h); }
        }

        /// <summary>把自己的工作集也压下去,符合产品定位。</summary>
        public static void TrimSelf()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        // ---------------- 系统级动作 ----------------
        /// <summary>清系统文件缓存(SystemFileCacheInformation = 0x15),需要 SeIncreaseQuotaPrivilege。</summary>
        public static bool ClearFileCache()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int cb = IntPtr.Size * 2;
                buf = Marshal.AllocHGlobal(cb);
                Marshal.WriteIntPtr(buf, IntPtr.Zero);          // CurrentSize = 0
                Marshal.WriteIntPtr(buf, IntPtr.Size, IntPtr.Zero); // PeakSize = 0
                int st = Native.NtSetSystemInformation(Native.SystemFileCacheInformation, buf, cb);
                return st == 0;
            }
            catch { return false; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        private static bool SetMemoryList(int command)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(4);
                Marshal.WriteInt32(buf, command);
                return Native.NtSetSystemInformation(Native.SystemMemoryListInformation, buf, 4) == 0;
            }
            catch { return false; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        // ---------------- 进程枚举 ----------------
        private class ProcEntry : IComparable<ProcEntry>
        {
            public int Pid;
            public string Name;
            public ulong WorkingSet;

            /// <summary>工作集升序:先动小进程,代价最低,收益比最高。</summary>
            public int CompareTo(ProcEntry o)
            {
                if (o == null) return 1;
                return WorkingSet.CompareTo(o.WorkingSet);
            }
        }

        private static List<ProcEntry> SnapshotProcesses()
        {
            var list = new List<ProcEntry>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return list; }

            foreach (Process p in procs)
            {
                try
                {
                    ulong ws = 0;
                    long w = p.WorkingSet64;
                    if (w > 0) ws = (ulong)w;
                    list.Add(new ProcEntry { Pid = p.Id, Name = p.ProcessName, WorkingSet = ws });
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            list.Sort();
            return list;
        }

        private static int ForegroundPid()
        {
            try
            {
                IntPtr hwnd = Native.GetForegroundWindow();
                int pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                return pid;
            }
            catch { return -1; }
        }

        /// <summary>有可见顶层窗口的进程:用户正在看的东西,不动。</summary>
        private static HashSet<int> VisibleWindowPids()
        {
            var set = new HashSet<int>();
            try
            {
                Native.EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    if (Native.GetWindowTextLength(h) <= 0) return true;
                    int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
                    int pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (pid > 0) set.Add(pid);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return set;
        }

        /// <summary>单次优化的工作量上限,按物理内存比例给,避免在 8GB 机器上清空一半。</summary>
        private static ulong ReleaseCap(OptimizeLevel level, ulong totalPhys)
        {
            double pct;
            if (level == OptimizeLevel.Light) pct = 0.08;
            else if (level == OptimizeLevel.Deep) pct = 0.25;
            else pct = 0.20;

            ulong cap = (ulong)(totalPhys * pct);
            ulong min = 256UL << 20;
            ulong max = (ulong)(totalPhys * 0.25);
            if (cap < min) cap = min;
            if (max > 0 && cap > max) cap = max;
            return cap;
        }

        // ---------------- 结果收敛 ----------------
        /// <summary>
        /// 工作集归还给系统是异步的,通常 400~800ms 才完成。固定 sleep 会让 after 采样偏早,
        /// 这里持续采样到连续 600ms 不再上涨为止,并取峰值作为有效可用量。
        /// </summary>
        private static MemSample Settle()
        {
            TrimSelf();
            MemSample last = Sample();
            ulong best = last.AvailPhys;
            int flat = 0;
            for (int k = 0; k < 10; k++)
            {
                Thread.Sleep(200);
                MemSample cur = Sample();
                if (cur.AvailPhys > best) best = cur.AvailPhys;
                if (cur.AvailPhys < last.AvailPhys + (2UL << 20)) flat++; else flat = 0;
                last = cur;
                if (flat >= 3 && k >= 3) break;
            }
            if (last.AvailPhys < best) last.AvailPhys = best;
            return last;
        }

        private static void FillResult(OptimizeResult r, MemSample before, MemSample after)
        {
            r.Before = before.AvailPhys;
            r.After = after.AvailPhys;
            long delta = (long)after.AvailPhys - (long)before.AvailPhys;
            r.Released = delta > 0 ? delta : 0;

            long sb = (long)before.StandbyBytes - (long)after.StandbyBytes;
            long cc = (long)before.SystemCache - (long)after.SystemCache;
            r.StandbyFreed = sb > 0 ? sb : 0;
            r.CacheFreed = cc > 0 ? cc : 0;
            long rest = r.Released - r.StandbyFreed - r.CacheFreed;
            r.WorkingSetFreed = rest > 0 ? rest : 0;

            Log.Write("result level=" + r.Level + " smart=" + (r.Smart ? "1" : "0")
                + " reason=" + r.Reason
                + " released=" + MB(r.Released) + "MB standby=" + MB(r.StandbyFreed) + "MB"
                + " cache=" + MB(r.CacheFreed) + "MB ws=" + MB(r.WorkingSetFreed) + "MB"
                + " touched=" + r.Touched + " skipped=" + r.Skipped
                + " cacheOk=" + (r.CacheCleared ? "1" : "0") + " standbyOk=" + (r.StandbyPurged ? "1" : "0"));
        }

        private static string MB(long bytes)
        {
            return (bytes / 1048576L).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------------- 主入口 ----------------
        public static OptimizeResult Optimize(OptimizeLevel level, Action<string> progress)
        {
            var result = new OptimizeResult { Level = level };
            var before = Sample();
            result.Before = before.AvailPhys;

            bool admin = IsAdmin;
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeIncreaseWorkingSetPrivilege");

            // 1) 遍历进程释放工作集
            if (progress != null) progress("sweep");
            int fg = ForegroundPid();
            int self = Process.GetCurrentProcess().Id;
            HashSet<int> visible = VisibleWindowPids();
            List<ProcEntry> entries = SnapshotProcesses();

            ulong cap = ReleaseCap(level, before.TotalPhys);
            ulong budget = 0;
            int touched = 0, skipped = 0;
            foreach (ProcEntry e in entries)
            {
                if (budget >= cap) break;
                if (e.Pid == self || e.Pid <= 4) { skipped++; continue; }
                if (Protected.Contains(e.Name) || string.IsNullOrEmpty(e.Name)) { skipped++; continue; }
                if (e.Pid == fg || visible.Contains(e.Pid)) { skipped++; continue; }
                if (e.WorkingSet < MinWorkingSetBytes) { skipped++; continue; }
                if (level == OptimizeLevel.Light && e.WorkingSet > LightMaxWorkingSet) { skipped++; continue; }
                if (EmptyOne(e.Pid)) { touched++; budget += e.WorkingSet; }
                else skipped++;
            }

            // 2) 标准档:清系统文件缓存
            if (level != OptimizeLevel.Light)
            {
                if (progress != null) progress("cache");
                result.CacheCleared = ClearFileCache();
                if (!result.CacheCleared && !admin) result.AdminRequired = true;
            }

            // 3) 深度档:空工作集 / 刷修改页 / 清备用列表
            // 非管理员时这些调用会直接失败,这里仍然尝试并静默降级,不抛异常也不阻塞。
            if (level == OptimizeLevel.Deep)
            {
                if (progress != null) progress("deep");
                SetMemoryList(Native.MemoryEmptyWorkingSets);
                SetMemoryList(Native.MemoryFlushModifiedList);
                result.StandbyPurged = SetMemoryList(Native.MemoryPurgeStandbyList);
                if (!admin && !result.StandbyPurged) result.AdminRequired = true;
            }

            FillResult(result, before, Settle());
            result.Touched = touched;
            result.Skipped = skipped;
            return result;
        }

        /// <summary>
        /// 自动维护:只清文件缓存和备用列表,不裁工作集。
        /// 这两项代价接近零,适合交给定时器自己跑;工作集裁剪留给用户手动点。
        /// </summary>
        public static OptimizeResult SmartClean(TriggerReason reason, Action<string> progress)
        {
            var result = new OptimizeResult { Level = OptimizeLevel.Light, Smart = true, Reason = reason };
            var before = Sample();
            result.Before = before.AvailPhys;

            bool admin = IsAdmin;
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            EnablePrivilege("SeProfileSingleProcessPrivilege");

            if (progress != null) progress("cache");
            result.CacheCleared = ClearFileCache();
            if (!result.CacheCleared && !admin) result.AdminRequired = true;

            if (before.StandbyPercent >= 3.0)
            {
                if (progress != null) progress("standby");
                result.StandbyPurged = SetMemoryList(Native.MemoryPurgeStandbyList);
                if (!admin && !result.StandbyPurged) result.AdminRequired = true;
            }

            FillResult(result, before, Settle());
            return result;
        }

        // ---------------- 提升权限重启 ----------------
        public static void RestartElevated(string args)
        {
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = Process.GetCurrentProcess().MainModule.FileName;
                psi.Arguments = args ?? "";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Environment.Exit(0);
            }
            catch { }
        }
    }

    /// <summary>统一的人类可读格式化,UI 与自检共用。</summary>
    public static class Fmt
    {
        public static string Bytes(ulong bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 1.0) return gb.ToString("0.0") + " GB";
            double mb = bytes / 1048576.0;
            if (mb >= 1.0) return mb.ToString("0") + " MB";
            double kb = bytes / 1024.0;
            return kb.ToString("0") + " KB";
        }

        public static string Bytes3(ulong bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 1.0) return gb.ToString("0.00") + " GB";
            double mb = bytes / 1048576.0;
            if (mb >= 1.0) return mb.ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }
    }

    /// <summary>最小日志:%APPDATA%\MemSweep\log.txt,超过 1MB 轮转一份。</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 1 << 20;

        public static string FilePath { get { return System.IO.Path.Combine(Settings.Dir, "log.txt"); } }

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    if (!System.IO.Directory.Exists(Settings.Dir)) System.IO.Directory.CreateDirectory(Settings.Dir);
                    var f = new System.IO.FileInfo(FilePath);
                    if (f.Exists && f.Length > MaxBytes)
                    {
                        try
                        {
                            string old = System.IO.Path.Combine(Settings.Dir, "log.old.txt");
                            if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                            System.IO.File.Move(FilePath, old);
                        }
                        catch { }
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg
                                  + Environment.NewLine;
                    System.IO.File.AppendAllText(FilePath, line, System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}