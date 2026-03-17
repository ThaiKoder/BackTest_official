using DatasetTool;
using System;
using System.Collections.Generic;
using System.IO;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    private FileIndex? _uiFileIndex;
    private CandleIndex? _uiCandleIndex;
    private FileIndex.FileCursorStep? _uiFileStep;
    private CandleIndex.CandleCursorStep? _uiCandleStep;

    private List<int> _uiPendingFileIdxs = new();
    private int _uiPendingFilePos = -1;

    private int RingPhysicalIndex(int logicalIndex)
    {
        return (_ringHead + logicalIndex) % WindowCount;
    }

    private void RingClear()
    {
        _ringHead = 0;
        _ringCount = 0;
        _ringFirstGlobalIdx = 0;
    }

    private void RingPushBack(long ts, long o, long h, long l, long c, uint v, byte sym)
    {
        if (_ringCount < WindowCount)
        {
            int p = (_ringHead + _ringCount) % WindowCount;
            _ts[p] = ts;
            _o[p] = o;
            _h[p] = h;
            _l[p] = l;
            _c[p] = c;
            _v[p] = v;
            _sym[p] = sym;
            _ringCount++;
            return;
        }

        // overwrite le plus ancien
        _ts[_ringHead] = ts;
        _o[_ringHead] = o;
        _h[_ringHead] = h;
        _l[_ringHead] = l;
        _c[_ringHead] = c;
        _v[_ringHead] = v;
        _sym[_ringHead] = sym;

        _ringHead = (_ringHead + 1) % WindowCount;
        _ringFirstGlobalIdx++;
    }

    private void RingPopFront(int count)
    {
        if (count <= 0 || _ringCount <= 0)
            return;

        if (count > _ringCount)
            count = _ringCount;

        _ringHead = (_ringHead + count) % WindowCount;
        _ringCount -= count;
        _ringFirstGlobalIdx += count;

        if (_ringCount == 0)
            _ringHead = 0;
    }

    private long RingTsAtLogical(int logicalIndex) => _ts[RingPhysicalIndex(logicalIndex)];
    private long RingOAtLogical(int logicalIndex) => _o[RingPhysicalIndex(logicalIndex)];
    private long RingHAtLogical(int logicalIndex) => _h[RingPhysicalIndex(logicalIndex)];
    private long RingLAtLogical(int logicalIndex) => _l[RingPhysicalIndex(logicalIndex)];
    private long RingCAtLogical(int logicalIndex) => _c[RingPhysicalIndex(logicalIndex)];
    private uint RingVAtLogical(int logicalIndex) => _v[RingPhysicalIndex(logicalIndex)];
    private byte RingSymAtLogical(int logicalIndex) => _sym[RingPhysicalIndex(logicalIndex)];

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

        // Premier batch : il faut traiter TOUS les fichiers valides du step initial
        SetPendingFilesFromStep(_uiFileStep, useWindow: true);

        if (_uiPendingFileIdxs.Count == 0)
        {
            DebugMessage.Write("[CandleChartControl] aucun fichier valide dans le premier step");
            return;
        }

        LoadCandlesForCurrentFileStep();
    }

    private void LoadCandlesForCurrentFileStep()
    {
        if (_uiFileIndex == null || _uiFileStep == null)
            return;

        if (_uiPendingFilePos < 0 || _uiPendingFilePos >= _uiPendingFileIdxs.Count)
            return;

        int fileIdx = _uiPendingFileIdxs[_uiPendingFilePos];

        var (startYmd, endYmd) = _uiFileIndex.Read(fileIdx);
        
        string filePath = Path.Combine(
            "data",
            "bin",
            $"glbx-mdp3-{startYmd}-{endYmd}.ohlcv-1m.bin");

        _uiCandleIndex?.Dispose();
        _uiCandleIndex = new CandleIndex();
        _uiCandleIndex.Load(filePath);

        _uiCandleStep = _uiCandleIndex.CandlesNext(0, UiCandleRange);
        ApplyCandleStepToWindow(_uiCandleStep, resetView: true);
    }

    private void ApplyCandleStepToWindow(CandleIndex.CandleCursorStep step, bool resetView)
    {
        if (resetView || _ringCount == 0)
        {
            RingClear();
            ResetIndicators();

            int firstValidIdx = -1;

            for (int i = 0; i < step.Window.Count; i++)
            {
                var candle = step.Window[i];
                if (candle.Idx == -1)
                    continue;

                if (firstValidIdx == -1)
                    firstValidIdx = candle.Idx;

                RingPushBack(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);

                FeedIndicators(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }

            _ringFirstGlobalIdx = Math.Max(0, firstValidIdx);
        }
        else
        {
            RingPopFront(step.Removed.Count);

            for (int i = 0; i < step.Added.Count; i++)
            {
                var candle = step.Added[i];
                if (candle.Idx == -1)
                    continue;

                RingPushBack(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);

                FeedIndicators(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }
        }

        _windowLoaded = _ringCount;
        _windowStart = _ringFirstGlobalIdx;
        _fileCount = _uiCandleIndex?.Count ?? 0;

        if (resetView)
        {
            _xInited = false;
            _yInited = false;
        }

        DebugMessage.Write(
            $"[CandleChartControl] step current={step.CurrentIdx} next={step.NextCursorIdx} " +
            $"loaded={_windowLoaded} head={_ringHead} firstGlobal={_ringFirstGlobalIdx}");

        InvalidateVisual();
    }

    private void RebuildWindowFromStep(CandleIndex.CandleCursorStep step)
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
    }


    private bool AdvanceCandlesNext()
    {
        if (_uiFileIndex == null || _uiFileStep == null)
            return false;

        // Pas encore de candles chargées
        if (_uiCandleIndex == null || _uiCandleStep == null)
        {
            if (_uiPendingFileIdxs.Count == 0)
                return false;

            LoadCandlesForCurrentFileStep();
            return _uiCandleStep != null;
        }

        // 1) Continuer dans le fichier courant
        if (_uiCandleStep.NextCursorIdx != -1)
        {
            _uiCandleStep = _uiCandleIndex.CandlesNext(_uiCandleStep.NextCursorIdx, UiCandleRange);
            ApplyCandleStepToWindow(_uiCandleStep, resetView: false);
            return true;
        }

        // 2) Fin du fichier courant => passer au fichier suivant déjà présent dans le step courant
        if (MoveToNextPendingFileInCurrentStep())
        {
            LoadCandlesForCurrentFileStep();
            return _uiCandleStep != null && _windowLoaded > 0;
        }

        // 3) Plus de fichiers en attente dans ce step => demander le step suivant de FilesNext
        if (_uiFileStep.NextCursorIdx == -1)
        {
            DebugMessage.Write("[CandleChartControl] fin du dernier fichier pour FilesNext/CandlesNext");
            return false;
        }

        _uiFileStep = _uiFileIndex.FilesNext(_uiFileStep.NextCursorIdx, UiFileRange);

        if (_uiFileStep.CurrentIdx < 0)
        {
            DebugMessage.Write("[CandleChartControl] fichier suivant invalide");
            return false;
        }

        // Sur les steps suivants, seuls les nouveaux fichiers à droite doivent être traités
        SetPendingFilesFromStep(_uiFileStep, useWindow: false);

        if (_uiPendingFileIdxs.Count == 0)
        {
            DebugMessage.Write("[CandleChartControl] aucun fichier ajouté dans le step suivant");
            return false;
        }

        LoadCandlesForCurrentFileStep();
        return _uiCandleStep != null && _windowLoaded > 0;
    }

    private void SetPendingFilesFromStep(FileIndex.FileCursorStep step, bool useWindow)
    {
        _uiPendingFileIdxs.Clear();

        var source = useWindow ? step.Window : step.Added;

        foreach (var file in source)
        {
            if (file.Idx == -1)
                continue;

            _uiPendingFileIdxs.Add(file.Idx);
        }

        _uiPendingFilePos = 0;
    }

    private bool MoveToNextPendingFileInCurrentStep()
    {
        if (_uiPendingFileIdxs.Count == 0)
            return false;

        int nextPos = _uiPendingFilePos + 1;
        if (nextPos >= _uiPendingFileIdxs.Count)
            return false;

        _uiPendingFilePos = nextPos;
        return true;
    }
}
