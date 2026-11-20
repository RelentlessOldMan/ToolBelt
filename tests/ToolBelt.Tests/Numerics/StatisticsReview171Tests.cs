using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 171 (statistics, tenth pass): each failed on the code before its fix.
    public sealed class StatisticsReview171Tests
    {
        private static double Ulps(double x, int steps) => BitConverter.Int64BitsToDouble(Math.Max(0, BitConverter.DoubleToInt64Bits(x) + steps));

        // The quantile x of an increasing f at p: f(x) is p to 1e-9, or (where an ulp of a subnormal x moves f further)
        // p lies between f four ulps either side.
        private static bool Inverts(Func<double, double> f, double x, double p, int sign) =>
            Math.Abs(f(x) - p) <= 1e-9 || (sign * (f(Ulps(x, -4)) - p) <= 0 && sign * (f(Ulps(x, 4)) - p) >= 0);

        // At tiny k the CDF at the smallest double passes ½ (k = 0.00143: 0.586), and past p = ½ the quantile came back as
        // 5e-324 where it lies below every double and is documented as 0.
        public void ChiSquareQuantile_BelowTheSmallestDouble_IsZeroInBothTails()
        {
            var rng = new Random(171);
            int zeros = 0, above = 0;
            for (int trial = 0; trial < 3000; trial++)
            {
                double k = Math.Exp(Math.Log(1e-4) + rng.NextDouble() * (Math.Log(0.05) - Math.Log(1e-4)));
                double p = 0.01 + 0.98 * rng.NextDouble(), q = 1 - p;
                bool below = Distributions.ChiSquareSurvival(double.Epsilon, k) < q;
                double x = Distributions.ChiSquareQuantile(p, k), y = Distributions.ChiSquareInverseSurvival(q, k);
                if (below)
                {
                    zeros++;
                    Check.Equal(0.0, x, $"ChiSquareQuantile({p:R}, {k:R})");
                    Check.Equal(0.0, y, $"ChiSquareInverseSurvival({q:R}, {k:R})");
                }
                else
                {
                    above++;
                    Check.True(x >= double.Epsilon && Inverts(v => Distributions.ChiSquareCdf(v, k), x, p, 1), $"ChiSquareQuantile({p:R}, {k:R}) = {x:R}");
                    Check.True(y >= double.Epsilon && Inverts(v => Distributions.ChiSquareSurvival(v, k), y, q, -1), $"ChiSquareInverseSurvival({q:R}, {k:R}) = {y:R}");
                }
            }
            Check.True(zeros > 500 && above > 500, $"{zeros} below, {above} above");
        }

        // The tie correction against a naive one in decimal; every value tied gives p = 1. Summed as t³ − t, the tie term
        // passed 2^53 and at na = 1,000,003, nb = 1,000,010 left the variance a rounding-sized negative: p was NaN.
        public void MannWhitneyU_TieCorrection_MatchesANaiveOneAndAllTiedIsOne()
        {
            var rng = new Random(1171);
            for (int trial = 0; trial < 300; trial++)
            {
                int na = rng.Next(1, 60), nb = rng.Next(1, 60), levels = rng.Next(1, 6);
                var a = Enumerable.Range(0, na).Select(_ => (double)rng.Next(levels)).ToArray();
                var b = Enumerable.Range(0, nb).Select(_ => (double)rng.Next(levels)).ToArray();
                var all = a.Concat(b).ToArray();
                int total = na + nb;
                decimal tie = 0, rankA = 0;
                foreach (var g in all.GroupBy(v => v)) { decimal t = g.Count(); tie += t * t * t - t; }
                foreach (double v in a) rankA += all.Count(w => w < v) + (all.Count(w => w == v) + 1) / 2m;
                double u1 = (double)(rankA - na * (na + 1) / 2m), meanU = na * (double)nb / 2;
                double factor = (double)((total + 1) - tie / ((decimal)total * (total - 1)));
                double sd = Math.Sqrt(na * (double)nb / 12 * factor);
                double expected = sd == 0 ? 1 : Math.Min(1, 2 * Distributions.NormalSurvival(Math.Max(0, (Math.Abs(u1 - meanU) - 0.5) / sd)));
                double got = HypothesisTests.MannWhitneyU(a, b).PValue;
                Check.True(Math.Abs(got - expected) <= 1e-12 + 1e-10 * expected, $"trial {trial}: {got:R}, expected {expected:R}");
            }
            foreach (var (na, nb) in new[] { (1000003, 1000010), (3, 5), (1, 1) })
                Check.Equal(1.0, HypothesisTests.MannWhitneyU(new double[na], new double[nb]).PValue, $"all tied, {na} and {nb}");
        }

        // SciPy's kstwo.sf (exact matrix form, or Pelz–Good past 140 points away from the far tail, as here). Stephens'
        // asymptotic form, used past 140 points wherever the tail was above 1e-4, was off by up to 2.7%.
        private static readonly (int N, double D, double P)[] KsTable =
        {
                (276, 0.07250732267680043, 0.10450917188955955),
                (4959, 0.02730720102290949, 0.0012045584152767838),
                (30968, 0.008865968057509782, 0.015278371023595768),
                (1859, 0.0395153957511164, 0.005857327302034252),
                (49603, 0.006660675174790997, 0.024415163457284197),
                (633, 0.04519521001516418, 0.1460817787411025),
                (5003, 0.02658977338103604, 0.001661917759216728),
                (539, 0.0392055444926902, 0.3692642465846653),
                (13861, 0.005528635140522858, 0.7884260945447958),
                (79060, 0.003299500789096739, 0.3548172053386176),
                (66977, 0.005698202412382018, 0.025730156374493163),
                (430, 0.08619536801873916, 0.00314732260491993),
                (47330, 0.00917452263113318, 0.0006886415030123689),
                (538, 0.08915120025176851, 0.00035977765505279313),
                (1169, 0.022632868152481878, 0.5792368679614979),
                (14155, 0.01597371793695412, 0.0014425122274243857),
                (2656, 0.01998219416905058, 0.23625137316799572),
                (719, 0.044425911180240925, 0.1135902310150495),
                (10654, 0.012588143279245562, 0.0677570322584784),
                (708, 0.08289700652803111, 0.00011114141998198235),
                (2108, 0.03462667289541616, 0.01245328806272505),
                (6520, 0.010807549613623721, 0.4285513462005104),
                (2587, 0.03358392281964238, 0.005708143015353133),
                (4593, 0.016124023587617244, 0.1814840504534262),
                (17167, 0.01211378237067075, 0.012863941946847653),
                (6732, 0.018150153336075607, 0.023410941541124455),
                (6066, 0.021668217645250037, 0.006619063894179016),
                (56521, 0.004002587079242377, 0.3246925670595824),
                (420, 0.10087148210853104, 0.00035755230597025144),
                (163, 0.14735427776973656, 0.0014899548974316385),
                (10164, 0.009278742922780959, 0.3435803815081886),
                (7427, 0.02095648161116926, 0.002895030831890225),
                (2299, 0.023083220906636025, 0.16984241221725982),
                (19966, 0.010020808031143144, 0.03602941327877096),
                (63362, 0.007238555738082898, 0.0026015427057637297),
                (9585, 0.01683035908858058, 0.008665522960873286),
                (384, 0.031201738554161387, 0.837214604152921),
                (7671, 0.007274481337126596, 0.8088026481160917),
                (283, 0.10007369358076365, 0.006407383718265528),
                (317, 0.10724360137689955, 0.0012508352300650441),
                (11641, 0.0104594960929229, 0.1554572033578826),
                (507, 0.09139317671232135, 0.00038989176124092263),
                (31201, 0.010452748399334183, 0.0021722802240986276),
                (13888, 0.011631422353699659, 0.04630583662531029),
                (604, 0.0817545326651174, 0.000584619587771958),
                (618, 0.057097380754078414, 0.03418653078108891),
                (8237, 0.02365815727267386, 0.00019467828748118387),
                (1485, 0.043798927387335304, 0.006505331734731556),        };

        public void KolmogorovSmirnovOneSample_PastHundredFortyPoints_MatchesSciPy()
        {
            foreach (var (n, d, p) in KsTable)
            {
                // D is exactly d: the first point's CDF is d, every other point sits half a step from its plateau.
                var sample = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
                var r = HypothesisTests.KolmogorovSmirnovOneSample(sample, x => x == 0 ? d : (x + 0.5) / n);
                Check.Equal(d, r.Statistic, "D at n = " + n);
                Check.True(Math.Abs(r.PValue - p) <= 5e-5 * p, $"n = {n}, D = {d:R}: {r.PValue:R}, SciPy {p:R}");
            }
        }


        // ---------- Paired ----------

        // Exact p-values over every sign pattern, from integer-valued differences summed as BigInteger.
        private static (double greater, double less) ExactPaired(double[] d)
        {
            int n = d.Length;
            var obs = d.Aggregate(BigInteger.Zero, (s, v) => s + new BigInteger(v));
            int greater = 0, less = 0;
            for (long mask = 0; mask < (1L << n); mask++)
            {
                var s = BigInteger.Zero;
                for (int i = 0; i < n; i++) s += new BigInteger(((mask >> i) & 1) == 0 ? d[i] : -d[i]);
                if (s >= obs) greater++;
                if (s <= obs) less++;
            }
            return ((double)greater / (1L << n), (double)less / (1L << n));
        }

        private static void CheckPaired(double[] d, string what)
        {
            var (g, l) = ExactPaired(d);
            var zero = new double[d.Length];
            var rg = PermutationTest.Paired(d, zero, new Random(1), 1 << 14, Alternative.Greater);
            Check.True(rg.IsExact, what);
            Check.Equal(g, rg.PValue, what + ", greater");
            Check.Equal(l, PermutationTest.Paired(d, zero, new Random(1), 1 << 14, Alternative.Less).PValue, what + ", less");
            Check.Equal(Math.Min(1, 2 * Math.Min(g, l)), PermutationTest.Paired(d, zero, new Random(1), 1 << 14, Alternative.TwoSided).PValue, what + ", two-sided");
            var sum = d.Aggregate(BigInteger.Zero, (s, v) => s + new BigInteger(v));
            double mean = (double)sum / d.Length;
            Check.True(Math.Abs(rg.Observed - mean) <= 4e-16 * Math.Abs(mean), $"{what}: mean {rg.Observed:R}, exact {mean:R}");
        }

        // Summed plainly, the differences {1e17, 3, −1e17} had mean 0 (for 1) and p = 0.75 (for ½); and sign patterns whose
        // sums differ by less than the 1e-12 tie tolerance of a large one read as ties: p = 0.5 for 3/128, 1 for 1/32.
        public void Paired_DifferencesThatCancel_GiveTheExactPValue()
        {
            CheckPaired(new[] { 1e17, 3, -1e17 }, "{1e17, 3, -1e17}");
            CheckPaired(new[] { 2, 5, -1, 2, 2e16, 6, 1 }, "{2, 5, -1, 2, 2e16, 6, 1}");
            CheckPaired(new[] { -19999999999999996, -2, -2, -6, -4, -6.0 }, "{-19999999999999996, -2, -2, -6, -4, -6}");
            var rng = new Random(4171);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(2, 11);
                var d = new double[n];
                for (int i = 0; i < n; i++) d[i] = rng.Next(-9, 10);
                // One or two huge values (multiples of 16 below 2^57, so exact) among small ones, now and then cancelling.
                int at = rng.Next(n);
                double big = Math.Floor(rng.NextDouble() * 9007199254740992.0) * 16;
                d[at] = rng.Next(2) == 0 ? big : -big;
                if (rng.Next(2) == 0) d[(at + 1) % n] = rng.Next(2) == 0 ? -d[at] : Math.Floor(rng.NextDouble() * 9007199254740992.0) * 16;
                CheckPaired(d, "{" + string.Join(", ", d.Select(v => v.ToString("R"))) + "}");
            }
        }

        // ---------- Pearson ----------

        // r from exact sums: every double is an integer times 2^−1074, so n·Σxy − Σx·Σy and the rest are exact as BigInteger.
        private static double ExactPearson(double[] x, double[] y)
        {
            BigInteger B(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                int e = (int)((bits >> 52) & 0x7FF);
                long m = bits & 0xFFFFFFFFFFFFFL;
                if (e != 0) m |= 1L << 52; else e = 1;
                var r = new BigInteger(m) << (e - 1);
                return bits < 0 ? -r : r;
            }
            int n = x.Length;
            BigInteger sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                var a = B(x[i]); var b = B(y[i]);
                sx += a; sy += b; sxx += a * a; syy += b * b; sxy += a * b;
            }
            var cxy = n * sxy - sx * sy; var cxx = n * sxx - sx * sx; var cyy = n * syy - sy * sy;
            if (cxy.IsZero) return 0;
            // r² = cxy²/(cxx·cyy) to more than 64 bits.
            var num = cxy * cxy; var den = cxx * cyy;
            int shift = Math.Max(0, (int)(BigInteger.Log(den, 2) - BigInteger.Log(num, 2)) + 80);
            double r2 = (double)((num << shift) / den) * Math.Pow(2, -shift);
            return cxy.Sign * Math.Sqrt(r2);
        }

        // About the rounded means alone, 16 points near 1e8 with a spread of 1e-5 gave r = 0.0807327123 for 0.0807328296.
        public void Pearson_OffsetData_MatchesExactSums()
        {
            double[] x = { 100000000.0000194, 100000000.0000266, 100000000.000013, 100000000.0000934, 100000000.0000066,
                100000000.0000041, 100000000.0000914, 100000000.0000844, 100000000.0000912, 100000000.0000796, 100000000.0000544,
                100000000.0000112, 100000000.0000008, 100000000.0000645, 100000000.0000379, 100000000.0000769 };
            double[] y = { -299999999.99992144, -299999999.9999154, -299999999.9999289, -299999999.9998319, -299999999.9999127,
                -299999999.9999695, -299999999.9999867, -299999999.9998612, -299999999.9999715, -299999999.9999125,
                -299999999.9999463, -299999999.9999434, -299999999.9999225, -299999999.9999855, -299999999.9999336,
                -299999999.9999644 };
            Check.True(Math.Abs(ExactPearson(x, y) - 0.08073282963210238) <= 1e-16, "the reference");
            Check.True(Math.Abs(Correlation.Pearson(x, y) - 0.08073282963210238) <= 1e-15, "the reviewer's 16 points: " + Correlation.Pearson(x, y).ToString("R"));
            var rng = new Random(5171);
            for (int trial = 0; trial < 400; trial++)
            {
                int n = rng.Next(3, 60);
                double cx = Math.Pow(10, rng.Next(0, 13)) * (rng.Next(2) == 0 ? 1 : -1), cy = Math.Pow(10, rng.Next(0, 13));
                double spreadX = Math.Abs(cx) * Math.Pow(10, -rng.Next(1, 13)), spreadY = cy * Math.Pow(10, -rng.Next(1, 13));
                double rho = 2 * rng.NextDouble() - 1;
                var xs = new double[n]; var ys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double u = rng.NextDouble() - 0.5, v = rho * u + (1 - Math.Abs(rho)) * (rng.NextDouble() - 0.5);
                    xs[i] = cx + spreadX * u;
                    ys[i] = cy + spreadY * v;
                }
                if (xs.Distinct().Count() < 2 || ys.Distinct().Count() < 2) continue;
                double want = ExactPearson(xs, ys), got = Correlation.Pearson(xs, ys);
                Check.True(Math.Abs(got - want) <= 8 * n * 1.1102230246251565e-16, $"trial {trial}, n = {n}, centres {cx:R}, {cy:R}: {got:R}, exact {want:R}");
            }
        }

        // ---------- the incomplete beta at large shapes ----------

        // (x, a, b, I_x(a, b), 1 − I_x(a, b), x·I′/min(I, 1 − I)) from a 40-digit continued fraction. The last column is the condition in
        // x, so the tolerance allows the error of an ulp of x (λ = a − (a+b)x rounds by about one there).
        private static readonly double[,] BetaTable =
        {
            { 0.906522564471844, 315595.83229051763, 32761.46878268354, 0.87479323503552950533, 0.12520676496447049467, 3.03e+03 },
            { 0.03318034637465749, 43766.749708083335, 1259302.3229385063, 0.00482768504957335043, 0.99517231495042664957, 615 },
            { 0.7735063817046574, 2478.0391952207938, 687.819614626086, 0.10471768961980475302, 0.89528231038019524698, 179 },
            { 0.634912249476123, 1295521.067590662, 739450.1840871724, 1.8137764169177641924e-7, 0.99999981862235830822, 9.91e+03 },
            { 0.8136169997193612, 264917.6014598954, 59350.62558469504, 4.4084580678522130553e-7, 0.99999955915419321478, 6.06e+03 },
            { 0.0014392023575295274, 644.0699030299777, 463799.18002806045, 0.83189631959986810411, 0.16810368040013189589, 38.4 },
            { 0.31210717674944083, 3033.562984834448, 6050.340905409025, 4.0440538407181734248e-6, 0.99999595594615928183, 302 },
            { 0.15444892479529054, 13629.467121280682, 78470.99073374958, 0.99999997648551186914, 2.3514488130859481845e-8, 727 },
            { 0.21285460715271437, 8745.796648916998, 34096.5239047975, 0.99999532863775938736, 4.6713622406126356006e-6, 497 },
            { 0.037989903001611605, 108.88077243855632, 4198.0733718511, 0.99999847062519975225, 1.5293748002477459494e-6, 59.7 },
            { 0.6573814583979783, 1960723.764578087, 1019220.521413707, 0.015652622046143073666, 0.98434737795385692633, 6e+03 },
            { 0.7744178312493218, 24234.366932278084, 7408.344629417163, 0.99985119488257106339, 0.00014880511742893660973, 1.28e+03 },
            { 0.05406736072632331, 3340.3268207969822, 54504.99382356593, 0.000055296085984526559242, 0.99994470391401547344, 238 },
            { 0.9859523206915676, 35977.157064936706, 566.8961471144037, 0.99001135516930376592, 0.0099886448306962340793, 4.34e+03 },
            { 0.08670581489614064, 1451.2024942465919, 13474.658606196497, 3.7170914144038279973e-6, 0.99999628290858559617, 180 },
            { 0.00719369815386979, 107.5831928941125, 10539.298819363383, 0.00045549664961530488859, 0.99954450335038469511, 33.3 },
            { 0.4689198195706667, 2273691.3731296025, 2566867.123083671, 0.00022126597287257919108, 0.99977873402712742081, 7.78e+03 },
            { 0.003308405135913747, 1082.5277356243857, 294621.0397984435, 0.00053610230531406134681, 0.99946389769468593865, 113 },
            { 0.8139085277694916, 50535.29868243171, 11208.850321151309, 0.0017537241210226995886, 0.99824627587897730041, 1.66e+03 },
            { 0.9968559109698963, 25911.315088248917, 108.85299551678571, 0.99757542760934359045, 0.0024245723906564095482, 9.4e+03 },
            { 0.26252141639633425, 6738.592546907563, 17442.89695368852, 7.2408318422299992046e-9, 0.99999999275916815777, 545 },
            { 0.9744414571966483, 146137.42535734866, 3543.4542538322366, 1.4068898779929838559e-6, 0.99999859311012200702, 1.15e+04 },
            { 0.07122464614106466, 3707.1115459065013, 52537.2071668889, 0.99999965243894209356, 3.4756105790644063124e-7, 334 },
            { 0.028034155319486755, 454.46386965013085, 17359.392193465213, 0.98147511919252840957, 0.018524880807471590435, 54.7 },
            { 0.7647966455859784, 160862.4791592992, 48043.02112332689, 8.021118940231182038e-9, 0.99999999197888105977, 4.78e+03 },
            { 0.9986811815110035, 467311.69527689257, 546.9449116667198, 0.0019073050748518588587, 0.99809269492514814114, 5.88e+04 },
            { 0.06627072991126415, 417.3135893509907, 5597.259485838172, 0.17132161264307093219, 0.82867838735692906781, 30.9 },
            { 0.0061856441630683, 207.9609035402069, 40225.2399286896, 0.99724948937247762534, 0.0027505106275223746608, 47.4 },
            { 0.992625610165128, 119817.70820454562, 1086.9309353356416, 0.99999999988307925544, 1.1692074455672285732e-10, 2.71e+04 },
            { 0.8764784893456058, 1062448.62985518, 149334.65841389418, 0.16901888617421314471, 0.83098111382578685529, 4.37e+03 },
            { 0.9949935806516089, 64431.5429764927, 333.39720457219374, 0.68767779134959640872, 0.31232220865040359128, 4.08e+03 },
            { 0.0007617753809961135, 1250.9430280657807, 1528923.838713424, 0.0069439455829479101391, 0.99305605441705208986, 96.3 },
            { 0.07571332800971285, 216.35747206780422, 2516.820264593541, 0.25577463332447653057, 0.74422536667552346943, 18.9 },
            { 0.12871340853992333, 598.1074195096994, 4848.358516803483, 0.99999016227744748332, 9.8377225525166791272e-6, 124 },
            { 0.07946896797754437, 1138.413495316412, 13700.827690696759, 0.8952631055385746521, 0.1047368944614253479, 61.7 },
            { 0.0013331545965628439, 460.1442177634674, 430293.03667647246, 0.99999963413874854099, 3.6586125145900639172e-7, 119 },
            { 0.7842033929283175, 14237.20227886486, 3736.3207202851754, 0.0047232949324944613921, 0.99527670506750553861, 740 },
            { 0.9830766983255211, 2194091.4207996726, 37899.13501813002, 0.74355881819383519455, 0.25644118180616480545, 1.43e+04 },
            { 0.0031770953837643857, 1983.836216756044, 557521.7537462217, 7.8686179024773646156e-7, 0.99999921313820975226, 215 },
            { 0.07829979962777478, 32635.98402033113, 392203.34110379964, 0.99984297407538031515, 0.00015702592461968485302, 729 },
            { 0.0016991687723732537, 883.1657242800816, 558022.878872304, 0.98599436231255983519, 0.014005637687440164812, 77.7 },
        };

        // (f, d1, d2, P(F > f), |d ln S / d ln f|), the same way.
        private static readonly double[,] FTable =
        {
            { 1.0354852154620406, 29700.066616176417, 12341.53790130479, 0.010877226916483707601, 173 },
            { 0.9768125854612121, 10917.466618790078, 1319572.7902422582, 0.95683933907234330112, 155 },
            { 0.9019283915643932, 208.36708988244723, 786099.0861186511, 0.84199687909270993288, 15.1 },
            { 1.0638570341798617, 4032.806523368797, 21652.906519277745, 0.0050024163942110738337, 121 },
            { 1.2055315518814451, 296.93109115939967, 2249379.2079763073, 0.0087147500447434355811, 35.2 },
            { 0.9654654415067326, 139848.48073218905, 5507.674270757378, 0.96598642159408923175, 115 },
            { 1.0436430408557653, 65735.66506625923, 3709.647301264087, 0.038241517283993332906, 89.9 },
            { 0.9885871403413867, 142410.32674539622, 49542.887636961415, 0.94046728104614426314, 270 },
            { 0.9679752644302737, 625.8078706350082, 71337.58182486036, 0.7093740290080868122, 20.5 },
            { 0.9412242085201865, 2118.4401524337322, 290133.9078099789, 0.97348777061934188895, 73.6 },
            { 0.9922707374974657, 40488.91423998773, 322.09113261709007, 0.549399683526881832, 11.1 },
            { 1.0433794933043128, 176025.8256308418, 2161.1000840080737, 0.085672548420497815631, 58.9 },
            { 0.9675106296663742, 391.9633677558455, 32408.127447590465, 0.66774706245752467635, 15 },
            { 1.0237597244103613, 16785.730260665987, 21663.44326800115, 0.053032371545800465716, 140 },
            { 0.9771684500488431, 6780.137169786982, 7922.376083608063, 0.83798027897253015157, 64.7 },
            { 1.1041428769434625, 383.9666905573564, 574793.2013780734, 0.077939973757151100298, 26.8 },
            { 0.9953542820444135, 2903.0799382895866, 6270.707755878071, 0.55675295094721501444, 28 },
            { 0.8677952221678958, 69055.20162709209, 477.01472792701924, 0.9879497643396613317, 42 },
            { 1.0323247887402134, 3844.340343516124, 7487.101901361054, 0.12743001611896879532, 58.5 },
            { 1.0048439152228554, 140068.1949846677, 146857.06277108748, 0.18011207838507246075, 276 },
            { 1.0089736986736424, 29877.321148452444, 10549.838899207736, 0.28950400228056064975, 73.7 },
            { 0.9993558332448299, 165410.7172276015, 778.2966543999955, 0.51175180095213859987, 16.1 },
            { 1.0650811167552456, 2362.684898592391, 14059.649276966522, 0.021301836404130386197, 77.4 },
            { 1.034217873689876, 7666.565487030357, 6280.412012242176, 0.08132586703202613628, 76.8 },
            { 0.9660239449454024, 3127.12659791894, 30425.523091161573, 0.9011719071876962285, 65.7 },
            { 1.0133655559324077, 7035.287145109012, 157369.5678134628, 0.2185113389971141589, 78.6 },
            { 0.991726163697967, 16409.039073017873, 144724.59228655853, 0.76093797808652187497, 111 },
            { 0.881003479498556, 15357.487119520372, 938.695394472185, 0.99682219773832903952, 66.2 },
            { 0.9827342611456957, 1725.677848226524, 12864.827633663364, 0.68084838772206808744, 30.7 },
            { 0.8606698820778852, 674.3659093169791, 4198.918879618827, 0.99373713143348340436, 46.3 },
            { 1.0830122597435654, 3988.6572027908583, 2432779.252004761, 0.00015101696652003121516, 177 },
            { 1.033372664180706, 3649.3574242149057, 571911.2960988078, 0.078808789643869211444, 80.3 },
        };

        // Within (4·cond + 30) ulps of the smaller tail, and an ulp of the value itself.
        private static bool Close(double got, double want, double cond) =>
            Math.Abs(got - want) <= (4 * cond + 30) * 1.1102230246251565e-16 * Math.Min(want, 1 - want) + 1.1102230246251565e-16 * want;

        private static MethodInfo PrivateBetaI(Type type) =>
            type.GetMethod("BetaI", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(type.Name + ".BetaI not found");

        // The Lentz fraction lost 100 to 400 ulps at large shapes: I at x = 0.0017, a = 883, b = 558,023 was 0.9859943623125793
        // (for …5598), and F's upper tail at f = 1.0334, d1 = 3649, d2 = 571,911 was 0.0788087896438341 (for …6892).
        public void RegularizedBeta_LargeShapes_MatchesAFortyDigitReference()
        {
            var copies = new[] { typeof(Distributions), typeof(SpecialFunctions), typeof(Anova), typeof(GaugeRR) };
            for (int i = 0; i < BetaTable.GetLength(0); i++)
            {
                double x = BetaTable[i, 0], a = BetaTable[i, 1], b = BetaTable[i, 2], v = BetaTable[i, 3], vc = BetaTable[i, 4], cond = BetaTable[i, 5];
                double got = SpecialFunctions.RegularizedBetaI(x, a, b);
                Check.True(Close(got, v, cond), $"I({x:R}, {a:R}, {b:R}) = {got:R}, reference {v:R}");
                foreach (var type in copies)
                {
                    var args = new object[] { x, 1 - x, a, b, 0.0, 0.0 };
                    PrivateBetaI(type).Invoke(null, args);
                    Check.True(Close((double)args[4], v, cond) && Close((double)args[5], vc, cond),
                        $"{type.Name}.BetaI({x:R}, {a:R}, {b:R}) = {args[4]:R}, {args[5]:R}");
                }
            }
            for (int i = 0; i < FTable.GetLength(0); i++)
            {
                double f = FTable[i, 0], d1 = FTable[i, 1], d2 = FTable[i, 2], s = FTable[i, 3], cond = FTable[i, 4];
                double got = Distributions.FSurvival(f, d1, d2), anova = Anova.FUpperTailProbability(f, d1, d2);
                Check.True(Close(got, s, cond), $"FSurvival({f:R}, {d1:R}, {d2:R}) = {got:R}, reference {s:R}");
                Check.True(Close(anova, s, cond), $"Anova.FUpperTailProbability({f:R}, {d1:R}, {d2:R}) = {anova:R}, reference {s:R}");
            }
        }
    }
}
