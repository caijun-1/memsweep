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
        public uint LoadPercent;
        public int ProcessCount;

        public ulong UsedPhys { get { return TotalPhys > AvailPhys ? TotalPhys - AvailPhys : 0; } }
        public ulong Committed { get { return CommitTotal; } }
    }

    public class OptimizeResult
    {
        public OptimizeLevel Level;
        public ulong Before;
        public ulong After;
        public long Released;
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

            var pi = new Native.PERFORMANCE_INFORMATION();
            int size = Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION));
            if (Native.GetPerformanceInfo(out pi, size))
            {
                ulong page = pi.PageSize == IntPtr.Zero ? 4096UL : (ulong)pi.PageSize.ToInt64();
                s.CommitTotal = Native.PagesToBytes(pi.CommitTotal, page);
                s.CommitLimit = Native.PagesToBytes(pi.CommitLimit, page);
                s.PhysicalTotal = Native.PagesToBytes(pi.PhysicalTotal, page);
                s.SystemCache = Native.PagesToBytes(pi.SystemCache, page);
                s.ProcessCount = pi.ProcessCount;
                if (s.TotalPhys == 0) s.TotalPhys = s.PhysicalTotal;
                if (s.AvailPhys == 0) s.AvailPhys = Native.PagesToBytes(pi.PhysicalAvailable, page);
            }
            return s;
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
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_QUOTA, false, pid);
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
        private class ProcEntry
        {
            public int Pid;
            public string Name;
            public double Cpu;
        }

        /// <summary>枚举进程并估算 CPU 占用(用于判断"空闲")。</summary>
        private static List<ProcEntry> SnapshotProcesses(int sampleMs)
        {
            var list = new List<ProcEntry>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return list; }

            var t0 = new Dictionary<int, TimeSpan>();
            var names = new Dictionary<int, string>();
            foreach (var p in procs)
            {
                try
                {
                    int id = p.Id;
                    t0[id] = p.TotalProcessorTime;
                    names[id] = p.ProcessName;
                }
                catch { }
            }

            if (sampleMs > 0) Thread.Sleep(sampleMs);

            foreach (var p in procs)
            {
                try
                {
                    int id = p.Id;
                    TimeSpan start;
                    if (!t0.TryGetValue(id, out start)) continue;
                    string name;
                    if (!names.TryGetValue(id, out name)) name = "";
                    double cpu = 0;
                    if (sampleMs > 0)
                    {
                        double delta = (p.TotalProcessorTime - start).TotalMilliseconds;
                        cpu = delta / sampleMs / Math.Max(1, Environment.ProcessorCount) * 100.0;
                    }
                    list.Add(new ProcEntry { Pid = id, Name = name, Cpu = cpu });
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
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
            var entries = SnapshotProcesses(level == OptimizeLevel.Light ? 120 : 0);

            int touched = 0, skipped = 0;
            foreach (var e in entries)
            {
                if (e.Pid == self || e.Pid <= 4) { skipped++; continue; }
                if (Protected.Contains(e.Name) || string.IsNullOrEmpty(e.Name)) { skipped++; continue; }
                if (level == OptimizeLevel.Light && (e.Pid == fg || e.Cpu > 3.0)) { skipped++; continue; }
                if (EmptyOne(e.Pid)) touched++; else skipped++;
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

            // 4) 自身瘦身 + 等待系统真正回收
            // 工作集归还给系统是异步的,通常 400~800ms 才完成。固定 sleep 会让 after 采样偏早、
            // released 偏小,所以这里持续采样:记录峰值,并连续 600ms 不再上涨才认为稳定。
            TrimSelf();
            ulong best = 0;
            int flat = 0;
            ulong lastAvail = Sample().AvailPhys;
            for (int k = 0; k < 10; k++)
            {
                Thread.Sleep(200);
                MemSample cur = Sample();
                if (cur.AvailPhys > best) best = cur.AvailPhys;
                if (cur.AvailPhys < lastAvail + (2UL << 20)) flat++; else flat = 0;
                lastAvail = cur.AvailPhys;
                if (flat >= 3 && k >= 3) break;
            }

            var after = Sample();
            ulong afterAvail = after.AvailPhys > best ? after.AvailPhys : best;
            result.After = afterAvail;
            long delta = (long)afterAvail - (long)before.AvailPhys;
            result.Released = delta > 0 ? delta : 0;
            result.Touched = touched;
            result.Skipped = skipped;
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
}