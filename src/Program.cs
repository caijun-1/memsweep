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
            // 异常不再静默吞掉:至少留一条日志,便于用户回传
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Write("unhandled: " + (ex == null ? "unknown" : ex.GetType().Name + " " + ex.Message));
            };
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                if (e.Exception != null)
                    Log.Write("ui exception: " + e.Exception.GetType().Name + " " + e.Exception.Message);
            };

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

            Log.Write("start admin=" + (MemoryEngine.IsAdmin ? "1" : "0"));
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
            catch (Exception ex) { Log.Write("mutex global failed: " + ex.Message); }

            try
            {
                if (mutex != null) { try { mutex.Dispose(); } catch { } mutex = null; }
                mutex = new Mutex(true, MutexLocal, out owned);
                return owned;
            }
            catch (Exception ex)
            {
                Log.Write("mutex local failed: " + ex.Message);
                // 互斥体不可用时退回到窗口探测:已有窗口就认为实例已存在,
                // 找不到才放行,避免因为一次权限异常就允许双开。
                try
                {
                    if (Native.FindWindow(null, WindowTitle) != IntPtr.Zero) return false;
                }
                catch { }
                return true;
            }
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

    /// <summary>无界面自检:先断言触发逻辑,再跑一次标准档优化,结果写 %TEMP%\memsweep_selftest.log。</summary>
    internal static class SelfTest
    {
        public static int Run()
        {
            var sb = new StringBuilder();
            string path = Path.Combine(Path.GetTempPath(), "memsweep_selftest.log");
            int fail = 0;

            try
            {
                fail += AssertDecide(sb);

                MemSample s0 = MemoryEngine.Sample();
                sb.AppendLine("admin=" + (MemoryEngine.IsAdmin ? "yes" : "no"));
                sb.AppendLine("before.avail=" + Mb(s0.AvailPhys) + " MB (" + Gb(s0.AvailPhys) + " GB)");
                sb.AppendLine("before.load=" + s0.LoadPercent + "%");
                sb.AppendLine("before.total=" + Mb(s0.TotalPhys) + " MB");
                sb.AppendLine("before.committed=" + Mb(s0.CommitTotal) + " MB / " + Mb(s0.CommitLimit) + " MB");
                sb.AppendLine("before.standby=" + Mb(s0.StandbyBytes) + " MB ("
                              + s0.StandbyPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%)");
                sb.AppendLine("decide=" + MemoryEngine.Decide(s0, new Settings()));

                OptimizeResult r = MemoryEngine.Optimize(OptimizeLevel.Standard, null);
                MemSample s1 = MemoryEngine.Sample();

                sb.AppendLine("after.avail=" + Mb(s1.AvailPhys) + " MB (" + Gb(s1.AvailPhys) + " GB)");
                sb.AppendLine("after.load=" + s1.LoadPercent + "%");
                sb.AppendLine("released=" + Mb((ulong)(r.Released > 0 ? r.Released : 0)) + " MB");
                sb.AppendLine("  standby=" + Mb((ulong)(r.StandbyFreed > 0 ? r.StandbyFreed : 0)) + " MB");
                sb.AppendLine("  cache=" + Mb((ulong)(r.CacheFreed > 0 ? r.CacheFreed : 0)) + " MB");
                sb.AppendLine("  workingset=" + Mb((ulong)(r.WorkingSetFreed > 0 ? r.WorkingSetFreed : 0)) + " MB");
                sb.AppendLine("trimmed=" + r.Touched + " skipped=" + r.Skipped + " cacheCleared=" + r.CacheCleared);

                MemoryEngine.TrimSelf();
                var me = Process.GetCurrentProcess();
                me.Refresh();
                sb.AppendLine("self.workingset=" + (me.WorkingSet64 / 1048576L) + " MB");
                sb.AppendLine("self.private=" + (me.PrivateMemorySize64 / 1048576L) + " MB");
                sb.AppendLine("log=" + Log.FilePath);
                sb.AppendLine(fail == 0 ? "assert=OK" : "assert=FAIL(" + fail + ")");
                sb.AppendLine("done");
            }
            catch (Exception ex)
            {
                sb.AppendLine("error: " + ex.GetType().Name + " " + ex.Message);
                sb.AppendLine(ex.StackTrace);
                fail++;
            }
            finally
            {
                try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); }
                catch { }
            }
            return fail == 0 ? 0 : 2;
        }

        /// <summary>用构造样本断言智能触发,不依赖真实内存状态。</summary>
        private static int AssertDecide(StringBuilder sb)
        {
            int fail = 0;
            var st = new Settings();
            var s = new MemSample();
            s.TotalPhys = 8UL << 30;
            s.CommitLimit = 16UL << 30;

            // 1) 办公态:各项都低,不该触发
            s.CommitTotal = 6UL << 30;
            s.StandbyBytes = 1UL << 30;
            s.LoadPercent = 45;
            fail += Check(sb, "decide.idle", MemoryEngine.Decide(s, st) == TriggerReason.None);

            // 2) 提交额度吃紧
            s.CommitTotal = 15UL << 30;
            fail += Check(sb, "decide.commit", MemoryEngine.Decide(s, st) == TriggerReason.Commit);

            // 3) 备用列表占比高
            s.CommitTotal = 6UL << 30;
            s.StandbyBytes = 2UL << 30;
            s.LoadPercent = 50;
            fail += Check(sb, "decide.standby", MemoryEngine.Decide(s, st) == TriggerReason.Standby);

            // 4) 物理负载高
            s.StandbyBytes = 1UL << 30;
            s.LoadPercent = 90;
            fail += Check(sb, "decide.load", MemoryEngine.Decide(s, st) == TriggerReason.Load);

            // 5) 空样本不能除零,也不能误触发
            fail += Check(sb, "decide.empty", MemoryEngine.Decide(new MemSample(), st) == TriggerReason.None);
            fail += Check(sb, "decide.null", MemoryEngine.Decide(null, st) == TriggerReason.None);

            // 6) CommitLimit 缺失时跳过提交判据
            var s2 = new MemSample();
            s2.TotalPhys = 8UL << 30;
            s2.CommitTotal = 100UL << 30;
            s2.LoadPercent = 10;
            fail += Check(sb, "decide.no-limit", MemoryEngine.Decide(s2, st) == TriggerReason.None);

            return fail;
        }

        private static int Check(StringBuilder sb, string name, bool ok)
        {
            sb.AppendLine((ok ? "  PASS " : "  FAIL ") + name);
            return ok ? 0 : 1;
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