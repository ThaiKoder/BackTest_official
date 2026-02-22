using System;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Threading;

namespace DatasetTool
{
    public static class JsonToBinaryIndex
    {
        public const int RecordSize = 8;

        public static void BuildRangesFromJsonFilenames_FastestPractical(string jsonDir, string outBinPath, CancellationToken ct = default)
        {
            if (!Directory.Exists(jsonDir))
                throw new DirectoryNotFoundException(jsonDir);

            using var outFs = new FileStream(
                outBinPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, options: FileOptions.SequentialScan);

            byte[] writeBuf = new byte[1 << 20];
            int w = 0;

            var e = new FileSystemEnumerable<string>(
                jsonDir,
                (ref FileSystemEntry entry) => entry.FileName.ToString(),
                new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    MatchCasing = MatchCasing.CaseInsensitive
                });

            e.ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory &&
                entry.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

            int idx = 0;


            foreach (var fileName in e)
            {
                ct.ThrowIfCancellationRequested();

                //int startInt = (idx * 2 + 1) % 100;
                //int endInt = (idx * 2 + 2) % 100;

                //if (endInt > byte.MaxValue)
                //    throw new InvalidOperationException("Dépassement byte (idx*2).");

                //byte start = (byte)startInt;
                //byte end = (byte)endInt;

                ReadOnlySpan<char> span = fileName.AsSpan();

                // "20251214" (positions fixes selon ton format)
                ReadOnlySpan<char> dateStart = span.Slice(10, 8);
                ReadOnlySpan<char> dateEnd = span.Slice(19, 8);

                // Conversion ultra rapide vers uint YYYYMMDD
                uint startYmd =
                    (uint)(
                        (dateStart[0] - '0') * 10000000 +
                        (dateStart[1] - '0') * 1000000 +
                        (dateStart[2] - '0') * 100000 +
                        (dateStart[3] - '0') * 10000 +
                        (dateStart[4] - '0') * 1000 +
                        (dateStart[5] - '0') * 100 +
                        (dateStart[6] - '0') * 10 +
                        (dateStart[7] - '0'));

                uint endYmd =
                    (uint)(
                        (dateEnd[0] - '0') * 10000000 +
                        (dateEnd[1] - '0') * 1000000 +
                        (dateEnd[2] - '0') * 100000 +
                        (dateEnd[3] - '0') * 10000 +
                        (dateEnd[4] - '0') * 1000 +
                        (dateEnd[5] - '0') * 100 +
                        (dateEnd[6] - '0') * 10 +
                        (dateEnd[7] - '0'));

                // Flush si nécessaire
                if (w + RecordSize > writeBuf.Length)
                {
                    outFs.Write(writeBuf, 0, w);
                    w = 0;
                }

                //// Écriture
                //writeBuf[w++] = start;
                //writeBuf[w++] = end;

                // startYmd (little endian)
                writeBuf[w++] = (byte)(startYmd);
                writeBuf[w++] = (byte)(startYmd >> 8);
                writeBuf[w++] = (byte)(startYmd >> 16);
                writeBuf[w++] = (byte)(startYmd >> 24);

                // endYmd (little endian)
                writeBuf[w++] = (byte)(endYmd);
                writeBuf[w++] = (byte)(endYmd >> 8);
                writeBuf[w++] = (byte)(endYmd >> 16);
                writeBuf[w++] = (byte)(endYmd >> 24);

                idx++;
            }

            if (w > 0)
                outFs.Write(writeBuf, 0, w);
        }

        private static byte ExtractYearByte(string fileName)
        {
            // On travaille sur le "stem" sans extension
            string s = Path.GetFileNameWithoutExtension(fileName);

            // scan rapide: cherche 4 digits consécutifs
            for (int i = 0; i + 3 < s.Length; i++)
            {
                char c0 = s[i];
                if (c0 < '0' || c0 > '9') continue;

                char c1 = s[i + 1], c2 = s[i + 2], c3 = s[i + 3];
                if ((uint)(c1 - '0') > 9 || (uint)(c2 - '0') > 9 || (uint)(c3 - '0') > 9) continue;

                int year = (c0 - '0') * 1000 + (c1 - '0') * 100 + (c2 - '0') * 10 + (c3 - '0');

                // bornes raisonnables
                if (year >= 1900 && year <= 2099)
                    return (byte)(year % 100);
            }

            return 0;
        }


        public static void ReadAll(string binPath, Action<uint, uint> onRecord)
        {
            const int RecordSize = 8;

            using var fs = new FileStream(
                binPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, options: FileOptions.SequentialScan);

            byte[] buf = new byte[1 << 20];
            int carry = 0;

            while (true)
            {
                int n = fs.Read(buf, carry, buf.Length - carry);
                if (n == 0) break;

                int total = carry + n;
                int i = 0;

                while (i + RecordSize <= total)
                {
                    uint startYmd =
                        (uint)(buf[i + 0]
                        | (buf[i + 1] << 8)
                        | (buf[i + 2] << 16)
                        | (buf[i + 3] << 24));

                    uint endYmd =
                        (uint)(buf[i + 4]
                        | (buf[i + 5] << 8)
                        | (buf[i + 6] << 16)
                        | (buf[i + 7] << 24));

                    onRecord(startYmd, endYmd);
                    i += RecordSize;
                }

                carry = total - i;
                if (carry > 0)
                    Buffer.BlockCopy(buf, i, buf, 0, carry);
            }

            if (carry != 0)
                throw new InvalidDataException(
                    $"Fichier corrompu: reste {carry} byte(s) (taille record={RecordSize}).");
        }

        private static void getDates(string fileName)
        {
            ReadOnlySpan<char> span = fileName.AsSpan();

            // extraction directe (ultra rapide, zéro allocation intermédiaire)
            ReadOnlySpan<char> date1 = span.Slice(10, 8);
            ReadOnlySpan<char> date2 = span.Slice(19, 8);

            Debug.WriteLine($"{date1} | {date2}");


        }
    
    }
}