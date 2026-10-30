using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MemSweep.UI;

namespace MemSweep
{
    internal static class Program
    {
        private const string MutexGlobal = "Global\\MemSweep_SingleInstance";
        private const string MutexLocal = "Local\\MemSweep_SingleInstance";
        private const string WindowTitle = "MemSweep 清忆";

        private static Mutex mutex;
        private static bool owned;

        [STAThread]
        private static void Main(string[] args)
        {
            // 自检模式:不创建界面,跑完三档后写日志并退出
            if (HasArg(args, "--selftest"))
            {
                Environment.Exit(SelfTest.Run());
                return;
            }

            bool minimized = HasArg(args, "--minimized");

            // 单实例:已有一个实例在跑就把它叫到前台
            if (!Acquire())
            {
                ActivateExisting();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Settings settings = Settings.Load();
            Application.Run(new MainForm(settings, minimized || settings.StartMinimized));

            Release();
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>优先用全局互斥体;非管理员某些环境下会失败,退回到会话级。</summary>
        private static bool Acquire()
        {
            try
            {
                mutex = new Mutex(true, MutexGlobal, out owned);
                if (owned) return true;
                return false;
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }

            try
            {
                if (mutex != null) { try { mutex.Dispose(); } catch { } mutex = null; }
                mutex = new Mutex(true, MutexLocal, out owned);
                return owned;
            }
            catch (Exception) { return true; }
        }

        private static void Release()
        {
            try
            {
                if (mutex != null)
                {
                    if (owned) mutex.ReleaseMutex();
                    mutex.Dispose();
                    mutex = null;
                }
            }
            catch { }
        }

        /// <summary>找到已有窗口并激活(包括隐藏在托盘里的情况)。</summary>
        private static void ActivateExisting()
        {
            try
            {
                // 隐藏到托盘的窗口用 FindWindow 找不到,先广播自定义消息唤醒
                Native.PostMessage((IntPtr)0xFFFF, Native.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);

                IntPtr h = Native.FindWindow(null, WindowTitle);
                if (h == IntPtr.Zero) return;
                Native.ShowWindow(h, 9); // SW_RESTORE
                Native.SetForegroundWindow(h);
            }
            catch { }
        }
    }

    /// <summary>无界面自检:跑一次标准档优化,前后数据写 %TEMP%\memsweep_selftest.log。</summary>
    internal static class SelfTest
    {
        public static int Run()
        {
            var sb = new StringBuilder();
            string path = Path.Combine(Path.GetTempPath(), "memsweep_selftest.log");

            try
            {
                MemSample s0 = MemoryEngine.Sample();
                sb.AppendLine("admin=" + (MemoryEngine.IsAdmin ? "yes" : "no"));
                sb.AppendLine("before.avail=" + Mb(s0.AvailPhys) + " MB (" + Gb(s0.AvailPhys) + " GB)");
                sb.AppendLine("before.load=" + s0.LoadPercent + "%");
                sb.AppendLine("before.total=" + Mb(s0.TotalPhys) + " MB");
                sb.AppendLine("before.committed=" + Mb(s0.CommitTotal) + " MB / " + Mb(s0.CommitLimit) + " MB");

                OptimizeResult r = MemoryEngine.Optimize(OptimizeLevel.Standard, null);
                MemSample s1 = MemoryEngine.Sample();

                sb.AppendLine("after.avail=" + Mb(s1.AvailPhys) + " MB (" + Gb(s1.AvailPhys) + " GB)");
                sb.AppendLine("after.load=" + s1.LoadPercent + "%");
                sb.AppendLine("released=" + Mb((ulong)r.Released) + " MB (" + Gb((ulong)r.Released) + " GB)");
                sb.AppendLine("trimmed=" + r.Touched + " skipped=" + r.Skipped + " cacheCleared=" + r.CacheCleared);
                sb.AppendLine("done");

                MemoryEngine.TrimSelf();
                var me = Process.GetCurrentProcess();
                me.Refresh();
                sb.AppendLine("self.workingset=" + (me.WorkingSet64 / 1048576L) + " MB");
                sb.AppendLine("self.private=" + (me.PrivateMemorySize64 / 1048576L) + " MB");
            }
            catch (Exception ex)
            {
                sb.AppendLine("error: " + ex.GetType().Name + " " + ex.Message);
                sb.AppendLine(ex.StackTrace);
            }
            finally
            {
                try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); }
                catch { }
            }
            return 0;
        }

        private static string Mb(ulong bytes)
        {
            return (bytes / 1048576UL).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Gb(ulong bytes)
        {
            return (bytes / 1073741824.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}