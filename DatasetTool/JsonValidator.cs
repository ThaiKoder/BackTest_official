using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DatasetTool
{
    public sealed record JsonValidationResult(
        long Lines,
        long ValidCandles,
        long InvalidLines,
        string? FirstError,
        long? FirstErrorLine);

    public static class JsonValidator
    {
        public static async Task<JsonValidationResult> ValidateJsonlAsync(string jsonPath, CancellationToken ct = default)
        {
            await using var fs = new FileStream(
                jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: true);

            byte[] buffer = new byte[1 << 20];
            byte[] carry = new byte[1 << 20];
            int carryLen = 0;

            long lines = 0, ok = 0, bad = 0;
            string? firstErr = null;
            long? firstErrLine = null;

            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();

                int start = 0;
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;

                    lines++;
                    int segLen = i - start;
                    int totalLen = carryLen + segLen;

                    try
                    {
                        if (totalLen > 0)
                        {
                            byte[] line = new byte[totalLen];
                            if (carryLen > 0) Buffer.BlockCopy(carry, 0, line, 0, carryLen);
                            if (segLen > 0) Buffer.BlockCopy(buffer, start, line, carryLen, segLen);

                            carryLen = 0;

                            int len = totalLen;
                            if (len > 0 && line[len - 1] == (byte)'\r') len--;

                            if (len > 0)
                                ValidateOneLine(line.AsSpan(0, len));

                            ok++;
                        }
                        else
                        {
                            ok++; // ligne vide tolérée
                        }
                    }
                    catch (Exception ex)
                    {
                        bad++;
                        if (firstErr is null)
                        {
                            firstErr = ex.Message;
                            firstErrLine = lines;
                        }
                    }

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

            // dernière ligne si pas de \n
            if (carryLen > 0)
            {
                lines++;
                try
                {
                    ValidateOneLine(carry.AsSpan(0, carryLen));
                    ok++;
                }
                catch (Exception ex)
                {
                    bad++;
                    firstErr ??= ex.Message;
                    firstErrLine ??= lines;
                }
            }

            return new JsonValidationResult(lines, ok, bad, firstErr, firstErrLine);
        }

        private static void ValidateOneLine(ReadOnlySpan<byte> jsonUtf8)
        {
            var reader = new Utf8JsonReader(jsonUtf8, isFinalBlock: true, state: default);

            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // hd.ts_event
            var hd = root.GetProperty("hd");
            var ts = hd.GetProperty("ts_event").GetString();
            if (string.IsNullOrWhiteSpace(ts)) throw new FormatException("hd.ts_event manquant");

            // champs prix/volume (string ou number)
            _ = ParseLong(root, "open");
            _ = ParseLong(root, "high");
            _ = ParseLong(root, "low");
            _ = ParseLong(root, "close");
            _ = ParseLong(root, "volume");
        }

        private static long ParseLong(JsonElement root, string prop)
        {
            var el = root.GetProperty(prop);
            return el.ValueKind switch
            {
                JsonValueKind.String => long.Parse(el.GetString()!, CultureInfo.InvariantCulture),
                JsonValueKind.Number => el.GetInt64(),
                _ => throw new FormatException($"{prop} invalide (kind={el.ValueKind})")
            };
        }

        private static void EnsureCapacity(ref byte[] arr, int needed)
        {
            if (arr.Length >= needed) return;
            int newSize = arr.Length;
            while (newSize < needed) newSize *= 2;
            Array.Resize(ref arr, newSize);
        }
    }
}