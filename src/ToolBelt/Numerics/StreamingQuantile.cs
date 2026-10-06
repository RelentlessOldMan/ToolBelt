// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Estimates one quantile of a stream in a single pass with constant memory — the P² algorithm of Jain &amp; Chlamtac
    /// (1985), which keeps five markers whose heights are adjusted by piecewise-parabolic interpolation as values arrive.
    /// For "the 99th percentile of a hundred million latencies" without storing them. Until five values have been seen
    /// the exact (linearly interpolated) quantile of what has arrived is returned.
    /// <para>
    /// Accuracy is empirical, not a guaranteed bound: on smooth distributions with thousands of samples the estimate is
    /// typically within a small fraction of the inter-quantile spread (the tests check uniform, normal and exponential
    /// streams); it is weakest for extreme quantiles of very few samples and for heavily discrete or multimodal data.
    /// Not thread-safe. For several quantiles of one stream use <see cref="StreamingQuantiles"/>.
    /// </para>
    /// </summary>
    public sealed class StreamingQuantile
    {
        private readonly double _p;
        private readonly double[] _q = new double[5];   // marker heights
        private readonly double[] _n = new double[5];   // actual marker positions (1-based)
        private readonly double[] _np = new double[5];  // desired positions
        private readonly double[] _dn;                  // desired-position increments
        private readonly List<double> _initial = new List<double>(5);

        /// <param name="probability">The quantile to track, in (0, 1) — e.g. 0.5 for the median, 0.99 for p99.</param>
        public StreamingQuantile(double probability)
        {
            if (!(probability > 0 && probability < 1)) throw new ArgumentOutOfRangeException(nameof(probability), probability, "Probability must be in (0, 1).");
            _p = probability;
            _dn = new[] { 0, probability / 2, probability, (1 + probability) / 2, 1 };
        }

        public double Probability => _p;

        /// <summary>The number of values added.</summary>
        public long Count { get; private set; }

        /// <summary>Smallest value seen (NaN before any).</summary>
        public double Min => Count == 0 ? double.NaN : (Count < 5 ? Extreme(false) : _q[0]);

        /// <summary>Largest value seen (NaN before any).</summary>
        public double Max => Count == 0 ? double.NaN : (Count < 5 ? Extreme(true) : _q[4]);

        /// <summary>The current estimate (NaN before any value).</summary>
        public double Estimate
        {
            get
            {
                if (Count == 0) return double.NaN;
                if (Count >= 5) return _q[2];
                var sorted = new List<double>(_initial);
                sorted.Sort();
                double h = (sorted.Count - 1) * _p;
                int lo = (int)Math.Floor(h);
                int hi = Math.Min(lo + 1, sorted.Count - 1);
                return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
            }
        }

        /// <summary>Adds a value. NaN is rejected (it would corrupt the marker ordering).</summary>
        public void Add(double value)
        {
            // ±∞ would turn the marker interpolation into ∞ − ∞ = NaN and poison every later estimate.
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Only finite values can be added to a quantile estimate.", nameof(value));
            Count++;
            if (Count <= 5)
            {
                _initial.Add(value);
                if (Count == 5) Initialise();
                return;
            }

            int k;
            if (value < _q[0]) { _q[0] = value; k = 0; }
            else if (value >= _q[4]) { _q[4] = value; k = 3; }
            else
            {
                k = 0;
                while (k < 3 && value >= _q[k + 1]) k++;
            }
            for (int i = k + 1; i < 5; i++) _n[i]++;
            for (int i = 0; i < 5; i++) _np[i] += _dn[i];

            for (int i = 1; i <= 3; i++)
            {
                double d = _np[i] - _n[i];
                if ((d >= 1 && _n[i + 1] - _n[i] > 1) || (d <= -1 && _n[i - 1] - _n[i] < -1))
                {
                    int s = d > 0 ? 1 : -1;
                    double qp = Parabolic(i, s);
                    _q[i] = _q[i - 1] < qp && qp < _q[i + 1] ? qp : Linear(i, s);
                    _n[i] += s;
                }
            }
        }

        public void AddRange(IEnumerable<double> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            foreach (double v in values) Add(v);
        }

        private void Initialise()
        {
            _initial.Sort();
            for (int i = 0; i < 5; i++) { _q[i] = _initial[i]; _n[i] = i + 1; }
            _np[0] = 1; _np[1] = 1 + 2 * _p; _np[2] = 1 + 4 * _p; _np[3] = 3 + 2 * _p; _np[4] = 5;
        }

        private double Parabolic(int i, int s)
            => _q[i] + s / (_n[i + 1] - _n[i - 1]) *
               ((_n[i] - _n[i - 1] + s) * (_q[i + 1] - _q[i]) / (_n[i + 1] - _n[i]) +
                (_n[i + 1] - _n[i] - s) * (_q[i] - _q[i - 1]) / (_n[i] - _n[i - 1]));

        private double Linear(int i, int s) => _q[i] + s * (_q[i + s] - _q[i]) / (_n[i + s] - _n[i]);

        private double Extreme(bool max)
        {
            double e = _initial[0];
            foreach (double v in _initial) e = max ? Math.Max(e, v) : Math.Min(e, v);
            return e;
        }
    }

    /// <summary>Tracks several quantiles of one stream (one <see cref="StreamingQuantile"/> per probability).</summary>
    public sealed class StreamingQuantiles
    {
        private readonly StreamingQuantile[] _trackers;

        public StreamingQuantiles(params double[] probabilities)
        {
            if (probabilities is null || probabilities.Length == 0) throw new ArgumentException("At least one probability is required.", nameof(probabilities));
            _trackers = new StreamingQuantile[probabilities.Length];
            for (int i = 0; i < probabilities.Length; i++) _trackers[i] = new StreamingQuantile(probabilities[i]);
        }

        public long Count => _trackers[0].Count;

        public void Add(double value)
        {
            foreach (var t in _trackers) t.Add(value);
        }

        /// <summary>The estimate for the i-th probability given at construction.</summary>
        public double this[int index] => _trackers[index].Estimate;

        /// <summary>Probability → estimate.</summary>
        public IReadOnlyDictionary<double, double> Estimates()
        {
            var d = new Dictionary<double, double>();
            foreach (var t in _trackers) d[t.Probability] = t.Estimate;
            return d;
        }
    }
}
