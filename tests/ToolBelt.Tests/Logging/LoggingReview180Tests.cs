using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    // Round 180 (logging, diagnostics, CLI, eleventh pass): each failed on the code before its fix.
    public sealed class LoggingReview180Tests
    {
        // Max, Max, -Max, -Max (the plain sum overflows) plus a few small samples whose own sum is exact in a double: the
        // mean is that sum over the count, one correctly rounded division, against the registry's mean.
        public void Metrics_MeanAfterOverflow_KeepsSmallSamples()
        {
            var rng = new Random(180);
            for (int trial = 0; trial < 2000; trial++)
            {
                var m = new MetricsRegistry();
                var samples = new List<double> { double.MaxValue, double.MaxValue, -double.MaxValue, -double.MaxValue };
                int n = 1 + rng.Next(6);
                int scale = -1074 + rng.Next(1100);           // small samples are integers below 2^20 times 2^scale
                double unit = Math.Pow(2, Math.Max(scale, -1074));
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    double x = (rng.Next(1 << 20) - (1 << 19)) * unit;
                    samples.Add(x);
                    sum += x;                                 // exact: at most 23 bits of one scale
                }
                // Shuffled, so the small ones fall between the large ones.
                for (int i = samples.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (samples[i], samples[j]) = (samples[j], samples[i]); }
                foreach (double v in samples) m.Record("t", v);
                double expected = sum / samples.Count;
                double mean = m.Snapshot().Timers["t"].Mean;
                Check.Equal(BitConverter.DoubleToInt64Bits(expected == 0 ? 0 : expected), BitConverter.DoubleToInt64Bits(mean));
            }

            var r = new MetricsRegistry();
            foreach (double v in new[] { double.MaxValue, double.MaxValue, -double.MaxValue, -double.MaxValue, 1e-20 }) r.Record("a", v);
            Check.Equal(1e-20 / 5, r.Snapshot().Timers["a"].Mean);
            r.Record("b", 3e-300);
            Check.Equal(3e-300, r.Snapshot().Timers["b"].Mean);
            foreach (double v in new[] { double.MaxValue, double.MaxValue, -double.MaxValue, -double.MaxValue, 1e-5 }) r.Record("c", v);
            Check.Equal(1e-5 / 5, r.Snapshot().Timers["c"].Mean);
            r.Record("d", double.MaxValue);
            r.Record("d", double.MaxValue);
            Check.Equal(double.MaxValue, r.Snapshot().Timers["d"].Mean);
        }

        // One -0 sample: the mean was -0 on .NET 8 and +0 on .NET Framework (Math.Min and Math.Max on signed zeros).
        public void Metrics_NegativeZeroSample_MeanIsPositiveZeroEverywhere()
        {
            var m = new MetricsRegistry();
            m.Record("z", -0.0);
            Check.Equal(0L, BitConverter.DoubleToInt64Bits(m.Snapshot().Timers["z"].Mean));
            m.Record("y", 5);
            m.Record("y", -5);
            Check.Equal(0L, BitConverter.DoubleToInt64Bits(m.Snapshot().Timers["y"].Mean));
        }

        // A one-tick timeout was Task.Delay(0): a probe that completes a moment later lost the race.
        public async Task HealthCheck_SubMillisecondTimeout_DoesNotRaceAZeroDelay()
        {
            int timedOut = 0;
            for (int i = 0; i < 50; i++)
            {
                var check = new HealthCheck().Add("soon", ct => Task.Run(() => ProbeResult.Healthy()), TimeSpan.FromTicks(1));
                HealthReport report = await check.RunAsync();
                if (report.Probes[0].TimedOut) timedOut++;
            }
            Check.True(timedOut < 10, "timed out " + timedOut + " of 50");
        }

        // Probes ran on the pool, where a blocking probe starved the timeout of a thread (one core: a 400 ms probe against a
        // 100 ms timeout reported Healthy). Both the synchronous probe and an async probe's synchronous start now run on
        // threads of their own.
        public async Task HealthCheck_ProbesRunOffThePool()
        {
            bool? syncOnPool = null, asyncOnPool = null;
            var check = new HealthCheck()
                .Add("sync", () => { syncOnPool = Thread.CurrentThread.IsThreadPoolThread; return ProbeResult.Healthy(); })
                .Add("async", ct => { asyncOnPool = Thread.CurrentThread.IsThreadPoolThread; return Task.FromResult(ProbeResult.Healthy()); });
            HealthReport report = await check.RunAsync();
            Check.Equal(HealthStatus.Healthy, report.Status);
            Check.Equal(false, syncOnPool);
            Check.Equal(false, asyncOnPool);
        }

        // A writer that stops the spinner while drawing for the Message setter: Stop waited on the loop while holding the
        // gate, and the loop was blocked on the gate in Tick, so both hung.
        public void Spinner_StopFromWriterDuringMessageDraw_DoesNotDeadlock()
        {
            for (int trial = 0; trial < 5; trial++)
            {
                var writer = new StoppingWriter();
                var spinner = new ConsoleSpinner("start", writer, interactive: true, frames: ConsoleSpinner.Ascii, interval: TimeSpan.FromMilliseconds(1));
                writer.Spinner = spinner;
                Thread.Sleep(20);   // the loop is ticking
                var setter = Task.Run(() => spinner.Message = "STOP");
                Check.True(setter.Wait(TimeSpan.FromSeconds(10)), "the Message setter hung");
                Check.True(writer.Stopped);
                spinner.Dispose();
            }
        }

        private sealed class StoppingWriter : TextWriter
        {
            public volatile ConsoleSpinner? Spinner;
            public volatile bool Stopped;
            private int _stopping;
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) { }
            public override void Write(string? value)
            {
                if (value != null && value.Contains("STOP") && Interlocked.Exchange(ref _stopping, 1) == 0)
                {
                    Thread.Sleep(50);   // the loop's next Tick blocks on the gate the setter holds
                    Spinner!.Stop();
                    Stopped = true;
                }
            }
        }

        // Input is trimmed; option text was not, so " Beta " could not be chosen by its text.
        public void Prompt_Choose_MatchesTrimmedOptionText()
        {
            var o = new StringWriter();
            var p = new Prompt(new StringReader(" Beta \n"), o, true);
            Check.Equal(1, p.Choose("q", new[] { "Alpha", " Beta " }));
            p = new Prompt(new StringReader("beta\n"), o, true);
            Check.Equal(1, p.Choose("q", new[] { "Alpha", "\tBeta" }));
        }

        // A connect timeout of 0.5 ms was Wait(0): the connect to a live loopback collector was never waited for, so every
        // first send failed into the back-off.
        public void Syslog_SubMillisecondConnectTimeout_StillWaitsForTheConnect()
        {
            int sent = 0;
            for (int trial = 0; trial < 10; trial++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    using var sink = new SyslogSink("127.0.0.1", port, SyslogTransport.Tcp) { ConnectTimeout = TimeSpan.FromTicks(5000) };
                    sink.Emit(new LogEvent(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", "m", null));
                    if (sink.FailedSends == 0) sent++;
                }
                finally { listener.Stop(); }
            }
            Check.True(sent >= 7, "sent " + sent + " of 10");
        }

        // A drain timeout of 0.5 ms was Wait(0): Dispose returned before the consumer had written an event already queued.
        public void AsyncLogSink_SubMillisecondDrainTimeout_StillWaits()
        {
            int drained = 0;
            for (int trial = 0; trial < 20; trial++)
            {
                var inner = new CountingSink();
                var sink = new AsyncLogSink(inner, drainTimeout: TimeSpan.FromTicks(5000));
                sink.Emit(new LogEvent(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", "m", null));
                sink.Dispose();
                if (Volatile.Read(ref inner.Count) == 1) drained++;
            }
            Check.True(drained >= 14, "drained " + drained + " of 20");
        }

        private sealed class CountingSink : ILogSink
        {
            public int Count;
            public void Emit(LogEvent logEvent) => Interlocked.Increment(ref Count);
            public void Dispose() { }
        }
    }
}
