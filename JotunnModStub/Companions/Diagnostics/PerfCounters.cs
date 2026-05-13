using System;
using System.Diagnostics;

namespace JotunnModStub.Companions.Diagnostics
{
    // Fixed-size circular buffer of microsecond samples — no GC on the hot path.
    internal static class PerfCounters
    {
        private const int Capacity = 4096;
        private static readonly double[] _aiSamples = new double[Capacity];
        private static int _aiHead;
        private static int _aiCount;

        private static readonly double[] _leashSamples = new double[Capacity];
        private static int _leashHead;
        private static int _leashCount;

        public static Stopwatch StartAi()
        {
            return Stopwatch.StartNew();
        }

        public static void StopAi(Stopwatch sw)
        {
            if (sw == null) return;
            sw.Stop();
            Record(_aiSamples, ref _aiHead, ref _aiCount, sw.Elapsed.TotalMilliseconds);
        }

        public static Stopwatch StartLeash()
        {
            return Stopwatch.StartNew();
        }

        public static void StopLeash(Stopwatch sw)
        {
            if (sw == null) return;
            sw.Stop();
            Record(_leashSamples, ref _leashHead, ref _leashCount, sw.Elapsed.TotalMilliseconds);
        }

        private static void Record(double[] buf, ref int head, ref int count, double value)
        {
            buf[head] = value;
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        public struct Stats
        {
            public double AvgMs;
            public double P99Ms;
            public int Samples;
        }

        public static Stats AiStats()  => StatsOf(_aiSamples, _aiCount);
        public static Stats LeashStats() => StatsOf(_leashSamples, _leashCount);

        private static Stats StatsOf(double[] buf, int count)
        {
            var s = new Stats { Samples = count };
            if (count == 0) return s;

            double sum = 0;
            var copy = new double[count];
            Array.Copy(buf, copy, count);
            for (int i = 0; i < count; i++) sum += copy[i];
            s.AvgMs = sum / count;

            Array.Sort(copy);
            int idx = (int)Math.Floor(0.99 * (count - 1));
            s.P99Ms = copy[idx];
            return s;
        }
    }
}
