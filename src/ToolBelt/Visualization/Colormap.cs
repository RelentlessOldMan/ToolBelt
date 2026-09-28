// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Maps a normalized value in [0, 1] to a color by linearly interpolating between color stops. Ships with
    /// a few presets, including a perceptually-uniform Viridis <i>approximation</i> (anchor points, not the
    /// full 256-entry table). Construct your own from custom stops.
    /// </summary>
    public sealed class Colormap
    {
        private readonly (double Position, Rgba Color)[] _stops;

        public Colormap(params (double Position, Rgba Color)[] stops)
        {
            if (stops is null) throw new ArgumentNullException(nameof(stops));
            if (stops.Length < 2) throw new ArgumentException("A colormap needs at least two stops.", nameof(stops));
            _stops = (( double, Rgba)[])stops.Clone();
            Array.Sort(_stops, (a, b) => a.Position.CompareTo(b.Position));
        }

        /// <summary>Maps <paramref name="t"/> (clamped to the stop range) to a color.</summary>
        public Rgba Map(double t)
        {
            if (double.IsNaN(t)) t = _stops[0].Position;
            if (t <= _stops[0].Position) return _stops[0].Color;
            if (t >= _stops[_stops.Length - 1].Position) return _stops[_stops.Length - 1].Color;

            for (int i = 1; i < _stops.Length; i++)
            {
                if (t <= _stops[i].Position)
                {
                    var a = _stops[i - 1];
                    var b = _stops[i];
                    double f = (t - a.Position) / (b.Position - a.Position);
                    return Lerp(a.Color, b.Color, f);
                }
            }
            return _stops[_stops.Length - 1].Color;
        }

        private static Rgba Lerp(Rgba a, Rgba b, double f)
            => new Rgba(Channel(a.R, b.R, f), Channel(a.G, b.G, f), Channel(a.B, b.B, f), Channel(a.A, b.A, f));

        private static byte Channel(byte a, byte b, double f)
        {
            int v = (int)Math.Round(a + (b - a) * f);
            return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }

        public static readonly Colormap Grayscale = new Colormap(
            (0.0, Rgba.Black), (1.0, Rgba.White));

        /// <summary>Black → red → yellow → white.</summary>
        public static readonly Colormap Hot = new Colormap(
            (0.0, Rgba.Black), (0.375, new Rgba(230, 0, 0)), (0.75, new Rgba(255, 230, 0)), (1.0, Rgba.White));

        /// <summary>Cyan → magenta.</summary>
        public static readonly Colormap Cool = new Colormap(
            (0.0, new Rgba(0, 255, 255)), (1.0, new Rgba(255, 0, 255)));

        /// <summary>A perceptually-uniform Viridis approximation (anchor sampling).</summary>
        public static readonly Colormap Viridis = new Colormap(
            (0.00, new Rgba(68, 1, 84)),
            (0.25, new Rgba(59, 82, 139)),
            (0.50, new Rgba(33, 145, 140)),
            (0.75, new Rgba(94, 201, 98)),
            (1.00, new Rgba(253, 231, 37)));
    }
}
