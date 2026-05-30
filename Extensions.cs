using System;
using System.Globalization;
using NinjaPricer.API.PoeNinja;

namespace NinjaPricer;

public static class Extensions
{
    public static string FormatNumber(this double number, int significantDigits, double maxInvertValue = 0, bool forceDecimals = false)
    {
        if (double.IsNaN(number))
        {
            return "n/a";
        }

        if (double.IsPositiveInfinity(number))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(number))
        {
            return "-inf";
        }

        if (number == 0)
        {
            return "0";
        }

        if (Math.Abs(number) <= 1e-10)
        {
            return "~0";
        }

        if (Math.Abs(number) < maxInvertValue)
        {
            var inverted = 1 / number;
            if (!double.IsFinite(inverted))
            {
                return "n/a";
            }

            if (Math.Abs(inverted) > (double)decimal.MaxValue)
            {
                return $"1/{inverted.ToString("0.#e+0", CultureInfo.InvariantCulture)}";
            }

            return $"1/{Math.Round((decimal)inverted, 1).ToString("#.#", CultureInfo.InvariantCulture)}";
        }

        significantDigits = Math.Clamp(significantDigits, 0, 28);
        var format = $"#,##0.{new string(forceDecimals ? '0' : '#', significantDigits)}";
        if (Math.Abs(number) > (double)decimal.MaxValue)
        {
            return number.ToString("0.##e+0", CultureInfo.InvariantCulture);
        }

        return Math.Round((decimal)number, significantDigits).ToString(format, CultureInfo.InvariantCulture);
    }

    public static bool IsChanceable(this object item)
    {
        return true;
    }
}