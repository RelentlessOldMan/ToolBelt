// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Primality testing and factorization over non-negative 64-bit integers: deterministic Miller-Rabin (exact for
    /// every long) and Pollard-Brent rho, so any long is tested in microseconds and factored in milliseconds (trial
    /// division took 35 s to confirm the largest long prime). Not a general big-integer number-theory library.
    /// </summary>
    public static class Primes
    {
        /// <summary>True if <paramref name="n"/> is prime. Values below 2 are not prime.</summary>
        public static bool IsPrime(long n)
        {
            if (n < 2) return false;
            foreach (long p in SmallPrimes)
            {
                if (n == p) return true;
                if (n % p == 0) return false;
            }
            if (n < 41 * 41) return true;

            // Miller-Rabin with the first twelve primes as bases is exact below 3.3·10²⁴, so for every long.
            ulong m = (ulong)n, d = m - 1;
            int r = 0;
            while ((d & 1) == 0) { d >>= 1; r++; }
            foreach (long a in SmallPrimes)
            {
                ulong x = PowMod((ulong)a, d, m);
                if (x == 1 || x == m - 1) continue;
                bool composite = true;
                for (int i = 1; i < r && composite; i++)
                {
                    x = MulMod(x, x, m);
                    if (x == m - 1) composite = false;
                }
                if (composite) return false;
            }
            return true;
        }

        private static readonly long[] SmallPrimes = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 };

        // a·b mod m for a, b < m < 2⁶³.
        private static ulong MulMod(ulong a, ulong b, ulong m)
        {
#if NET7_0_OR_GREATER
            return (ulong)((UInt128)a * b % m);
#else
            // Shift-and-add: every partial sum stays below 2m < 2⁶⁴.
            ulong result = 0;
            a %= m;
            while (b != 0)
            {
                if ((b & 1) != 0) { result += a; if (result >= m) result -= m; }
                a += a; if (a >= m) a -= m;
                b >>= 1;
            }
            return result;
#endif
        }

        private static ulong PowMod(ulong b, ulong e, ulong m)
        {
            ulong result = 1;
            b %= m;
            while (e != 0)
            {
                if ((e & 1) != 0) result = MulMod(result, b, m);
                b = MulMod(b, b, m);
                e >>= 1;
            }
            return result;
        }

        /// <summary>The largest prime a <see cref="long"/> can hold (2⁶³ − 25).</summary>
        public const long LargestLongPrime = 9223372036854775783;

        /// <summary>
        /// The smallest prime strictly greater than <paramref name="n"/>. Throws <see cref="OverflowException"/>
        /// when that prime would not fit in a <see cref="long"/> (n ≥ <see cref="LargestLongPrime"/>).
        /// </summary>
        public static long Next(long n)
        {
            if (n < 2) return 2;
            // Past the last long prime the candidate wrapped negative and the search never ended.
            if (n >= LargestLongPrime) throw new OverflowException("There is no prime above 2^63 - 25 that fits in a long.");
            long candidate = n + 1;
            if ((candidate & 1) == 0 && candidate != 2) candidate++;
            while (!IsPrime(candidate))
                candidate += 2;
            return candidate;
        }

        /// <summary>
        /// The prime factorization of <paramref name="n"/> as ascending (prime, exponent) pairs.
        /// Requires <paramref name="n"/> &gt;= 1; the factorization of 1 is empty.
        /// </summary>
        public static IReadOnlyList<(long Prime, int Exponent)> Factorize(long n)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), n, "Value must be >= 1.");

            // Trial division clears the small factors cheaply; Pollard rho splits whatever is left (trial division
            // up to √n took 54 s for (2³¹ − 1)²).
            var primes = new List<long>();
            for (long d = 2; d < 1000 && d <= n / d; d += d == 2 ? 1 : 2)
                while (n % d == 0) { primes.Add(d); n /= d; }
            if (n > 1) Split(n, primes);
            primes.Sort();

            var factors = new List<(long, int)>();
            foreach (long p in primes)
            {
                if (factors.Count > 0 && factors[factors.Count - 1].Item1 == p)
                    factors[factors.Count - 1] = (p, factors[factors.Count - 1].Item2 + 1);
                else
                    factors.Add((p, 1));
            }
            return factors;
        }

        private static void Split(long n, List<long> primes)
        {
            if (n == 1) return;
            if (IsPrime(n)) { primes.Add(n); return; }
            long f = PollardBrent(n);
            Split(f, primes);
            Split(n / f, primes);
        }

        // A non-trivial factor of an odd composite n with no factor below 1000 (Brent's cycle-finding variant).
        private static long PollardBrent(long n)
        {
            ulong m = (ulong)n;
            for (ulong c = 1; ; c++)
            {
                ulong y = 2, x = 2, q = 1, ys = 2, g = 1;
                const int batch = 128;
                for (ulong span = 1; g == 1; span <<= 1)
                {
                    x = y;
                    for (ulong i = 0; i < span; i++) y = Step(y, c, m);
                    for (ulong k = 0; k < span && g == 1; k += batch)
                    {
                        ys = y;
                        ulong limit = Math.Min(batch, span - k);
                        for (ulong i = 0; i < limit; i++)
                        {
                            y = Step(y, c, m);
                            q = MulMod(q, x > y ? x - y : y - x, m);
                        }
                        g = Gcd(q, m);
                    }
                }
                if (g == m) // the batch overshot: retrace one step at a time
                {
                    do
                    {
                        ys = Step(ys, c, m);
                        g = Gcd(x > ys ? x - ys : ys - x, m);
                    } while (g == 1);
                }
                if (g != m) return (long)g; // otherwise this c cycled without splitting n; try the next
            }
        }

        private static ulong Step(ulong y, ulong c, ulong m)
        {
            ulong s = MulMod(y, y, m) + c; // < m + c, far below 2⁶⁴
            return s >= m ? s - m : s;
        }

        private static ulong Gcd(ulong a, ulong b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }

        /// <summary>The largest <c>limit</c> <see cref="Sieve"/> accepts (its flag array must fit in one .NET array).</summary>
        public const int MaxSieveLimit = 2_000_000_000;

        /// <summary>All primes up to and including <paramref name="limit"/> via the Sieve of Eratosthenes.
        /// <paramref name="limit"/> must be in [0, <see cref="MaxSieveLimit"/>].</summary>
        public static IReadOnlyList<long> Sieve(int limit)
        {
            if (limit < 0 || limit > MaxSieveLimit)
                throw new ArgumentOutOfRangeException(nameof(limit), limit, $"Limit must be in [0, {MaxSieveLimit}].");
            var result = new List<long>();
            if (limit < 2) return result;

            var composite = new bool[limit + 1];
            for (int i = 2; i <= limit; i++)
            {
                if (composite[i]) continue;
                result.Add(i);
                for (long j = (long)i * i; j <= limit; j += i)
                    composite[j] = true;
            }
            return result;
        }
    }
}
