using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DeadCellsMultiplayerMod.Mobs.Bosses;

/// <summary>
/// BOSS 战诊断 + 主线程看门狗（临时）。
/// - Log/Phase：记录 BOSS 相关阶段（[BOSS-DIAG]），并更新"主线程最近活动"。
/// - Heartbeat：每帧由主线程调用，作为存活心跳。
/// - 看门狗后台线程：若主线程超过 StallSeconds 没推进（卡死），把"卡死前最后阶段"
///   同步直写 coremod/logs/mp_crash.log（File.AppendAllText 立即落盘），用于定位卡死位置
///   （卡死不抛异常，普通 Serilog 缓冲也可能丢失，故需此机制）。
/// </summary>
public static class BossDiag
{
    // 阈值取 12s：关卡生成等合法长帧约 6s 以内会被过滤，真正的死锁是永久的，仍会被捕获。
    private const double StallSeconds = 12.0;

    private static Serilog.ILogger? _log;
    private static volatile string _phase = "(init)";
    private static long _lastBeatTicks = Stopwatch.GetTimestamp();
    private static long _frameCounter;
    private static volatile bool _stallReported;
    private static int _started;

    public static void Init(Serilog.ILogger log)
    {
        _log = log;
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;
        try
        {
            var t = new Thread(WatchLoop) { IsBackground = true, Name = "BossDiagWatchdog" };
            t.Start();
        }
        catch { }
    }

    /// <summary>每帧主线程调用，标记存活。</summary>
    public static void Heartbeat()
    {
        _frameCounter++;
        _phase = "frameTick#" + _frameCounter;
        _lastBeatTicks = Stopwatch.GetTimestamp();
        _stallReported = false;
    }

    /// <summary>记录一个 BOSS 阶段（会成为卡死时报告的 lastPhase），同时输出到普通日志。</summary>
    public static void Log(string phase)
    {
        _phase = phase;
        _lastBeatTicks = Stopwatch.GetTimestamp();
        try { _log?.Information("[BOSS-DIAG] {Phase}", phase); } catch { }
    }

    /// <summary>仅设置阶段（不写普通日志），用于高频/即将进入原生调用前的标记。</summary>
    public static void Phase(string phase)
    {
        _phase = phase;
        _lastBeatTicks = Stopwatch.GetTimestamp();
    }

    private static void WatchLoop()
    {
        while (true)
        {
            try { Thread.Sleep(1000); } catch { }
            double elapsed;
            try { elapsed = Stopwatch.GetElapsedTime(_lastBeatTicks).TotalSeconds; }
            catch { continue; }

            if (elapsed >= StallSeconds && !_stallReported)
            {
                _stallReported = true;
                WriteStall(elapsed, _phase);
            }
        }
    }

    private static void WriteStall(double seconds, string lastPhase)
    {
        var text =
            $"==== [BOSS-DIAG] MAIN THREAD STALLED {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
            $"stalledFor={seconds:F1}s lastPhase={lastPhase} ===={Environment.NewLine}";

        try
        {
            string dir;
            try { dir = Path.Combine(ModCore.Storage.FolderInfo.CoreRoot.FullPath, "logs"); }
            catch { dir = AppContext.BaseDirectory; }
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "mp_crash.log"), text);
        }
        catch { }

        try { _log?.Fatal("[BOSS-DIAG] MAIN THREAD STALLED stalledFor={Seconds:F1}s lastPhase={Phase}", seconds, lastPhase); } catch { }
    }
}
