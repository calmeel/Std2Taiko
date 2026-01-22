using OsuStdToTaiko;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsuStdToTaikoGui
{
    internal static class ConversionHelpers
    {
        // constant speed 1回分の出力結果
        internal readonly struct CsExportResult
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
        internal static CsExportResult ExportConstantSpeedOne(
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
            string? ver = OsuFileHelpers.TryReadMetadataVersion(text);

            // 最終ファイル名：入力の [Diff] を version に置換（取れなければ tmp名のまま）
            // ※ここはあなたの現行 RunConvert 内ローカル関数と互換
            string finalName = (ver != null)
                ? OsuFileHelpers.ReplaceBracketDifficulty(Path.GetFileName(inPath), ver)
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

        /// <summary>
        /// Constant speed 出力テキストから「SV clamped」行だけを抽出する
        /// （UI 表示ロジックは持たず、解析のみを担当）
        /// </summary>
        internal static List<string> ExtractClampWarningLines(string csText)
        {
            return csText
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.StartsWith("// [ConstantSpeed] SV clamped") && !l.Contains("(summary)"))
                .Distinct()
                .ToList();
        }

        internal sealed class SvaAnalysisResult
        {
            public List<StableVisualAssist.SvaSegment> ReplacementSegments { get; }
            public List<StableVisualAssist.SvaEffectWarning> EffectWarnings { get; }

            public SvaAnalysisResult(
                List<StableVisualAssist.SvaSegment> replacementSegments,
                List<StableVisualAssist.SvaEffectWarning> effectWarnings)
            {
                ReplacementSegments = replacementSegments;
                EffectWarnings = effectWarnings;
            }
        }

        /// <summary>
        /// csText から SVA の適用区間と警告情報を解析する（UI には依存しない）
        /// </summary>
        internal static SvaAnalysisResult AnalyzeSvaFromText(string csText)
        {
            var appliedSegs = StableVisualAssist.DetectAppliedSvaSegments(csText);

            // redline replacement info
            var rep = appliedSegs
                .Where(s => s.Multiplier > 1 && s.OldBpm > 0 && s.NewBpm > 0)
                .OrderBy(s => s.StartTimeMs)
                .ToList();

            var effWarns = StableVisualAssist.DetectGreenlineEffectWarnings(
                    csText,
                    appliedSegs,
                    multiplierHint: 0,
                    insertedGreenlineCount: 0
                )
                .ToList();

            return new SvaAnalysisResult(rep, effWarns);
        }
    }
}
