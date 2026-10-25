// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>How many significant digits an uncertainty keeps.</summary>
    public enum UncertaintyRule
    {
        /// <summary>
        /// The Particle Data Group convention: from the three leading digits of the uncertainty, 100–354 keep two digits,
        /// 355–949 keep one, and 950–999 round up to 1000 and keep two.
        /// </summary>
        ParticleDataGroup,
        TwoSignificantDigits,
        OneSignificantDigit,
    }

    /// <summary>How <see cref="Uncertainty.Format"/> writes a value with its uncertainty.</summary>
    public enum UncertaintyStyle
    {
        /// <summary>"12.346 ± 0.012"</summary>
        PlusMinus,
        /// <summary>"12.346(12)" — the uncertainty in units of the last digit.</summary>
        Parenthetical,
    }

    /// <summary>
    /// Reports a measured value at the precision its uncertainty justifies: the uncertainty is rounded to one or two
    /// significant digits and the value to the same decimal place — so a report shows "12.346 ± 0.012" instead of fifteen
    /// meaningless digits. Values and uncertainties of any magnitude are handled (rounding that would carry past
    /// <see cref="double.MaxValue"/> throws <see cref="ArgumentOutOfRangeException"/>); very large or small ones are written
    /// with a shared exponent ("(1.234 ± 0.030)e-7"), down to subnormal uncertainties. An uncertainty below 10⁻¹⁷ of
    /// the value is finer than the value itself can be known and is rejected. Formatting is culture-invariant.
    /// </summary>
    public static class Uncertainty
    {
        /// <summary>
        /// The rounded value and uncertainty, and the number of decimal places they share (negative when rounding to tens,
        /// hundreds, ...).
        /// </summary>
        public static (double Value, double Uncertainty, int Decimals) Round(double value, double uncertainty, UncertaintyRule rule = UncertaintyRule.ParticleDataGroup)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be finite.");
            if (!(uncertainty > 0) || double.IsInfinity(uncertainty)) throw new ArgumentOutOfRangeException(nameof(uncertainty), uncertainty, "Uncertainty must be positive and finite.");
            // Rounding 1e300 to 1e-300's decimal place overflowed 10³⁰¹ and returned NaN.
            if (uncertainty < Math.Abs(value) * 1e-17)
                throw new ArgumentOutOfRangeException(nameof(uncertainty), uncertainty, "Uncertainty is below the value's own precision (10^-17 of it).");

            // Leading three digits of the uncertainty (100 ≤ lead < 1000) and its decimal exponent.
            int exponent = (int)Math.Floor(Math.Log10(uncertainty));
            int lead = (int)Math.Floor(Shift10(uncertainty, 2 - exponent) + 1e-9); // 10^(exponent−2) is 0 for subnormals
            if (lead >= 1000) { lead /= 10; exponent++; }                     // log10 rounded down a hair
            if (lead < 100) { lead *= 10; exponent--; }                        // ...or up

            int digits = rule switch
            {
                UncertaintyRule.OneSignificantDigit => 1,
                UncertaintyRule.TwoSignificantDigits => 2,
                _ => lead <= 354 ? 2 : (lead <= 949 ? 1 : 2),
            };
            // PDG: 950–999 is rounded up to 1000 and shown with two digits — i.e. "10" one decade higher (0.096 → 0.10).
            if (rule == UncertaintyRule.ParticleDataGroup && lead >= 950) exponent++;
            int decimals = digits - 1 - exponent;
            double u = RoundTo(uncertainty, decimals);
            RequireInRange(u, nameof(uncertainty));
            // Rounding may carry into a new leading digit (0.0996 → 0.100; PDG's 950–999 → 0.10): keep `digits` digits of the result.
            int roundedExponent = (int)Math.Floor(Math.Log10(u) + 1e-12);
            if (roundedExponent > exponent)
            {
                decimals = digits - 1 - roundedExponent;
                u = RoundTo(uncertainty, decimals);
                RequireInRange(u, nameof(uncertainty));
            }
            double rounded = RoundTo(value, decimals);
            RequireInRange(rounded, nameof(value));
            return (rounded, u, decimals);
        }

        // Rounding near double.MaxValue can carry past it (1.79e308 to the nearest 1e308 is 2e308).
        private static void RequireInRange(double rounded, string paramName)
        {
            if (double.IsInfinity(rounded))
                throw new ArgumentOutOfRangeException(paramName, "Rounded, this exceeds the double range.");
        }

        /// <summary>
        /// The value and its uncertainty as text, rounded per <paramref name="rule"/>. Plain notation is used when the shared
        /// decimal place is between 10⁻⁶ and 10⁶ and the value is below 10⁷; otherwise both share an exponent.
        /// </summary>
        public static string Format(double value, double uncertainty, UncertaintyStyle style = UncertaintyStyle.PlusMinus,
            UncertaintyRule rule = UncertaintyRule.ParticleDataGroup)
        {
            var (v, u, decimals) = Round(value, uncertainty, rule);
            if (decimals <= 6 && decimals >= -6 && Math.Abs(v) < 1e7)
                return Compose(v, u, Math.Max(decimals, 0), style, null);

            int e = (int)Math.Floor(Math.Log10(Math.Max(Math.Abs(v), u)));
            int d = Math.Max(decimals + e, 0);
            return Compose(RoundTo(Shift10(v, -e), d), RoundTo(Shift10(u, -e), d), d, style, e);
        }

        private static string Compose(double v, double u, int decimals, UncertaintyStyle style, int? exponent)
        {
            string fmt = decimals > 0 ? "F" + decimals.ToString(CultureInfo.InvariantCulture) : "F0";
            string vs = Clean(v.ToString(fmt, CultureInfo.InvariantCulture));
            string text;
            if (style == UncertaintyStyle.Parenthetical)
            {
                // The uncertainty in units of the last shown digit of the value.
                double inLastDigit = Math.Round(u * Math.Pow(10, Math.Max(decimals, 0)), MidpointRounding.AwayFromZero);
                text = vs + "(" + inLastDigit.ToString("0", CultureInfo.InvariantCulture) + ")";
                return exponent is int e1 ? text + "e" + e1.ToString(CultureInfo.InvariantCulture) : text;
            }
            text = vs + " ± " + u.ToString(fmt, CultureInfo.InvariantCulture);
            return exponent is int e2 ? "(" + text + ")e" + e2.ToString(CultureInfo.InvariantCulture) : text;
        }

        private static string Clean(string s) => s.StartsWith("-", StringComparison.Ordinal) && s.Trim('-', '0', '.').Length == 0 ? s.Substring(1) : s;

        private static double RoundTo(double x, int decimals)
        {
            if (decimals >= 0 && decimals <= 15) return Math.Round(x, decimals, MidpointRounding.AwayFromZero);
            if (x == 0 || Math.Log10(Math.Abs(x)) + decimals >= 17) return x; // already coarser than that place
            return Shift10(Math.Round(Shift10(x, decimals), MidpointRounding.AwayFromZero), -decimals);
        }

        // x·10^k in steps that stay in range: 10^k alone overflows (or underflows to 0) for |k| > 308.
        private static double Shift10(double x, int k)
        {
            while (k > 300) { x *= 1e300; k -= 300; }
            while (k < -300) { x /= 1e300; k += 300; }
            return k >= 0 ? x * Math.Pow(10, k) : x / Math.Pow(10, -k);
        }
    }
}
