using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace DatasetTool
{
    public sealed class Binary : IDisposable
    {
        private const int CandleSize = 44;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly long _candleCount;

        public long CandleCount => _candleCount;


        //Read FAST : lit le fichier en utilisant un buffer partagé pour éviter les allocations répétées
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
            byte[] sym = br.ReadBytes(10);
            string s = Encoding.ASCII.GetString(sym).TrimEnd('\0');





            long ms = ts / 1_000_000L;

            DateTimeOffset dto = DateTimeOffset.FromUnixTimeMilliseconds(ms);

            Console.WriteLine($"FIRST TS = {dto:O}");
            Console.WriteLine($"FIRST O = {o} ; H = {h} ; L = {l} ; C = {c} ; V = {v} ; S = {s}");
        }

        //V1
        //public static void ReadAllFast(
        //    string binPath,
        //    byte[] buffer,
        //    Action<long, long, long, long, long, int>? onCandle = null)
        //{
        //    const int CandleSize = 44;

        //    using var fs = new FileStream(
        //        binPath,
        //        FileMode.Open,
        //        FileAccess.Read,
        //        FileShare.Read,
        //        bufferSize: buffer.Length,
        //        FileOptions.SequentialScan
        //    );

        //    while (true)
        //    {
        //        int bytesRead = fs.Read(buffer, 0, buffer.Length);
        //        if (bytesRead == 0) break;

        //        int records = bytesRead / CandleSize;
        //        Span<byte> span = buffer.AsSpan(0, records * CandleSize);

        //        for (int i = 0; i < records; i++)
        //        {
        //            int off = i * CandleSize;

        //            long ts = MemoryMarshal.Read<long>(span.Slice(off + 0));
        //            long o = MemoryMarshal.Read<long>(span.Slice(off + 8));
        //            long h = MemoryMarshal.Read<long>(span.Slice(off + 16));
        //            long l = MemoryMarshal.Read<long>(span.Slice(off + 24));
        //            long c = MemoryMarshal.Read<long>(span.Slice(off + 32));
        //            int v = MemoryMarshal.Read<int>(span.Slice(off + 40));

        //            onCandle?.Invoke(ts, o, h, l, c, v);
        //        }
        //    }
        //}


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
        // 🔥 ACCÈS ULTRA RAPIDE PAR INDEX
        // =====================================================
        public void GetCandle(
            long index,
            out long ts,
            out long o,
            out long h,
            out long l,
            out long c,
            out int v)
        {

            if ((ulong)index >= (ulong)_candleCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            long offset = index * CandleSize;

            ts = _accessor.ReadInt64(offset + 0);
            o = _accessor.ReadInt64(offset + 8);
            h = _accessor.ReadInt64(offset + 16);
            l = _accessor.ReadInt64(offset + 24);
            c = _accessor.ReadInt64(offset + 32);
            v = _accessor.ReadInt32(offset + 40);
        }

        // =====================================================
        // 🔁 STREAM COMPLET (séquentiel, très rapide aussi)
        // =====================================================
        public void ReadAllFast(Action<long, long, long, long, long, int> onCandle)
        {
            for (long i = 0; i < _candleCount; i++)
            {
                GetCandle(i, out var ts, out var o, out var h, out var l, out var c, out var v);
                onCandle(ts, o, h, l, c, v);
            }
        }

        public void Dispose()
        {
            _accessor.Dispose();
            _mmf.Dispose();
        }
    }

}
