using System;
using System.IO;
using System.IO.MemoryMappedFiles;


namespace BacktestApp.Controls
{
    /// <summary>
    /// Reader MMAP pour records fixes.
    /// Layout: [Ts:Int64][O:Int64][H:Int64][L:Int64][C:Int64][V:UInt32][Symbol:10 bytes]
    /// Total = 54 bytes / record
    /// </summary>
    /// 
    public sealed class MmapCandleFile : IDisposable
    {
        public const int CandleSize = 45;
        public const int SymbolSize = 10;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _acc;
        private readonly long _count;

        private readonly byte[] _symTmp = new byte[SymbolSize];

        public long Count => _count;

        public MmapCandleFile(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException("Bin not found", path);

            long len = fi.Length;
            if (len < CandleSize) _count = 0;
            else _count = len / CandleSize;

            _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _acc = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }

        public bool ReadAt(long index,
                           out long ts, out long o, out long h, out long l, out long c, out uint v,
                           Span<byte> sym10)
        {
            ts = o = h = l = c = 0;
            v = 0;

            if ((ulong)index >= (ulong)Count) return false;
            if (sym10.Length < SymbolSize) throw new ArgumentException("sym10 must be length >= 10");

            long off = index * CandleSize;

            ts = _acc.ReadInt64(off + 0);
            o = _acc.ReadInt64(off + 8);
            h = _acc.ReadInt64(off + 16);
            l = _acc.ReadInt64(off + 24);
            c = _acc.ReadInt64(off + 32);
            v = _acc.ReadUInt32(off + 40);

            _acc.ReadArray(off + 44, _symTmp, 0, SymbolSize);
            _symTmp.AsSpan(0, SymbolSize).CopyTo(sym10);

            return true;
        }
        public void Dispose()
        {
            _acc.Dispose();
            _mmf.Dispose();
        }
    }
}