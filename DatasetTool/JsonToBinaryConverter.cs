using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DatasetTool
{
    public readonly record struct Candle1m
    {
        public long TsNs { get; }
        public long O { get; }
        public long H { get; }
        public long L { get; }
        public long C { get; }
        public long V { get; }

        public Candle1m(long tsNs, long o, long h, long l, long c, long v)
        {
            if (tsNs <= 0) throw new JsonException("Missing required field: hd.ts_event");
            if (o <= 0) throw new JsonException("Missing required field: open");
            if (h <= 0) throw new JsonException("Missing required field: high");
            if (l <= 0) throw new JsonException("Missing required field: low");
            if (c <= 0) throw new JsonException("Missing required field: close");
            if (v <= 0) throw new JsonException("Missing required field: volume");

            TsNs = tsNs;
            O = o;
            H = h;
            L = l;
            C = c;
            V = v;
        }
    }


    public static class JsonToBinaryConverter
    {

        internal static Candle1m ReadCandle(ref Utf8JsonReader reader)
        {
            long tsNs = 0, o = 0, h = 0, l = 0, c = 0, v = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                // Nom de propriété
                if (reader.ValueTextEquals("hd"))
                {
                    reader.Read(); // StartObject
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndObject)
                            break;

                        if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("ts_event"))
                        {
                            reader.Read(); // Value
                            string iso = reader.GetString()!;
                            tsNs = ParseIsoToEpochNs(iso);
                        }
                        else
                        {
                            reader.Skip(); // ignore le reste de hd
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
                    v = ParseLongMaybeString(ref reader);
                }
                else
                {
                    // symbol / instrument_id / rtype etc.
                    reader.Skip();
                }
            }


            return new Candle1m(tsNs, o, h, l, c, v);
        }

        public static async Task ConvertJsonAsync(string jsonPath, string binPath, CancellationToken ct = default)
        {
            await using var fs = new FileStream(
                jsonPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 20,
                useAsync: true);

            await using var outFs = new FileStream(
                binPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1 << 20,
                useAsync: true);

            using var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true);

            // Buffer de lecture
            byte[] buffer = new byte[1 << 20];

            // "carry" stocke le bout de ligne coupée entre deux ReadAsync
            byte[] carry = new byte[1 << 20];
            int carryLen = 0;

            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();

                int start = 0;

                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        int segLen = i - start;
                        int totalLen = carryLen + segLen;

                        if (totalLen > 0)
                        {
                            // Construire la ligne complète (carry + segment)
                            byte[] line = new byte[totalLen];
                            if (carryLen > 0) Buffer.BlockCopy(carry, 0, line, 0, carryLen);
                            if (segLen > 0) Buffer.BlockCopy(buffer, start, line, carryLen, segLen);

                            // reset carry
                            carryLen = 0;

                            // enlever '\r' si CRLF
                            int len = totalLen;
                            if (len > 0 && line[len - 1] == (byte)'\r') len--;

                            if (len > 0)
                            {
                                var candle = ReadCandleFromJsonLine(line.AsSpan(0, len));
                                bw.Write(candle.TsNs);
                                bw.Write(candle.O);
                                bw.Write(candle.H);
                                bw.Write(candle.L);
                                bw.Write(candle.C);
                                bw.Write(candle.V);
                            }
                        }

                        start = i + 1;
                    }
                }

                // Reste sans '\n' => va dans carry
                int remaining = bytesRead - start;
                if (remaining > 0)
                {
                    EnsureCapacity(ref carry, carryLen + remaining);
                    Buffer.BlockCopy(buffer, start, carry, carryLen, remaining);
                    carryLen += remaining;
                }
            }

            // Dernière ligne si pas de \n final
            if (carryLen > 0)
            {
                var candle = ReadCandleFromJsonLine(carry.AsSpan(0, carryLen));
                bw.Write(candle.TsNs);
                bw.Write(candle.O);
                bw.Write(candle.H);
                bw.Write(candle.L);
                bw.Write(candle.C);
                bw.Write(candle.V);
            }

            bw.Flush();
        }

        private static void EnsureCapacity(ref byte[] arr, int needed)
        {
            if (arr.Length >= needed) return;
            int newSize = arr.Length;
            while (newSize < needed) newSize *= 2;
            Array.Resize(ref arr, newSize);
        }

        // Ici on utilise Utf8JsonReader "en entier" sur UNE ligne JSON.
        private static Candle1m ReadCandleFromJsonLine(ReadOnlySpan<byte> jsonLineUtf8)
        {
            var readerState = new JsonReaderState();
            var reader = new Utf8JsonReader(jsonLineUtf8, isFinalBlock: true, readerState);

            // Avancer jusqu'au StartObject de la ligne
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

        // "2010-06-06T22:00:00.000000000Z" -> epoch ns
        internal static long ParseIsoToEpochNs(string isoZ)
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