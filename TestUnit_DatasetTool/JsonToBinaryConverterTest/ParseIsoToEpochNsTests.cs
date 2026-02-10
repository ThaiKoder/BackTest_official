using System;
using System.Globalization;
using Xunit;

namespace TestUnit_DatasetTool.JsonToBinaryConverterTest
{

    public class ParseIsoToEpochNsTests
    {
        [Fact]
        public void NoFraction_Should_Return_Correct_Nanoseconds()
        {
            var iso = "2010-06-07T15:17:00Z";

            long ns = ParseIsoToEpochNs(iso);

            var dto = DateTimeOffset.Parse(
                iso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

            long expected = dto.ToUnixTimeMilliseconds() * 1_000_000L;

            Assert.Equal(expected, ns);
        }

        [Fact]
        public void Milliseconds_Should_Be_Converted_To_Nanoseconds()
        {
            var iso = "2022-06-10T12:30:00.123Z";

            long ns = ParseIsoToEpochNs(iso);

            Assert.EndsWith("123000000", ns.ToString());
        }

        [Fact]
        public void Microseconds_Should_Be_Padded_To_Nanoseconds()
        {
            var iso = "2022-06-10T12:30:00.123456Z";

            long ns = ParseIsoToEpochNs(iso);

            Assert.EndsWith("123456000", ns.ToString());
        }

        [Fact]
        public void Nanoseconds_Should_Be_Kept_As_Is()
        {
            var iso = "2022-06-10T12:30:00.123456789Z";

            long ns = ParseIsoToEpochNs(iso);

            Assert.EndsWith("123456789", ns.ToString());
        }

        [Fact]
        public void More_Than_9_Digits_Should_Be_Truncated()
        {
            var iso = "2022-06-10T12:30:00.123456789999Z";

            long ns = ParseIsoToEpochNs(iso);

            Assert.EndsWith("123456789", ns.ToString());
        }

        [Fact]
        public void Less_Than_9_Digits_Should_Be_Right_Padded()
        {
            var iso = "2022-06-10T12:30:00.1Z";

            long ns = ParseIsoToEpochNs(iso);

            Assert.EndsWith("100000000", ns.ToString());
        }

        private static long ParseIsoToEpochNs(string isoZ)
        {
            int dot = isoZ.IndexOf('.');
            if (dot < 0)
            {
                var dto = DateTimeOffset.Parse(
                    isoZ,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal);

                return dto.ToUnixTimeMilliseconds() * 1_000_000L;
            }

            int z = isoZ.IndexOf('Z', dot);
            if (z < 0) z = isoZ.Length;

            string basePart = isoZ[..dot] + "Z";
            string fracPart = isoZ[(dot + 1)..z];

            var dto2 = DateTimeOffset.Parse(
                basePart,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

            long baseNs = dto2.ToUnixTimeMilliseconds() * 1_000_000L;

            if (fracPart.Length > 9) fracPart = fracPart[..9];
            fracPart = fracPart.PadRight(9, '0');

            long extraNs = long.Parse(fracPart, CultureInfo.InvariantCulture);
            return baseNs + extraNs;
        }
    }


}
