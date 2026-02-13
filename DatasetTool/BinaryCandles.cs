using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

public sealed unsafe class Binary : IDisposable
{
    private const int CandleSize = 45;
    private const long WeekNs = 7L * 24 * 3600 * 1_000_000_000;

    // Index sparse (1 entrée / block)
    private const int BlockShift = 12;               // 2^12 = 4096 records / block
    private const int BlockSize = 1 << BlockShift;
    private const int BlockMask = BlockSize - 1;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _candleCount;

    private byte* _ptr;                               // pointeur sur la vue
    private readonly long[] _blockFirstTs;            // Ts du 1er record de chaque block
    private readonly int _blockCount;

    public long CandleCount => _candleCount;

    public Binary(string binPath)
    {
        var fileInfo = new FileInfo(binPath);
        if (!fileInfo.Exists) throw new FileNotFoundException(binPath);
        if (fileInfo.Length % CandleSize != 0) throw new InvalidDataException("Fichier corrompu (taille invalide)");

        _candleCount = fileInfo.Length / CandleSize;

        _mmf = MemoryMappedFile.CreateFromFile(binPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

        // Acquire pointer
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);

        // Build sparse index
        _blockCount = (int)((_candleCount + BlockSize - 1) >> BlockShift);
        _blockFirstTs = new long[_blockCount];

        for (int b = 0; b < _blockCount; b++)
        {
            long i = (long)b << BlockShift;
            if (i >= _candleCount) i = _candleCount - 1;
            _blockFirstTs[b] = ReadTsAt(i);
        }
    }

    public void Dispose()
    {
        if (_ptr != null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _ptr = null;
        }
        _accessor.Dispose();
        _mmf.Dispose();
    }

    // =================== LECTURES ULTRA-FAST ===================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ReadTsAt(long index)
    {
        // Ts est à offset 0 du record
        byte* p = _ptr + (index * CandleSize);
        return Unsafe.ReadUnaligned<long>(p);
    }

    // Lecture complète (toujours ultra-rapide)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetCandle(
        long index,
        out long ts, out long o, out long h, out long l, out long c, out uint v, out byte symbolCode)
    {
        if ((ulong)index >= (ulong)_candleCount) throw new ArgumentOutOfRangeException(nameof(index));

        byte* p = _ptr + (index * CandleSize);

        ts = Unsafe.ReadUnaligned<long>(p + 0);
        o = Unsafe.ReadUnaligned<long>(p + 8);
        h = Unsafe.ReadUnaligned<long>(p + 16);
        l = Unsafe.ReadUnaligned<long>(p + 24);
        c = Unsafe.ReadUnaligned<long>(p + 32);
        v = Unsafe.ReadUnaligned<uint>(p + 40);
        symbolCode = *(p + 44);
    }

    // =================== SEARCH (INDEX + BLOCK) ===================

    // lower_bound : premier index avec Ts >= target
    public long LowerBoundTs(long target)
    {
        // 1) binary search dans l’index de blocks
        int bLo = 0, bHi = _blockCount; // hi exclusif
        while (bLo < bHi)
        {
            int bMid = bLo + ((bHi - bLo) >> 1);
            if (_blockFirstTs[bMid] < target) bLo = bMid + 1;
            else bHi = bMid;
        }

        // block candidate = bLo (peut être 0.._blockCount)
        int block = bLo;
        if (block <= 0) block = 0;
        else block -= 1; // on revient d’un block, car _blockFirstTs[block] < target

        long start = (long)block << BlockShift;
        long end = start + BlockSize;
        if (end > _candleCount) end = _candleCount;

        // 2) binary search dans le block [start, end)
        long lo = start, hi = end;
        while (lo < hi)
        {
            long mid = lo + ((hi - lo) >> 1);
            if (ReadTsAt(mid) < target) lo = mid + 1;
            else hi = mid;
        }
        return lo; // peut valoir _candleCount
    }

    public long FindNearestIndex(long ts)
    {
        long i = LowerBoundTs(ts);
        if (i <= 0) return 0;
        if (i >= _candleCount) return _candleCount - 1;

        long left = ReadTsAt(i - 1);
        long right = ReadTsAt(i);
        return (ts - left) <= (right - ts) ? (i - 1) : i;
    }

    // =================== WINDOW ± SEMAINES ===================

    public (long start, long endExclusive) GetWindowByWeeks(long ts, int weeksBack = 5, int weeksForward = 5)
    {
        long fromTs = checked(ts - (long)weeksBack * WeekNs);
        long toTs = checked(ts + (long)weeksForward * WeekNs);

        long start = LowerBoundTs(fromTs);
        long endExclusive = LowerBoundTs(checked(toTs + 1));

        if (start < 0) start = 0;
        if (start > _candleCount) start = _candleCount;
        if (endExclusive < start) endExclusive = start;
        if (endExclusive > _candleCount) endExclusive = _candleCount;

        return (start, endExclusive);
    }

    public void ReadWindowByWeeks(long ts, int weeksBack, int weeksForward,
        Action<long, long, long, long, long, uint, byte> onCandle)
    {
        var (start, endEx) = GetWindowByWeeks(ts, weeksBack, weeksForward);
        for (long i = start; i < endEx; i++)
        {
            GetCandle(i, out var _ts, out var o, out var h, out var l, out var c, out var v, out var s);
            onCandle(_ts, o, h, l, c, v, s);
        }
    }
}