using System.Text;
using OsuStdToTaiko;

namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // ボタン→変換起動
        void OnRunClick(object? sender, EventArgs e) => RunConvert();


        // constant speed 1回分の出力結果
        private readonly struct CsExportResult
        {
            public readonly string FinalPath;
            public readonly string? Version;
            public readonly string CsText;

            public CsExportResult(string finalPath, string? version, string csText)
            {
                FinalPath = finalPath;
                Version = version;
                CsText = csText;
            }
        }

        // 共通：1回分の constant speed 出力を行い、最終パスとVersionと本文を返す
        private CsExportResult ExportConstantSpeedOne(
            string inPath,
            string outDir,
            OutputMode outputMode,
            bool lazerSafe,
            bool enableSvaForThisRun
        )
        {
            // tmp名はユニークにする（2回呼ぶため衝突回避）
            string tmpName = $"__tmp_cs_{(enableSvaForThisRun ? "adj" : "base")}_{Guid.NewGuid():N}.osu";
            string tmpPath = Path.Combine(outDir, tmpName);

            // Convert
            ConverterCore.ConvertFile(
                inPath,
                tmpPath,
                outputMode,
                lazerSafe,
                constantSpeed: true,
                enableSva: enableSvaForThisRun
            );

            // 出力された .osu を読む
            string text = File.ReadAllText(tmpPath, Encoding.UTF8);
            string? ver = TryReadMetadataVersion(text);

            // 最終ファイル名：入力の [Diff] を version に置換（取れなければ tmp名のまま）
            // ※ここはあなたの現行 RunConvert 内ローカル関数と互換
            string finalName = (ver != null)
                ? ReplaceBracketDifficulty(Path.GetFileName(inPath), ver)
                : tmpName;

            string finalPath = Path.Combine(outDir, finalName);

            // リネーム（同名があれば上書き）
            if (!finalPath.Equals(tmpPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(finalPath))
                    File.Delete(finalPath);

                File.Move(tmpPath, finalPath);
            }

            // ★返す text は finalPath の内容と同一（Move後も中身は同じ）
            return new CsExportResult(finalPath, ver, text);
        }

        // 共通：クランプ警告を csText から拾って出す
        private void ShowClampWarningsFromText(string csText)
        {
            var clampLines = csText
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.StartsWith("// [ConstantSpeed] SV clamped") && !l.Contains("(summary)"))
                .Distinct()
                .ToList();

            if (clampLines.Count > 0)
            {
                LogColored(T("CsClampWarnHeader"), LogWarnColor);
                foreach (var l in clampLines)
                    LogColored(string.Format(T("CsClampWarnLine"), l), LogWarnColor);
            }
        }

        // 共通：SVA warning scan（csTextを使う）
        private void ShowSvaWarningsFromText(string csText)
        {
            try
            {
                var appliedSegs = StableVisualAssist.DetectAppliedSvaSegments(csText);

                // --- SVA: redline replacement info (BPM A -> B) ---
                var rep = appliedSegs
                    .Where(s => s.Multiplier > 1 && s.OldBpm > 0 && s.NewBpm > 0)
                    .OrderBy(s => s.StartTimeMs)
                    .ToList();

                if (rep.Count > 0)
                {
                    LogColored(T("SvaReplaceHeader"), LogWarnColor);
                    foreach (var s in rep)
                    {
                        string endStr = double.IsInfinity(s.EndTimeMs)
                            ? "INF"
                            : ((int)Math.Round(s.EndTimeMs)).ToString();

                        LogColored(
                            string.Format(
                                T("SvaReplaceLine"),
                                (int)Math.Round(s.StartTimeMs),
                                endStr,
                                s.Multiplier,
                                s.OldBpm,
                                s.NewBpm,
                                s.MergedReds,
                                s.InsertedGreens
                            ),
                            LogWarnColor
                        );
                    }
                }
                // --- /SVA: redline replacement info ---

                var effWarns = StableVisualAssist.DetectGreenlineEffectWarnings(
                    csText,
                    appliedSegs,
                    multiplierHint: 0,
                    insertedGreenlineCount: 0
                );

                if (effWarns.Any())
                {
                    LogColored(T("SvaWarnHeader"), LogWarnColor);

                    foreach (var w in effWarns)
                    {
                        string endStr = double.IsInfinity(w.SegEnd)
                            ? "INF"
                            : ((int)Math.Round(w.SegEnd)).ToString();

                        LogColored(
                            string.Format(
                                T("SvaWarnSegment"),
                                (int)Math.Round(w.SegStart),
                                endStr,
                                w.Multiplier,
                                w.GreenlineCount,
                                w.InsertedGreenlineCount
                            ),
                            LogWarnColor
                        );

                        LogColored(T("SvaWarnNote"), LogWarnColor);

                        foreach (var s in w.Samples)
                            LogColored(string.Format(T("SvaWarnGreenLine"), s), LogWarnColor);
                    }
                }
            }
            catch (Exception exWarn)
            {
                LogColored($"[SVA] warn-scan failed: {exWarn.Message}", LogWarnColor);
            }
        }

        // コンバーター本体呼び出し
        private void RunConvert()
        {
            string inPath = txtIn.Text;

            if (string.IsNullOrWhiteSpace(inPath) || !File.Exists(inPath))
            {
                MessageBox.Show(T("InvalidInputFile"));
                return;
            }

            // osu!standard 譜面以外は弾く
            string osuText = File.ReadAllText(inPath, Encoding.UTF8);
            int beatmapMode = GetGeneralModeOrDefault(osuText);

            if (beatmapMode != 0)
            {
                MessageBox.Show(T("ErrNotStandard"));
                return;
            }

            // 出力先は同じフォルダ固定
            string outDir = Path.GetDirectoryName(inPath)!;

            OutputMode outputMode = (cmbMode.SelectedItem as ModeItem)?.Mode ?? OutputMode.Lazer;
            bool lazerSafe = chkLazerSafe.Checked;

            // ★ Constant speed を出すか
            bool constantSpeed = chkConstantSpeed.Checked;

            // 通常出力
            string outName = MakeTaikoOutputFileName(Path.GetFileName(inPath), constantSpeed: false, adjusted: false);
            string outPath = Path.Combine(outDir, outName);

            try
            {
                LogColored($"✔ OK → {Path.GetFileName(inPath)} -> {outName}", LogOkColor);
                LogColored($"📁 Output → {outPath}", LogOutputColor);

                // 通常出力（constantSpeed=false）
                ConverterCore.ConvertFile(inPath, outPath, outputMode, lazerSafe, constantSpeed: false);

                // Constant speed 出力（オプション）
                if (constantSpeed)
                {
                    bool enableAdjustUi = chkSva?.Checked ?? false; // ※UI名はそのまま chkSva を想定

                    // -------------------------
                    // 1) base: (constant speed) を必ず出す
                    // -------------------------
                    var baseOut = ExportConstantSpeedOne(
                        inPath: inPath,
                        outDir: outDir,
                        outputMode: outputMode,
                        lazerSafe: lazerSafe,
                        enableSvaForThisRun: false
                    );

                    LogColored(
                        $"✔ OK ({(baseOut.Version ?? "constant speed")}) → {Path.GetFileName(inPath)} -> {Path.GetFileName(baseOut.FinalPath)}",
                        LogOkConstantColor
                    );
                    LogColored($"📁 Output → {baseOut.FinalPath}", LogOutputColor);

                    // clamp warning (base)
                    ShowClampWarningsFromText(baseOut.CsText);

                    // -------------------------
                    // 2) adjusted: UIがONなら追加で出す（ただし不要なら出さない）
                    // -------------------------
                    if (enableAdjustUi)
                    {
                        var adjOut = ExportConstantSpeedOne(
                            inPath: inPath,
                            outDir: outDir,
                            outputMode: outputMode,
                            lazerSafe: lazerSafe,
                            enableSvaForThisRun: true
                        );

                        bool isAdjusted =
                            adjOut.Version != null &&
                            adjOut.Version.Contains("constant speed adjusted", StringComparison.OrdinalIgnoreCase);

                        // SVAが不要だった場合：Program.cs側で adjusted suffix が付かない → baseと同じ版になる可能性が高い
                        // その場合は adjusted を残す意味が薄いので削除して「スキップ」ログだけ出す
                        if (!isAdjusted || Path.GetFileName(adjOut.FinalPath).Equals(Path.GetFileName(baseOut.FinalPath), StringComparison.OrdinalIgnoreCase))
                        {
                            // 既に base と同名にされて上書きされている可能性があるので、削除は慎重に
                            // adjOut.FinalPath が baseOut.FinalPath と同じなら削除しない（baseが消える）
                            if (!adjOut.FinalPath.Equals(baseOut.FinalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    if (File.Exists(adjOut.FinalPath)) File.Delete(adjOut.FinalPath);
                                }
                                catch
                                {
                                    /* non-fatal */
                                }
                            }

                            LogColored(T("SvaSkipNote"), LogOutputColor);
                        }
                        else
                        {
                            LogColored(
                                $"✔ OK ({adjOut.Version}) → {Path.GetFileName(inPath)} -> {Path.GetFileName(adjOut.FinalPath)}",
                                LogOkConstantColor
                            );
                            LogColored($"📁 Output → {adjOut.FinalPath}", LogOutputColor);

                            // SVA warning scan (adjusted)
                            ShowSvaWarningsFromText(adjOut.CsText);

                            // --- DrumRoll / Swell warning (adjusted only) ---
                            try
                            {
                                string adjText = adjOut.CsText;
                                var appliedSegs = StableVisualAssist.DetectAppliedSvaSegments(adjText);
                                var counts = CountRollAndSwellInSegments(adjText, appliedSegs);

                                // 閾値：とりあえず「区間内で slider+spinner が 3 以上」または「区間内オブジェクトの 30% 以上が roll/swell」
                                const int ABS_THRESHOLD = 3;
                                const double RATIO_THRESHOLD = 0.30;

                                var warns = counts
                                    .Where(x =>
                                    {
                                        int rs = x.sliderCount + x.spinnerCount;
                                        if (rs >= ABS_THRESHOLD) return true;
                                        if (x.totalInSeg <= 0) return false;
                                        return (rs / (double)x.totalInSeg) >= RATIO_THRESHOLD;
                                    })
                                    .ToList();

                                if (warns.Count > 0)
                                {
                                    LogColored(T("SvaRollSwellWarnHeader"), LogWarnColor);

                                    foreach (var w in warns)
                                    {
                                        string endStr = double.IsInfinity(w.end) ? "INF" : ((int)Math.Round(w.end)).ToString();
                                        LogColored(
                                            string.Format(
                                                T("SvaRollSwellWarnLine"),
                                                (int)Math.Round(w.start),
                                                endStr,
                                                w.sliderCount,
                                                w.spinnerCount,
                                                w.totalInSeg
                                            ),
                                            LogWarnColor
                                        );
                                    }

                                    LogColored(T("SvaRollSwellWarnNote"), LogWarnColor);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogColored($"[SVA] roll/swell warn-scan failed: {ex.Message}", LogWarnColor);
                            }
                            // --- /DrumRoll / Swell warning ---
                        }
                    }
                }

                Log("");

                MessageBox.Show(
                    T("MsgConvertSuccessBody"),
                    T("Done"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                LogColored($"✖ Error → {ex.Message}", LogErrorColor);
                MessageBox.Show(ex.ToString(), T("MsgConvertErrorTitle"));
            }
        }

    }
}
