using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace DatasetToolTest.BackTestApp.Controls.CandleChartControl.CandleIndex.IntegrityCheck;

public class CandlesFileIntegrity
{
    private const string IndexPath = "data/bin/_index.bin";
    private const string BinFolder = "data/bin";

    private const int IndexRecordSize = 8;
    private const int CandleRecordSize = 45;
    private const int ExpectedFileCount = 817;

    private readonly record struct IndexRecord(int Idx, uint StartYmd, uint EndYmd);

    private readonly record struct CandleRecord(
        long Idx,
        long Ts,
        long O,
        long H,
        long L,
        long C,
        uint V,
        byte Sym);

    private static List<IndexRecord> ReadIndexRaw()
    {
        Assert.True(File.Exists(IndexPath), $"Index introuvable: {IndexPath}");

        byte[] bytes = File.ReadAllBytes(IndexPath);

        Assert.True(bytes.Length > 0, $"L'index ne doit pas être vide: {IndexPath}");
        Assert.True(bytes.Length % IndexRecordSize == 0);

        var records = new List<IndexRecord>(bytes.Length / IndexRecordSize);

        for (int offset = 0, idx = 0; offset < bytes.Length; offset += IndexRecordSize, idx++)
        {
            uint startYmd = BitConverter.ToUInt32(bytes, offset);
            uint endYmd = BitConverter.ToUInt32(bytes, offset + 4);

            records.Add(new IndexRecord(idx, startYmd, endYmd));
        }

        return records;
    }

    private static string BuildCandleFilePath(IndexRecord record)
        => Path.Combine(
            BinFolder,
            $"glbx-mdp3-{record.StartYmd}-{record.EndYmd}.ohlcv-1m.bin");

    private static IEnumerable<CandleRecord> ReadCandlesRaw(string path)
    {
        Assert.True(File.Exists(path), $"Fichier candle introuvable: {path}");

        byte[] bytes = File.ReadAllBytes(path);

        Assert.True(
            bytes.Length > 0,
            $"Le fichier candle ne doit pas être vide: {path}");

        Assert.True(
            bytes.Length % CandleRecordSize == 0,
            $"La taille du fichier candle doit être multiple de {CandleRecordSize}. path={path}, size={bytes.Length}");

        long candleIdx = 0;

        for (int offset = 0; offset < bytes.Length; offset += CandleRecordSize, candleIdx++)
        {
            yield return new CandleRecord(
                Idx: candleIdx,
                Ts: BitConverter.ToInt64(bytes, offset + 0),
                O: BitConverter.ToInt64(bytes, offset + 8),
                H: BitConverter.ToInt64(bytes, offset + 16),
                L: BitConverter.ToInt64(bytes, offset + 24),
                C: BitConverter.ToInt64(bytes, offset + 32),
                V: BitConverter.ToUInt32(bytes, offset + 40),
                Sym: bytes[offset + 44]);
        }
    }

    private static bool IsValidCandle(CandleRecord candle)
    {
        const long MinTs = 946684800L * 1_000_000_000L;   // 2000-01-01
        const long MaxTs = 4102444800L * 1_000_000_000L;  // 2100-01-01
        const long MaxReasonable = 10_000_000_000_000L;

        if (candle.Ts < MinTs || candle.Ts > MaxTs) return false;
        if (candle.O <= 0 || candle.H <= 0 || candle.L <= 0 || candle.C <= 0) return false;
        if (candle.H < candle.L) return false;
        if (candle.O > MaxReasonable) return false;
        if (candle.H > MaxReasonable) return false;
        if (candle.L > MaxReasonable) return false;
        if (candle.C > MaxReasonable) return false;

        return true;
    }

    [Fact]
    public void One_File_Should_Not_Have_Duplicate_Candles()
    {
        var file = ReadIndexRaw()[0];
        string path = BuildCandleFilePath(file);

        var uniqueTs = new HashSet<long>();
        long total = 0;

        foreach (var candle in ReadCandlesRaw(path))
        {
            Assert.True(
                uniqueTs.Add(candle.Ts),
                $"Doublon candle détecté dans {path}. idx={candle.Idx}, ts={candle.Ts}");

            total++;
        }

        Assert.Equal(total, uniqueTs.Count);
    }

    [Fact]
    public void One_File_Should_Be_Strictly_Chronological()
    {
        var file = ReadIndexRaw()[0];
        string path = BuildCandleFilePath(file);

        long? previousTs = null;

        foreach (var candle in ReadCandlesRaw(path))
        {
            if (previousTs.HasValue)
            {
                Assert.True(
                    candle.Ts > previousTs.Value,
                    $"Candles non chronologiques dans {path}. idx={candle.Idx}, prev={previousTs.Value}, cur={candle.Ts}");
            }

            previousTs = candle.Ts;
        }
    }

    [Fact]
    public void One_File_Should_Have_Valid_Candle_Records()
    {
        var file = ReadIndexRaw()[0];
        string path = BuildCandleFilePath(file);

        long total = 0;

        foreach (var candle in ReadCandlesRaw(path))
        {
            Assert.True(
                IsValidCandle(candle),
                $"Candle invalide dans {path}. idx={candle.Idx}, ts={candle.Ts}, o={candle.O}, h={candle.H}, l={candle.L}, c={candle.C}, v={candle.V}, sym={candle.Sym}");

            total++;
        }

        Assert.True(total > 0, $"Le fichier doit contenir au moins une candle: {path}");
    }

    [Fact]
    public void All_Files_Should_Have_Valid_Candles_Without_Duplicate_And_Chronological()
    {
        var files = ReadIndexRaw();

        Assert.Equal(ExpectedFileCount, files.Count);

        long totalCandles = 0;

        foreach (var file in files)
        {
            string path = BuildCandleFilePath(file);

            var uniqueTsInFile = new HashSet<long>();
            long? previousTsInFile = null;
            long candlesInFile = 0;

            foreach (var candle in ReadCandlesRaw(path))
            {
                Assert.True(
                    IsValidCandle(candle),
                    $"Candle invalide dans {path}. idx={candle.Idx}, ts={candle.Ts}, o={candle.O}, h={candle.H}, l={candle.L}, c={candle.C}");

                Assert.True(
                    uniqueTsInFile.Add(candle.Ts),
                    $"Doublon candle dans le fichier {path}. idx={candle.Idx}, ts={candle.Ts}");

                if (previousTsInFile.HasValue)
                {
                    Assert.True(
                        candle.Ts > previousTsInFile.Value,
                        $"Candles non chronologiques dans {path}. idx={candle.Idx}, prev={previousTsInFile.Value}, cur={candle.Ts}");
                }

                previousTsInFile = candle.Ts;
                candlesInFile++;
                totalCandles++;
            }

            Assert.True(candlesInFile > 0, $"Fichier candle vide: {path}");
            Assert.Equal(candlesInFile, uniqueTsInFile.Count);
        }

        Assert.True(totalCandles > 0, "Le dataset doit contenir au moins une candle.");
    }
}