using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace DatasetTool
{
    public readonly struct Candle1m
    {
        public readonly long TsNs;
        public readonly long O, H, L, C;
        public readonly uint V;
        public readonly byte SymbolCode;

        public Candle1m(long tsNs, long o, long h, long l, long c, uint v, byte symbolCode)
            => (TsNs, O, H, L, C, V, SymbolCode) = (tsNs, o, h, l, c, v, symbolCode);
    }

    public static class JsonToBinaryConverter
    {
        public const int CandleRecordSize = 45;

        internal static Candle1m ReadCandle(ref Utf8JsonReader reader)
        {
            long tsNs = 0, o = 0, h = 0, l = 0, c = 0;
            uint v = 0;
            byte s = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("hd"))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                        throw new FormatException("hd doit être un objet");

                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndObject)
                            break;

                        if (reader.TokenType != JsonTokenType.PropertyName)
                            continue;

                        if (reader.ValueTextEquals("ts_event"))
                        {
                            reader.Read();
                            string iso = reader.GetString()!;
                            tsNs = ParseIsoToEpochNs(iso);
                        }
                        else
                        {
                            reader.Read();
                            reader.Skip();
                        }
                    }
                }
                else if (reader.ValueTextEquals("open"))
                {
                    reader.Read();
                    o = ParseLongMaybeString(ref reader);
                }
                else if (reader.ValueTextEquals("high"))
                {
                    reader.Read();
                    h = ParseLongMaybeString(ref reader);
                }
                else if (reader.ValueTextEquals("low"))
                {
                    reader.Read();
                    l = ParseLongMaybeString(ref reader);
                }
                else if (reader.ValueTextEquals("close"))
                {
                    reader.Read();
                    c = ParseLongMaybeString(ref reader);
                }
                else if (reader.ValueTextEquals("volume"))
                {
                    reader.Read();
                    v = ParseUInt32MaybeString(ref reader);
                }
                else if (reader.ValueTextEquals("symbol"))
                {
                    reader.Read();
                    s = GetSymbolValue(ref reader);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            return new Candle1m(tsNs, o, h, l, c, v, s);
        }

        public static byte GetQuarter(long dateTicks)
        {
            DateTime date = DateTime.UnixEpoch.AddTicks(dateTicks / 100);
            int year = date.Year;

            DateTime ThirdFriday(int y, int month)
            {
                DateTime firstDay = new DateTime(y, month, 1);
                int daysOffset = (int)firstDay.DayOfWeek % 5;
                DateTime firstFriday = (daysOffset != 0 ? firstDay.AddDays(5 - daysOffset) : firstDay);
                DateTime thirdFriday = firstFriday.AddDays(14);
                return thirdFriday.AddDays(-1);
            }

            DateTime q1Date = ThirdFriday(year, 3);
            DateTime q2Date = ThirdFriday(year, 6);
            DateTime q3Date = ThirdFriday(year, 9);
            DateTime q4Date = ThirdFriday(year, 12);

            if (date <= q1Date) return 1;
            if (date <= q2Date) return 2;
            if (date <= q3Date) return 3;
            if (date <= q4Date) return 4;
            return 1;
        }

        public static void ConvertJson(string jsonPath, string binPath, CancellationToken ct = default)
        {
            string tmpPath = binPath + ".tmp";
            long lineNo = 0;
            var seenTsNs = new HashSet<long>();

            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            try
            {
                using var fs = new FileStream(
                    jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1 << 20, useAsync: false);

                using var outFs = new FileStream(
                    tmpPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 1 << 20, useAsync: false);

                using var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true);

                byte[] buffer = new byte[1 << 20];
                byte[] carry = new byte[1 << 20];
                int carryLen = 0;

                int bytesRead;
                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    int start = 0;
                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] != (byte)'\n')
                            continue;

                        ProcessLine(
                            buffer, start, i - start,
                            ref carry, ref carryLen,
                            ref lineNo, jsonPath, bw, seenTsNs);

                        start = i + 1;
                    }

                    int remaining = bytesRead - start;
                    if (remaining > 0)
                    {
                        EnsureCapacity(ref carry, carryLen + remaining);
                        Buffer.BlockCopy(buffer, start, carry, carryLen, remaining);
                        carryLen += remaining;
                    }
                }

                if (carryLen > 0)
                {
                    ProcessFinalCarryLine(
                        carry, carryLen,
                        ref lineNo, jsonPath, bw, seenTsNs);
                }

                bw.Flush();
                outFs.Flush(true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                }

                throw;
            }

            long tmpLen = new FileInfo(tmpPath).Length;
            if (tmpLen % CandleRecordSize != 0)
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                }

                throw new IOException(
                    $"Fichier binaire invalide: {Path.GetFileName(tmpPath)}, taille={tmpLen}, reste={tmpLen % CandleRecordSize}");
            }

            if (File.Exists(binPath))
                File.Delete(binPath);

            File.Move(tmpPath, binPath);
        }

        private static void ProcessLine(
            byte[] buffer, int start, int segLen,
            ref byte[] carry, ref int carryLen,
            ref long lineNo, string jsonPath,
            BinaryWriter bw,
            HashSet<long> seenTsNs)
        {
            int totalLen = carryLen + segLen;
            if (totalLen <= 0)
            {
                carryLen = 0;
                return;
            }

            ReadOnlySpan<byte> lineSpan;
            byte[]? tmp = null;

            if (carryLen == 0)
            {
                lineSpan = buffer.AsSpan(start, segLen);
            }
            else
            {
                tmp = ArrayPool<byte>.Shared.Rent(totalLen);
                carry.AsSpan(0, carryLen).CopyTo(tmp);
                buffer.AsSpan(start, segLen).CopyTo(tmp.AsSpan(carryLen));
                lineSpan = tmp.AsSpan(0, totalLen);
            }

            carryLen = 0;

            if (!lineSpan.IsEmpty && lineSpan[^1] == (byte)'\r')
                lineSpan = lineSpan[..^1];

            if (!lineSpan.IsEmpty)
            {
                lineNo++;
                try
                {
                    var candle = ReadCandleFromJsonLine(lineSpan);
                    WriteCandleIfUnique(bw, candle, seenTsNs);
                }
                catch (Exception ex)
                {
                    string txt = Encoding.UTF8.GetString(lineSpan);
                    throw new FormatException($"[{Path.GetFileName(jsonPath)}] ligne {lineNo}: {txt}", ex);
                }
            }

            if (tmp is not null)
                ArrayPool<byte>.Shared.Return(tmp);
        }

        private static void ProcessFinalCarryLine(
            byte[] carry, int carryLen,
            ref long lineNo, string jsonPath,
            BinaryWriter bw,
            HashSet<long> seenTsNs)
        {
            ReadOnlySpan<byte> lineSpan = carry.AsSpan(0, carryLen);

            if (!lineSpan.IsEmpty && lineSpan[^1] == (byte)'\r')
                lineSpan = lineSpan[..^1];

            if (lineSpan.IsEmpty)
                return;

            lineNo++;
            try
            {
                var candle = ReadCandleFromJsonLine(lineSpan);
                WriteCandleIfUnique(bw, candle, seenTsNs);
            }
            catch (Exception ex)
            {
                string txt = Encoding.UTF8.GetString(lineSpan);
                throw new FormatException($"[{Path.GetFileName(jsonPath)}] ligne {lineNo}: {txt}", ex);
            }
        }

        private static void WriteCandleIfUnique(BinaryWriter bw, Candle1m candle, HashSet<long> seenTsNs)
        {
            // ✅ ignore tout ce qui n'est pas un contrat 4 caractères valide
            if (candle.SymbolCode == 0)
                return;

            byte quarter = GetQuarter(candle.TsNs);

            if (quarter != candle.SymbolCode)
                return;

            if (!seenTsNs.Add(candle.TsNs))
                return;

            bw.Write(candle.TsNs);
            bw.Write(candle.O);
            bw.Write(candle.H);
            bw.Write(candle.L);
            bw.Write(candle.C);
            bw.Write(candle.V);
            bw.Write(candle.SymbolCode);
        }
        private static void EnsureCapacity(ref byte[] arr, int needed)
        {
            if (arr.Length >= needed)
                return;

            int newSize = arr.Length;
            while (newSize < needed)
                newSize <<= 1;

            Array.Resize(ref arr, newSize);
        }

        private static Candle1m ReadCandleFromJsonLine(ReadOnlySpan<byte> jsonLineUtf8)
        {
            var readerState = new JsonReaderState();
            var reader = new Utf8JsonReader(jsonLineUtf8, isFinalBlock: true, readerState);

            while (reader.Read() && reader.TokenType != JsonTokenType.StartObject) { }

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new FormatException("Ligne JSON invalide : pas d'objet.");

            return ReadCandle(ref reader);
        }

        internal static long ParseLongMaybeString(ref Utf8JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => long.Parse(reader.GetString()!, CultureInfo.InvariantCulture),
                JsonTokenType.Number => reader.GetInt64(),
                _ => throw new FormatException($"Nombre invalide: {reader.TokenType}")
            };
        }

        internal static uint ParseUInt32MaybeString(ref Utf8JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number when reader.TryGetUInt32(out var n) => n,
                JsonTokenType.String when uint.TryParse(
                    reader.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var s) => s,
                _ => throw new FormatException($"UInt32 invalide: {reader.TokenType}")
            };
        }

        internal static long ParseIsoToEpochNs(string isoZ)
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
            if (z < 0)
                z = isoZ.Length;

            string basePart = isoZ[..dot] + "Z";
            string fracPart = isoZ[(dot + 1)..z];

            var dto2 = DateTimeOffset.Parse(
                basePart,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

            long baseNs = dto2.ToUnixTimeMilliseconds() * 1_000_000L;

            if (fracPart.Length > 9)
                fracPart = fracPart[..9];

            fracPart = fracPart.PadRight(9, '0');

            long extraNs = long.Parse(fracPart, CultureInfo.InvariantCulture);
            return baseNs + extraNs;
        }

        internal static byte GetSymbolValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException($"Token attendu String, reçu {reader.TokenType}");

            var value = reader.GetString();

            if (value is null)
                throw new InvalidOperationException("Valeur null");

            value = value.Trim();

            // ✅ on ne garde que les contrats à 4 caractères
            if (value.Length != 4)
                return 0;

            if (value.StartsWith("NQH", StringComparison.OrdinalIgnoreCase)) return 1;
            if (value.StartsWith("NQM", StringComparison.OrdinalIgnoreCase)) return 2;
            if (value.StartsWith("NQU", StringComparison.OrdinalIgnoreCase)) return 3;
            if (value.StartsWith("NQZ", StringComparison.OrdinalIgnoreCase)) return 4;

            return 0;
        }
    }
}
