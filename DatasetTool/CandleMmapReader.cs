using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;

public sealed class CandleMmapReader : IDisposable
{
    private const int CandleSize = 54;          // adapte à ton format
    private const long WeekNs = 7L * 24 * 3600 * 1_000_000_000;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _acc;
    private readonly long _count;

    public long Count => _count;

    public CandleMmapReader(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException(path);

        long len = fi.Length;
        if (len % CandleSize != 0) throw new InvalidDataException("Taille fichier non multiple de CandleSize.");

        _count = len / CandleSize;
        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _acc = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public void Dispose()
    {
        _acc.Dispose();
        _mmf.Dispose();
    }

    // Lit le Ts (Int64) du record i
    private long ReadTsAt(long i)
    {
        long offset = checked(i * CandleSize);
        return _acc.ReadInt64(offset + 0);
    }

    // lower_bound : premier index avec Ts >= target
    public long LowerBoundTs(long target)
    {
        long lo = 0, hi = _count; // hi exclusif
        while (lo < hi)
        {
            long mid = lo + ((hi - lo) >> 1);
            long ts = ReadTsAt(mid);
            if (ts < target) lo = mid + 1;
            else hi = mid;
        }
        return lo; // peut valoir _count
    }

    // Fenêtre +/- semaines autour de ts (retourne [startIndex, endIndexExclu])
    public (long start, long endExcl) GetWindowByWeeks(long ts, int weeksBack = 5, int weeksForward = 5)
    {
        long fromTs = ts - checked((long)weeksBack * WeekNs);
        long toTs = ts + checked((long)weeksForward * WeekNs);

        long start = LowerBoundTs(fromTs);
        long endExcl = LowerBoundTs(toTs + 1); // +1ns => inclut tout Ts <= toTs

        if (start < 0) start = 0;
        if (endExcl > _count) endExcl = _count;
        if (endExcl < start) endExcl = start;

        return (start, endExcl);
    }

    // Exemple: lire juste les timestamps dans la fenêtre (à remplacer par ton parsing complet Candle)
    public long[] ReadTimestampsInWindow(long ts, int weeksBack = 5, int weeksForward = 5)
    {
        var (start, endExcl) = GetWindowByWeeks(ts, weeksBack, weeksForward);
        long n = endExcl - start;
        var arr = new long[n];

        for (long k = 0; k < n; k++)
            arr[k] = ReadTsAt(start + k);

        return arr;
    }

    // Optionnel : trouver le record le plus proche de ts
    public long FindNearestIndex(long ts)
    {
        long i = LowerBoundTs(ts);
        if (i <= 0) return 0;
        if (i >= _count) return _count - 1;

        long a = ReadTsAt(i - 1);
        long b = ReadTsAt(i);
        return (ts - a) <= (b - ts) ? (i - 1) : i;
    }
}