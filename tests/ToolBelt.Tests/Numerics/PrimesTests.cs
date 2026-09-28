using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class PrimesTests
    {
        public void IsPrime_KnownValues()
        {
            Check.False(Primes.IsPrime(0));
            Check.False(Primes.IsPrime(1));
            Check.True(Primes.IsPrime(2));
            Check.True(Primes.IsPrime(3));
            Check.False(Primes.IsPrime(4));
            Check.True(Primes.IsPrime(97));
            Check.False(Primes.IsPrime(99));
            Check.True(Primes.IsPrime(7919));   // 1000th prime
            Check.False(Primes.IsPrime(7917));
            Check.False(Primes.IsPrime(-5));
        }

        public void LargePrimeAndComposite()
        {
            Check.True(Primes.IsPrime(1_000_003));
            Check.False(Primes.IsPrime(1_000_005));
        }

        public void Next()
        {
            Check.Equal(2, Primes.Next(0));
            Check.Equal(2, Primes.Next(1));
            Check.Equal(3, Primes.Next(2));
            Check.Equal(11, Primes.Next(7));
            Check.Equal(101, Primes.Next(97));
        }

        public void Factorize()
        {
            Check.Equal(0, Primes.Factorize(1).Count);
            Check.True(Primes.Factorize(360).SequenceEqual(new[] { (2L, 3), (3L, 2), (5L, 1) })); // 2^3 * 3^2 * 5
            Check.True(Primes.Factorize(97).SequenceEqual(new[] { (97L, 1) }));
            Check.Throws<ArgumentOutOfRangeException>(() => Primes.Factorize(0));
        }

        public void Sieve()
        {
            Check.True(Primes.Sieve(1).Count == 0);
            Check.True(Primes.Sieve(30).SequenceEqual(new long[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 }));
            Check.Throws<ArgumentOutOfRangeException>(() => Primes.Sieve(-1));
        }

        // Property: Sieve agrees with IsPrime, and factors multiply back to n.
        public void Property_Consistency()
        {
            var sieved = new System.Collections.Generic.HashSet<long>(Primes.Sieve(5000));
            for (long n = 0; n <= 5000; n++)
                Check.Equal(sieved.Contains(n), Primes.IsPrime(n), $"sieve vs IsPrime at {n}");

            var rng = new Random(99);
            for (int t = 0; t < 500; t++)
            {
                long n = rng.Next(1, 1_000_000);
                long product = 1;
                foreach (var (p, e) in Primes.Factorize(n))
                {
                    Check.True(Primes.IsPrime(p), $"factor {p} of {n} not prime");
                    for (int k = 0; k < e; k++) product *= p;
                }
                Check.Equal(n, product, $"factors of {n} must multiply back");
            }
        }
    }
}
