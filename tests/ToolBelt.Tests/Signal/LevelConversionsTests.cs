using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class LevelConversionsTests
    {
        public void AmplitudeDb_KnownValues()
        {
            Check.Close(0.0, LevelConversions.AmplitudeToDb(1.0), 1e-12);
            Check.Close(20.0, LevelConversions.AmplitudeToDb(10.0), 1e-9);
            Check.Close(6.0206, LevelConversions.AmplitudeToDb(2.0), 1e-4); // doubling ~ +6 dB
        }

        public void PowerDb_KnownValues()
        {
            Check.Close(0.0, LevelConversions.PowerToDb(1.0), 1e-12);
            Check.Close(10.0, LevelConversions.PowerToDb(10.0), 1e-9);
            Check.Close(3.0103, LevelConversions.PowerToDb(2.0), 1e-4); // doubling ~ +3 dB
        }

        public void RoundTrips()
        {
            var rng = new Random(3);
            for (int i = 0; i < 100; i++)
            {
                double db = rng.NextDouble() * 120 - 60;
                Check.Close(db, LevelConversions.AmplitudeToDb(LevelConversions.DbToAmplitude(db)), 1e-9);
                Check.Close(db, LevelConversions.PowerToDb(LevelConversions.DbToPower(db)), 1e-9);
            }
        }

        public void Rms_OfSine()
        {
            const int n = 2000;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Sqrt(2) * Math.Sin(2 * Math.PI * 5 * i / n); // amp sqrt2 -> RMS 1
            Check.Close(1.0, LevelConversions.Rms(x), 1e-2);
        }

        public void DbFs_FullScaleSineNear_Minus3()
        {
            const int n = 4000;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Sin(2 * Math.PI * 7 * i / n); // full-scale amplitude 1
            Check.Close(-3.0103, LevelConversions.DbFs(x), 1e-2);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => LevelConversions.AmplitudeToDb(0));
            Check.Throws<ArgumentOutOfRangeException>(() => LevelConversions.PowerToDb(-1));
            Check.Throws<ArgumentException>(() => LevelConversions.Rms(Array.Empty<double>()));
            Check.Throws<ArgumentOutOfRangeException>(() => LevelConversions.DbFs(new double[] { 1 }, 0));
        }
    }
}
