using System;
using System.Collections.Generic;

namespace LastBell.Network.Endurance
{
    // Diagnostic clock candidate only. The fixture authenticates and matches probe replies.
    // Times are milliseconds from independent monotonic clocks, not calendar time.
    public sealed class ClockEstimator
    {
        private sealed class Sample
        {
            public double At, Offset, Rtt;
        }
        private readonly List<Sample> samples = new List<Sample>();
        public const int Capacity = 8;
        public const double MaxAgeMs = 2000;
        private int generation = -1;
        public int Generation { get { return generation; } }
        public int Count { get { return samples.Count; } }
        public ClockEstimator(int generation) { Reset(generation); }
        public void Reset(int generation)
        {
            if (generation < 0 || generation <= Generation)
                throw new ArgumentException("Invalid clock generation");
            this.generation = generation; samples.Clear();
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public bool Add(int generation, double localSent, double hostReceived, double hostSent, double localReceived)
        {
            if (generation != Generation || !Finite(localSent) || !Finite(hostReceived) ||
                !Finite(hostSent) || !Finite(localReceived) || localReceived < localSent || hostSent < hostReceived)
                return false;
            double rtt = (localReceived - localSent) - (hostSent - hostReceived);
            if (!Finite(rtt) || rtt < 0 || rtt > 1000 ||
                (samples.Count > 0 && localReceived < samples[samples.Count - 1].At)) return false;
            double offset = ((hostReceived - localSent) + (hostSent - localReceived)) / 2;
            if (!Finite(offset)) return false;
            samples.Add(new Sample { At = localReceived, Offset = offset, Rtt = rtt });
            if (samples.Count > Capacity) samples.RemoveAt(0);
            return true;
        }
        public bool TryEstimate(double localNow, out double hostNow, out double uncertaintyMs)
        {
            hostNow = 0; uncertaintyMs = 0;
            if (!Finite(localNow)) return false;
            Sample best = null;
            foreach (Sample sample in samples)
            {
                double age = localNow - sample.At;
                if (age < 0 || age > MaxAgeMs) continue;
                if (best == null || sample.Rtt < best.Rtt || (sample.Rtt == best.Rtt && sample.At > best.At)) best = sample;
            }
            if (best == null) return false;
            hostNow = localNow + best.Offset;
            // Assumes nonnegative path delays and <=1000 ppm relative drift. Not a security guarantee.
            uncertaintyMs = best.Rtt / 2 + (localNow - best.At) * 0.001 + 1;
            return Finite(hostNow);
        }
    }
}
