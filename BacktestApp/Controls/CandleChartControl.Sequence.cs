using System;
using System.IO;
using DatasetTool;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    private FileIndex? _uiFileIndex;
    private CandleIndex? _uiCandleIndex;
    private FileIndex.FileCursorStep? _uiFileStep;
    private CandleIndex.CandleCursorStep? _uiCandleStep;

    private const int UiFileRange = 3;
    private const int UiCandleRange = 3;

    private void InitializeFilesAndCandlesMode()
    {
        if (_uiFileIndex != null)
            return;

        _uiFileIndex = new FileIndex();
        _uiFileIndex.Load(Path.Combine("data", "bin", "_index.bin"));

        _uiFileStep = _uiFileIndex.FilesNext(0, UiFileRange);
        if (_uiFileStep.CurrentIdx < 0)
        {
            DebugMessage.Write("[CandleChartControl] _index.bin vide ou premier curseur invalide");
            return;
        }

        LoadCandlesForCurrentFileStep();
    }

    private void LoadCandlesForCurrentFileStep()
    {
        if (_uiFileIndex == null || _uiFileStep == null)
            return;

        var (startYmd, endYmd) = _uiFileIndex.Read(_uiFileStep.CurrentIdx);
        string filePath = Path.Combine(
            "data",
            "bin",
            $"glbx-mdp3-{startYmd}-{endYmd}.ohlcv-1m.bin");

        _uiCandleIndex?.Dispose();
        _uiCandleIndex = new CandleIndex();
        _uiCandleIndex.Load(filePath, UiFileRange);

        _uiCandleStep = _uiCandleIndex.CandlesNext(0, UiCandleRange);
        ApplyCandleStepToWindow(_uiCandleStep, resetView: true);
    }

    private void ApplyCandleStepToWindow(CandleIndex.CandleCursorStep step, bool resetView)
    {
        int filled = 0;
        int firstValidIdx = -1;

        foreach (var candle in step.Window)
        {
            if (candle.Idx == -1)
                continue;

            if (firstValidIdx == -1)
                firstValidIdx = candle.Idx;

            if (filled >= WindowCount)
                break;

            _ts[filled] = candle.Ts;
            _o[filled] = candle.O;
            _h[filled] = candle.H;
            _l[filled] = candle.L;
            _c[filled] = candle.C;
            _v[filled] = candle.V;
            _sym[filled] = candle.Sym;
            filled++;
        }

        _windowLoaded = filled;
        _windowStart = Math.Max(0, firstValidIdx);
        _fileCount = _uiCandleIndex?.Count ?? 0;

        if (resetView)
        {
            _xInited = false;
            _yInited = false;
        }

        DebugMessage.Write($"[CandleChartControl] step current={step.CurrentIdx} next={step.NextCursorIdx} loaded={_windowLoaded}");
        InvalidateVisual();
    }

    private bool AdvanceCandlesNext()
    {
        if (_uiCandleIndex == null || _uiCandleStep == null)
            return false;

        if (_uiCandleStep.NextCursorIdx == -1)
        {
            DebugMessage.Write("[CandleChartControl] fin du fichier courant pour CandlesNext");
            return false;
        }

        _uiCandleStep = _uiCandleIndex.CandlesNext(_uiCandleStep.NextCursorIdx, UiCandleRange);
        ApplyCandleStepToWindow(_uiCandleStep, resetView: false);
        return true;
    }
}
