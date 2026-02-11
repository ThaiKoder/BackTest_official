using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace DatasetTool
{
    public sealed class Binary : IDisposable
    {
        // 5x Int64 (Ts,O,H,L,C) = 40
        // 1x UInt32 (V)         = 4
        // 10 bytes symbol       = 10
        // TOTAL                = 54
        private const int CandleSize = 54;
        private const int SymbolSize = 10;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly long _candleCount;

        public long CandleCount => _candleCount;

        // Read FAST (debug): lit le 1er record
        public static void ReadFile(string binPath)
        {
            using var fs = File.OpenRead(binPath);
            using var br = new BinaryReader(fs);

            long ts = br.ReadInt64();
            long o = br.ReadInt64();
            long h = br.ReadInt64();
            long l = br.ReadInt64();
            long c = br.ReadInt64();
            uint v = br.ReadUInt32();

            byte[] sym = br.ReadBytes(SymbolSize);
            string s = Encoding.ASCII.GetString(sym).TrimEnd('\0');

            long ms = ts / 1_000_000L;
            DateTimeOffset dto = DateTimeOffset.FromUnixTimeMilliseconds(ms);

            Console.WriteLine($"FIRST TS = {dto:O}");
            Console.WriteLine($"FIRST O = {o} ; H = {h} ; L = {l} ; C = {c} ; V = {v} ; S = {s}");
        }

        public Binary(string binPath)
        {
            var fileInfo = new FileInfo(binPath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException(binPath);

            if (fileInfo.Length % CandleSize != 0)
                throw new InvalidDataException("Fichier corrompu (taille invalide)");

            _candleCount = fileInfo.Length / CandleSize;

            _mmf = MemoryMappedFile.CreateFromFile(
                binPath,
                FileMode.Open,
                null,
                0,
                MemoryMappedFileAccess.Read
            );

            _accessor = _mmf.CreateViewAccessor(
                0,
                fileInfo.Length,
                MemoryMappedFileAccess.Read
            );
        }

        // =====================================================
        // ACCÈS RAPIDE PAR INDEX
        // =====================================================
        public void GetCandle(
            long index,
            out long ts,
            out long o,
            out long h,
            out long l,
            out long c,
            out uint v,
            out string symbol)
        {
            if ((ulong)index >= (ulong)_candleCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            long offset = index * CandleSize;

            ts = _accessor.ReadInt64(offset + 0);
            o = _accessor.ReadInt64(offset + 8);
            h = _accessor.ReadInt64(offset + 16);
            l = _accessor.ReadInt64(offset + 24);
            c = _accessor.ReadInt64(offset + 32);

            v = _accessor.ReadUInt32(offset + 40);

            // Lire symbol (10 bytes)
            byte[] sym = new byte[SymbolSize];
            _accessor.ReadArray(offset + 44, sym, 0, SymbolSize);
            symbol = Encoding.ASCII.GetString(sym).TrimEnd('\0');
        }

        // =====================================================
        // STREAM COMPLET (séquentiel)
        // =====================================================
        public void ReadAllFast(Action<long, long, long, long, long, uint, string> onCandle)
        {
            for (long i = 0; i < _candleCount; i++)
            {
                GetCandle(i, out var ts, out var o, out var h, out var l, out var c, out var v, out var s);
                onCandle(ts, o, h, l, c, v, s);
            }
        }

        public void Dispose()
        {
            _accessor.Dispose();
            _mmf.Dispose();
        }
    }
}