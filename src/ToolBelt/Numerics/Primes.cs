// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Primality testing and factorization over non-negative 64-bit integers using trial division.
    /// Suitable for everyday values; not a general big-integer number-theory library.
    /// </summary>
    public static class Primes
    {
        /// <summary>True if <paramref name="n"/> is prime. Values below 2 are not prime.</summary>
        public static bool IsPrime(long n)
        {
            if (n < 2) return false;
            if (n < 4) return true;          // 2, 3
            if ((n & 1) == 0) return false;  // even
            if (n % 3 == 0) return false;

            // Trial-divide by 6k±1 up to sqrt(n).
            for (long i = 5; i <= n / i; i += 6)
            {
                if (n % i == 0 || n % (i + 2) == 0) return false;
            }
            return true;
        }

        /// <summary>The smallest prime strictly greater than <paramref name="n"/>.</summary>
        public static long Next(long n)
        {
            if (n < 2) return 2;
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

            var factors = new List<(long, int)>();
            for (long d = 2; d <= n / d; d++)
            {
                if (n % d != 0) continue;
                int exp = 0;
                while (n % d == 0) { n /= d; exp++; }
                factors.Add((d, exp));
            }
            if (n > 1) factors.Add((n, 1)); // remaining prime cofactor
            return factors;
        }

        /// <summary>All primes up to and including <paramref name="limit"/> via the Sieve of Eratosthenes.</summary>
        public static IReadOnlyList<long> Sieve(int limit)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be non-negative.");
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
