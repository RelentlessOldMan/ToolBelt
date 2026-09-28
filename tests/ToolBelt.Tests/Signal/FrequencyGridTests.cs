using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class FrequencyGridTests
    {
        public void Resolution_IsSampleRateOverLength()
        {
            Check.Close(1.0, FrequencyGrid.Resolution(1024, 1024));
            Check.Close(46.875, FrequencyGrid.Resolution(1024, 48000), 1e-9);
        }

        public void BinFrequencies_SpanZeroToNyquist()
        {
            var f = FrequencyGrid.BinFrequencies(8, 8000);
            Check.Equal(5, f.Length);                 // N/2 + 1
            Check.Close(0.0, f[0]);
            Check.Close(1000.0, f[1]);                // 8000/8
            Check.Close(4000.0, f[4]);                // Nyquist = fs/2
        }

        public void BinFrequency_MatchesArray()
        {
            var f = FrequencyGrid.BinFrequencies(16, 44100);
            for (int k = 0; k <= 8; k++)
                Check.Close(f[k], FrequencyGrid.BinFrequency(k, 16, 44100), 1e-9, $"bin {k}");
        }

        public void NearestBin_Rounds()
        {
            // fftLen 10, fs 1000 -> resolution 100 Hz; 260 Hz -> bin 3 (300), 240 -> bin 2 (200).
            Check.Equal(3, FrequencyGrid.NearestBin(260, 10, 1000));
            Check.Equal(2, FrequencyGrid.NearestBin(240, 10, 1000));
            Check.Equal(0, FrequencyGrid.NearestBin(-50, 10, 1000));   // clamps low
            Check.Equal(5, FrequencyGrid.NearestBin(99999, 10, 1000)); // clamps to Nyquist bin N/2
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.Resolution(0, 1000));
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.Resolution(1024, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.BinFrequency(-1, 16, 1000));
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.BinFrequency(9, 16, 1000)); // > N/2
        }
    }
}
