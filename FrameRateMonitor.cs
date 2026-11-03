using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace FpsOverlay
{
    /// <summary>A snapshot of frame pacing for one process.</summary>
    public readonly record struct FrameStats(
        int ProcessId,
        string ProcessName,
        double Fps,
        double FrameTimeMs,
        double OnePercentLowFps);

    /// <summary>
    /// Measures the frame rate of any running game by listening to the same ETW
    /// (Event Tracing for Windows) events that PresentMon, CapFrameX and similar
    /// tools use: every call to IDXGISwapChain::Present (Direct3D 10/11/12) or
    /// IDirect3DDevice9::Present (Direct3D 9) emits an event tagged with the
    /// calling process ID. Counting those per process gives that process's FPS.
    /// No injection into the game is needed, so it is anti-cheat friendly.
    /// </summary>
    public sealed class FrameRateMonitor : IDisposable
    {
        // Microsoft-Windows-DXGI
        private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private const int DxgiPresentStart = 42;
        private const int DxgiPresentMultiplaneOverlayStart = 55;

        // Microsoft-Windows-D3D9
        private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private const int D3D9PresentStart = 1;

        private const string SessionName = "FpsOverlay-PresentTrace";

        /// <summary>How much frame history to keep per process (used for 1% lows).</summary>
        private const double HistoryMs = 10_000;

        /// <summary>Window used for the headline FPS number.</summary>
        private const double FpsWindowMs = 1_000;

        /// <summary>A process that hasn't presented for this long is no longer "rendering".</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

        private sealed class ProcessFrames
        {
            public readonly Queue<double> Timestamps = new(); // ETW time, ms
            public DateTime LastSeenUtc;
        }

        private readonly object _lock = new();
        private readonly Dictionary<int, ProcessFrames> _frames = new();
        private readonly Dictionary<int, string> _names = new();
        private readonly int _ownPid = Environment.ProcessId;

        private TraceEventSession? _session;
        private Thread? _thread;

        public string? StartupError { get; private set; }

        public void Start()
        {
            if (TraceEventSession.IsElevated() != true)
            {
                StartupError = "Run as administrator to read FPS";
                return;
            }

            try
            {
                // Creating a session with an existing name stops the old one, so a
                // previous instance that crashed doesn't leave us locked out.
                _session = new TraceEventSession(SessionName) { StopOnDispose = true };
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue);
                _session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose, ulong.MaxValue);
                _session.Source.AllEvents += OnEvent;
            }
            catch (Exception ex)
            {
                StartupError = "ETW session failed: " + ex.Message;
                _session?.Dispose();
                _session = null;
                return;
            }

            var source = _session.Source;
            _thread = new Thread(() =>
            {
                try { source.Process(); }
                catch { /* session was disposed */ }
            })
            {
                IsBackground = true,
                Name = "ETW present listener",
            };
            _thread.Start();
        }

        private void OnEvent(TraceEvent e)
        {
            int id = (int)e.ID;
            bool isPresent =
                (e.ProviderGuid == DxgiProvider && (id == DxgiPresentStart || id == DxgiPresentMultiplaneOverlayStart)) ||
                (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart);

            if (!isPresent || e.ProcessID <= 0 || e.ProcessID == _ownPid)
                return;

            double t = e.TimeStampRelativeMSec;
            lock (_lock)
            {
                if (!_frames.TryGetValue(e.ProcessID, out var pf))
                {
                    pf = new ProcessFrames();
                    _frames[e.ProcessID] = pf;
                }

                pf.Timestamps.Enqueue(t);
                pf.LastSeenUtc = DateTime.UtcNow;
                while (pf.Timestamps.Count > 0 && t - pf.Timestamps.Peek() > HistoryMs)
                    pf.Timestamps.Dequeue();
            }
        }

        /// <summary>
        /// Returns frame stats for <paramref name="preferredPid"/> (normally the
        /// foreground window's process) if it is rendering; otherwise for whichever
        /// process is presenting the most frames. Null if nothing is rendering.
        /// </summary>
        public FrameStats? GetStats(int preferredPid)
        {
            int pid;
            double[] ts;

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                foreach (var stale in _frames.Where(kv => now - kv.Value.LastSeenUtc > StaleAfter)
                                             .Select(kv => kv.Key).ToList())
                {
                    _frames.Remove(stale);
                    lock (_names) _names.Remove(stale); // PIDs get reused
                }

                if (_frames.ContainsKey(preferredPid))
                    pid = preferredPid;
                else if (_frames.Count > 0)
                    pid = _frames.OrderByDescending(kv => kv.Value.Timestamps.Count).First().Key;
                else
                    return null;

                ts = _frames[pid].Timestamps.ToArray();
            }

            if (ts.Length < 2)
                return null;

            double last = ts[^1];

            // Headline FPS: frames presented over the last second.
            int firstInWindow = Array.FindIndex(ts, x => last - x <= FpsWindowMs);
            int framesInWindow = ts.Length - firstInWindow - 1;
            double windowSpan = last - ts[firstInWindow];
            double fps = framesInWindow > 0 && windowSpan > 0 ? framesInWindow * 1000.0 / windowSpan : 0;
            double frameTime = fps > 0 ? 1000.0 / fps : 0;

            // 1% low: average FPS of the slowest 1% of frames over the full history.
            var intervals = new double[ts.Length - 1];
            for (int i = 1; i < ts.Length; i++)
                intervals[i - 1] = ts[i] - ts[i - 1];
            Array.Sort(intervals);
            int worstCount = Math.Max(1, intervals.Length / 100);
            double worstAvg = 0;
            for (int i = intervals.Length - worstCount; i < intervals.Length; i++)
                worstAvg += intervals[i];
            worstAvg /= worstCount;
            double onePercentLow = worstAvg > 0 ? 1000.0 / worstAvg : 0;

            return new FrameStats(pid, GetProcessName(pid), fps, frameTime, onePercentLow);
        }

        private string GetProcessName(int pid)
        {
            lock (_names)
            {
                if (_names.TryGetValue(pid, out var cached))
                    return cached;

                string name;
                try { name = Process.GetProcessById(pid).ProcessName; }
                catch { name = "pid " + pid; }
                _names[pid] = name;
                return name;
            }
        }

        public void Dispose()
        {
            try { _session?.Dispose(); } catch { }
            _session = null;
        }
    }
}
