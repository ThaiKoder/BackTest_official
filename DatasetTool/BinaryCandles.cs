using System.IO.MemoryMappedFiles;

public sealed class Binary : IDisposable
{
    // TsNs(8) + O(8) + H(8) + L(8) + C(8) = 40
    // V (UInt32)                         = 4  => 44
    // SymbolCode (byte)                  = 1  => 45
    private const int CandleSize = 45;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _candleCount;

    public long CandleCount => _candleCount;

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
            MemoryMappedFileAccess.Read);

        _accessor = _mmf.CreateViewAccessor(
            0,
            fileInfo.Length,
            MemoryMappedFileAccess.Read);
    }

    public void GetCandle(
        long index,
        out long ts,
        out long o,
        out long h,
        out long l,
        out long c,
        out uint v,
        out byte symbolCode)
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

        symbolCode = _accessor.ReadByte(offset + 44);
    }

    public void ReadAllFast(Action<long, long, long, long, long, uint, byte> onCandle)
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