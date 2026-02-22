using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


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

    //public readonly record struct Candle1m
    //{
    //    public long TsNs { get; }
    //    public long O { get; }
    //    public long H { get; }
    //    public long L { get; }
    //    public long C { get; }
    //    public uint V { get; }
    //    public byte Symbol { get; }

    //    public Candle1m(long tsNs, long o, long h, long l, long c, uint v, byte symbol)
    //    {
    //        //if (tsNs < 0) throw new JsonException("Missing required field: hd.ts_event");
    //        //if (o < 0) throw new JsonException("Missing required field: open");
    //        //if (h < 0) throw new JsonException("Missing required field: high");
    //        //if (l <= 0) throw new JsonException("Missing required field: low");
    //        //if (c <= 0) throw new JsonException("Missing required field: close");
    //        //if (v <= 0) throw new JsonException("Missing required field: volume");

    //        TsNs = tsNs;
    //        O = o;
    //        H = h;
    //        L = l;
    //        C = c;
    //        V = v;
    //        Symbol = symbol;
    //    }
    //}


    public static class JsonToBinaryConverter
    {

        private static long _lastTsNs = -1;
        private static readonly string[] QuarterContracts = { "", "NQH", "NQM", "NQU", "NQZ" };

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
                    reader.Read(); // doit être StartObject
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
                            reader.Read(); // Value
                            string iso = reader.GetString()!;
                            tsNs = ParseIsoToEpochNs(iso);
                        }
                        else
                        {
                            // IMPORTANT: sauter la VALEUR de la propriété
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
                    // IMPORTANT: sauter la VALEUR de la propriété
                    reader.Read();
                    reader.Skip();
                }
            }

            return new Candle1m(tsNs, o, h, l, c, v, s);
        }

        private static void WriteFixedString10(BinaryWriter bw, string symbol)
        {
            Span<byte> tmp = stackalloc byte[10];
            tmp.Clear();

            // on encode en ASCII (idéal pour symbol type "BTCUSDT", etc.)
            // si tu as des caractères non ASCII, remplace par Encoding.UTF8 (mais longueur variable)
            int n = Encoding.ASCII.GetBytes(symbol.AsSpan(), tmp);
            // si symbol > 10, n sera 10 (troncature)
            bw.Write(tmp);
        }



        public static byte GetQuarter(long dateTicks)
        {
            DateTime date = DateTime.UnixEpoch.AddTicks(dateTicks / 100);
            int year = date.Year;

            // Fonction pour trouver le 3e vendredi d'un mois
            DateTime ThirdFriday(int y, int month)
            {



                //Recuperer le jour du premier mois
                DateTime firstDay = new DateTime(y, month, 1);

                //Ajouter le nombre de jours pour arriver au premier vendredi
                int daysOffset = (int)firstDay.DayOfWeek % 5;
                DateTime firstFriday;

                //Si le premier jour est un vendredi, on reste dessus, sinon on avance jusqu'au vendredi suivant
                firstFriday = (daysOffset != 0 ? firstDay.AddDays(5 - daysOffset) : firstDay);

                //Faire * 3 pour arriver au 3e vendredi
                DateTime thirdFriday = firstFriday.AddDays(14);

                //-1 pour arriver au jour avant le 3e vendredi soit changement jeudi
                return thirdFriday.AddDays(-1);
            }

            // Vendredi avant le 3e vendredi
            DateTime Q1Date = ThirdFriday(year, 3);
            DateTime Q2Date = ThirdFriday(year, 6);
            DateTime Q3Date = ThirdFriday(year, 9);
            DateTime Q4Date = ThirdFriday(year, 12);

            if (date <= Q1Date)
                return 1;
            if (date <= Q2Date)
                return 2;
            if (date <= Q3Date)
                return 3;
            if (date <= Q4Date)
                return 4;
            return 0;
        }




        public static void ConvertJson(string jsonPath, string binPath, CancellationToken ct = default)
        {
            long lineNo = 0;

            using var fs = new FileStream(
                jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: false);

            using var outFs = new FileStream(
                binPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, useAsync: false);

            using var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: false);

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
                    if (buffer[i] != (byte)'\n') continue;

                    // traiter une ligne
                    ProcessLine(buffer, start, i - start, ref carry, ref carryLen, ref lineNo, jsonPath, bw);

                    start = i + 1;
                }

                // reste -> carry
                int remaining = bytesRead - start;
                if (remaining > 0)
                {
                    EnsureCapacity(ref carry, carryLen + remaining);
                    Buffer.BlockCopy(buffer, start, carry, carryLen, remaining);
                    carryLen += remaining;
                }
            }

            // dernière ligne
            if (carryLen > 0)
                ProcessFinalCarryLine(carry, carryLen, ref lineNo, jsonPath, bw);

            // bw.Dispose() flush automatiquement
        }

        // ✅ SYNC
        private static void ProcessLine(
            byte[] buffer, int start, int segLen,
            ref byte[] carry, ref int carryLen,
            ref long lineNo, string jsonPath,
            BinaryWriter bw)
        {
            int totalLen = carryLen + segLen;
            if (totalLen <= 0) { carryLen = 0; return; }

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

                    byte quarter = GetQuarter(candle.TsNs);
                    string contractName = QuarterContracts[quarter]; // si tu en as besoin

                    if (quarter == candle.SymbolCode && _lastTsNs != candle.TsNs)
                    {
                        bw.Write(candle.TsNs);
                        bw.Write(candle.O);
                        bw.Write(candle.H);
                        bw.Write(candle.L);
                        bw.Write(candle.C);
                        bw.Write(candle.V);
                        bw.Write(candle.SymbolCode);
                        _lastTsNs = candle.TsNs;

                    }

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
            BinaryWriter bw)
        {
            ReadOnlySpan<byte> lineSpan = carry.AsSpan(0, carryLen);
            if (!lineSpan.IsEmpty && lineSpan[^1] == (byte)'\r') lineSpan = lineSpan[..^1];
            if (lineSpan.IsEmpty) return;

            lineNo++;
            try
            {
                var candle = ReadCandleFromJsonLine(lineSpan);

                bw.Write(candle.TsNs);
                bw.Write(candle.O);
                bw.Write(candle.H);
                bw.Write(candle.L);
                bw.Write(candle.C);
                bw.Write(candle.V);
                bw.Write(candle.SymbolCode);
            }
            catch (Exception ex)
            {
                string txt = Encoding.UTF8.GetString(lineSpan);
                throw new FormatException($"[{Path.GetFileName(jsonPath)}] ligne {lineNo}: {txt}", ex);
            }
        }

        private static void EnsureCapacity(ref byte[] arr, int needed)
        {
            if (arr.Length >= needed) return;
            int newSize = arr.Length;
            while (newSize < needed) newSize <<= 1;
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

        internal static string GetStringValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"Token attendu String, reçu {reader.TokenType}");

            return reader.GetString()!;
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

        internal static ulong ParseULongMaybeString(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetUInt64(out var n))
                    return n;

                throw new FormatException("UInt64 invalide (Number)");
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();

                if (ulong.TryParse(
                    s,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var v))
                    return v;

                throw new FormatException($"UInt64 invalide (String): '{s}'");
            }

            throw new FormatException($"UInt64 invalide (TokenType): {reader.TokenType}");
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
        // "2010-06-06T22:00:00.000000000Z" -> epoch ns
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
            if (z < 0) z = isoZ.Length;

            string basePart = isoZ[..dot] + "Z";
            string fracPart = isoZ[(dot + 1)..z];

            var dto2 = DateTimeOffset.Parse(
                basePart,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

            long baseNs = dto2.ToUnixTimeMilliseconds() * 1_000_000L;

            // normaliser fraction à 9 digits (ns)
            if (fracPart.Length > 9)
                fracPart = fracPart[..9];

            fracPart = fracPart.PadRight(9, '0');

            long extraNs = long.Parse(fracPart, CultureInfo.InvariantCulture);

            return baseNs + extraNs;
        }
        internal static ulong ParseTimestampNsMaybeString(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt64(out var n))
                return n;

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();

                if (string.IsNullOrWhiteSpace(s))
                    throw new FormatException("Timestamp vide");

                // 1) epoch en string
                if (ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
                    return epoch;

                // 2) ISO 8601 en string -> epoch ns
                try
                {
                    return (ulong)ParseIsoToEpochNs(s);
                }
                catch (Exception ex)
                {
                    throw new FormatException($"Timestamp string invalide: '{s}'", ex);
                }
            }

            throw new FormatException($"Timestamp invalide: {reader.TokenType}");
        }

        //internal static byte GetSymbolValue(ref Utf8JsonReader reader)
        //{
        //    if (reader.TokenType != JsonTokenType.String)
        //        throw new InvalidOperationException(
        //            $"Token attendu String, reçu {reader.TokenType}");


        //    //FAST => Correct later for "NQH-NQZ" or "NQZ-NQH"
        //    //if (reader.GetString().Trim().StartsWith(contractName, StringComparison.OrdinalIgnoreCase))
        //    if (reader.GetString().Trim().StartsWith("NQH", StringComparison.OrdinalIgnoreCase)) return 1;
        //    //if (reader.ValueTextEquals("NQM")) return 2;
        //    //if (reader.ValueTextEquals("NQU")) return 3;
        //    //if (reader.ValueTextEquals("NQZ")) return 4;

        //    return 5;

        //    throw new InvalidOperationException("Valeur inconnue");
        //}


        internal static byte GetSymbolValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException(
                    $"Token attendu String, reçu {reader.TokenType}");

            var value = reader.GetString();

            if (value is null)
                throw new InvalidOperationException("Valeur null");

            value = value.Trim();

            if (value.StartsWith("NQH", StringComparison.OrdinalIgnoreCase))
                return 1;

            if (value.StartsWith("NQM", StringComparison.OrdinalIgnoreCase))
                return 2;

            if (value.StartsWith("NQU", StringComparison.OrdinalIgnoreCase))
                return 3;

            if (value.StartsWith("NQZ", StringComparison.OrdinalIgnoreCase))
                return 4;

            return 5; // valeur par défaut
        }
    }
}