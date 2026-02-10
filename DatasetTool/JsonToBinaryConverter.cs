using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DatasetTool
{
    public readonly record struct Candle1m(long TsNs, long O, long H, long L, long C, long V);

    public static class JsonToBinaryConverter
    {
        // "2010-06-06T22:00:00.000000000Z" -> epoch ns
        private static long ParseIsoToEpochNs(string isoZ)
        {
            int dot = isoZ.IndexOf('.');
            if (dot < 0)
            {
                var dto = DateTimeOffset.Parse(isoZ, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                return dto.ToUnixTimeMilliseconds() * 1_000_000L;
            }

            int z = isoZ.IndexOf('Z', dot);
            if (z < 0) z = isoZ.Length;

            string basePart = isoZ[..dot] + "Z";
            string fracPart = isoZ[(dot + 1)..z];

            var dto2 = DateTimeOffset.Parse(basePart, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            long baseNs = dto2.ToUnixTimeMilliseconds() * 1_000_000L;

            // normaliser fraction à 9 digits (ns)
            if (fracPart.Length > 9) fracPart = fracPart[..9];
            fracPart = fracPart.PadRight(9, '0');

            long extraNs = long.Parse(fracPart, CultureInfo.InvariantCulture);
            return baseNs + extraNs;
        }
    }
}