using System.Text;
using System.Reflection;

// ppy.osu.Game
// osu!lazer は完全にオープンソース（一方 osu!stable は完全非公開）なのでライブラリをダウンロードして使用する
using osu.Game.Beatmaps;                      // .osu を内部表現に変換・保持する中核クラス
using osu.Game.Beatmaps.Formats;              // .osu / .osz の パーサ・エンコーダ
using osu.Game.IO;                            // ストリーム、読み書き補助（内部用）
using osu.Game.Rulesets;                      // ゲームモード（standard / taiko / catch / mania）共通の基盤
using osu.Game.Rulesets.Osu;                  // OsuHitObject, Slider, SliderPath, PathControlPoint
using osu.Game.Rulesets.Taiko;                // TaikoHitObject, Hit, DrumRoll, Swell, TaikoBeatmapConverter

// ★ Decoder のあいまい参照回避（osu.Game側Decoderに別名を付ける）
using BeatmapDecoder = osu.Game.Beatmaps.Formats.Decoder;

namespace OsuStdToTaiko
{
    public static class ConverterCore
    {
        // 4引数版のラッパー
        public static void ConvertFile(string inputPath, string outputPath, OutputMode outputMode, bool lazerSafeOutput)
            => ConvertFile(inputPath, outputPath, outputMode, lazerSafeOutput,
                constantSpeed: false,
                enableSva: false);

        // ConvertFile
        public static void ConvertFile(
            string inputPath,
            string outputPath,
            OutputMode outputMode,
            bool lazerSafeOutput,
            bool constantSpeed = false,
            bool enableSva = false,
            bool enableAspireTpMeterFix = false,
            bool enableAspireTpBeatLengthClamp = false,
            bool enableAspireSimplifyExtremeSliders = false,
            bool enableAspireClampSliderControlPoints = false)
        {
            bool LAZER_SAFE_OUTPUT = lazerSafeOutput;

            // 0) RulesetStore（Osu/Taikoなど参照しているRuleset DLLから一覧を作る）
            var rulesets = new AssemblyRulesetStore();

            // 1) Decode (.osu -> Beatmap)
            // Aspire譜面など「文法的に壊れた slider / TimingPoints」を先に最小修正してから decode する
            AspireSliderSanitizer.Report aspireReport; // ★後で splitter に渡すため外に出す

            string inputTextForDecode = File.ReadAllText(inputPath, Encoding.UTF8);

            // ★TimingPoints の meter(分子) が 0以下なら 4 に補正（-4 などで decoder が落ちるのを防ぐ）
            if (enableAspireTpMeterFix)
            {
                inputTextForDecode = AspireTimingPointSanitizer.SanitizeTimingPointsMeter(inputTextForDecode, out int fixedMeter);
                if (fixedMeter > 0) Console.WriteLine($"[AspireTP] fixed meter count={fixedMeter}");
            }

            // ★TimingPoints の beatLength が巨大/極小で decoder が落ちるのを防ぐ（9.8E+304 等）
            if (enableAspireTpBeatLengthClamp)
            {
                inputTextForDecode = AspireTimingPointSanitizer.SanitizeTimingPointsBeatLengthFloatRange(inputTextForDecode, out int fixedBeatLen);
                if (fixedBeatLen > 0) Console.WriteLine($"[AspireTP] fixed beatLength count={fixedBeatLen}");
            }

            // ★デカすぎる slider を decode 用に最小形状へ潰す（osu!frameworkの "Value is too high" 回避）
            if (enableAspireSimplifyExtremeSliders)
            {
                inputTextForDecode = AspireHitObjectPathSanitizer.SimplifyExtremeSlidersForDecode(inputTextForDecode, out int simplified);
                if (simplified > 0) Console.WriteLine($"[AspireHO] simplified extreme sliders count={simplified}");
            }

            // ★slider の制御点座標が極端な範囲外の場合、decoder が落ちるのを防ぐためクランプする
            if (enableAspireClampSliderControlPoints)
            {
                inputTextForDecode = AspireHitObjectPathSanitizer.ClampSliderControlPoints(inputTextForDecode, out int fixedControlPoints);
                if (fixedControlPoints > 0) Console.WriteLine($"[AspireHO] clamped slider control points count={fixedControlPoints}");
            }

            // ★HitObjects の異常slider（curve単体 / pixelLength<=0）を補正
            inputTextForDecode = AspireSliderSanitizer.SanitizeHitObjects(inputTextForDecode, out aspireReport);
            if (aspireReport.HasAnyFix)
            {
                Console.WriteLine($"[AspireSanitize] touched={aspireReport.LinesTouched}, " +
                                  $"fixMissingCP={aspireReport.FixedMissingControlPoints}, " +
                                  $"fixNonPositiveLen={aspireReport.FixedNonPositivePixelLength}");
                foreach (var s in aspireReport.Samples)
                    Console.WriteLine($"[AspireSanitize] {s}");
            }
            // ★ログ：NoSplitStartTimes がここでは何件あるか
            Console.WriteLine($"[AspireNoSplit@decode] count={aspireReport.NoSplitStartTimes.Count}");


            Beatmap beatmap;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(inputTextForDecode)))
            using (var lbr = new LineBufferedReader(ms, true))
            {
                var decoder = new LegacyBeatmapDecoder();

                // ★ osu.Game側のDecoderを明示して呼ぶ
                BeatmapDecoder.RegisterDependencies(rulesets);

                // この版では Decode(LineBufferedReader, LineBufferedReader[]) が必要
                beatmap = decoder.Decode(lbr, Array.Empty<LineBufferedReader>());
            }


            // 念のため ruleset を明示（standardとして扱う）
            beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;

            Console.WriteLine($"Decoded: HitObjects={beatmap.HitObjects.Count}, Ruleset={beatmap.BeatmapInfo.Ruleset.ShortName}");

            // 2) Convert (osu!standard -> osu!taiko)
            Ruleset taiko = new TaikoRuleset();

            // 入力が osu!standard であることを明示
            beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;

            var converter = taiko.CreateBeatmapConverter(beatmap);

            // ★ここが重要：Convert() の「戻り値」を反射で取る（版差に強い）
            var convertMethod = converter.GetType().GetMethod(
                "Convert",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );

            object? convertResult = convertMethod?.Invoke(converter, null);

            // 変換済みビートマップを取得する（優先順）
            // 1) Convert() の戻り値が IBeatmap
            // 2) converter のプロパティの中に IBeatmap があればそれ
            osu.Game.Beatmaps.IBeatmap? taikoBeatmap =
                convertResult as osu.Game.Beatmaps.IBeatmap;

            // プロパティ探索（IBeatmap型のプロパティを全部探す）
            if (taikoBeatmap == null)
            {
                var props = converter.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (!typeof(osu.Game.Beatmaps.IBeatmap).IsAssignableFrom(p.PropertyType)) continue;
                    taikoBeatmap = p.GetValue(converter) as osu.Game.Beatmaps.IBeatmap;
                    if (taikoBeatmap != null) break;
                }
            }

            if (taikoBeatmap == null)
                throw new Exception("Taiko converted beatmap could not be obtained (Convert result/properties did not provide IBeatmap).");

            // Ruleset を taiko に確定（エンコード時に Mode:1 を出させるため）
            taikoBeatmap.BeatmapInfo.Ruleset = taiko.RulesetInfo;

            Console.WriteLine($"Converted: HitObjects={taikoBeatmap.HitObjects.Count}, Ruleset={taikoBeatmap.BeatmapInfo.Ruleset.ShortName}");

            // ★中身が本当に Taiko になったか確認（重要）
            var topTypes = taikoBeatmap.HitObjects
                .Select(o => o.GetType().FullName ?? o.GetType().Name)
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(8);

            Console.WriteLine("Top object types:");
            foreach (var g in topTypes)
                Console.WriteLine($"  {g.Key} x{g.Count()}");

            // 3) Encode (Beatmap -> .osu)
            using (var fs = File.Create(outputPath))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                // ★ あなたの版では (IBeatmap, ISkin?) が必要。nullでOK。
                var encoder = new LegacyBeatmapEncoder(taikoBeatmap, null);
                encoder.Encode(sw);
            }

            // ★ 後処理：出力された .osu を読み直して slider→hit 分解して上書き
            string encodedOsuText = File.ReadAllText(outputPath, Encoding.UTF8);
            int slidersBefore = OsuTextStats.CountSliders(encodedOsuText);

            // 原本を読む
            string inputOsuText = File.ReadAllText(inputPath, Encoding.UTF8);

            // ★ 入力譜面の TimingPoints を使って SV/BeatLength を再現（出力側でSVが欠落する環境対策）
            var timingOverride = TimingPointParser.ParseTimingPointsFromOsuText(inputOsuText);
            int inputBeatmapVersion = OsuTextUtils.GetInputBeatmapVersion(inputOsuText);

            Console.WriteLine($"[AspireNoSplit@split] count={aspireReport.NoSplitStartTimes.Count}");

            // split を適用した後のスライダー数を数える（Aspire sanitizer で触った slider は分解禁止）
            string after = SliderSplitter.SplitSlidersToTaikoHits_LazerLike(
                encodedOsuText,
                timingOverride,
                inputBeatmapVersion,
                aspireReport.NoSplitStartTimes);

            int slidersAfter = OsuTextStats.CountSliders(after);


            // 診断用
            // 変換前に inputとencoder出力の差 のTimingPoints差分をログ
            const bool LOG_TIMING_DIFF = false;
            if (LOG_TIMING_DIFF)
                TimingDiagnostics.LogTimingPointsDiff("input_vs_encoded", inputOsuText, encodedOsuText);

            // ★ Metadata の BeatmapID を 0 に固定（既存のBeatmapとの重複防止）
            after = OsuTextUtils.ForceBeatmapIDZero(after);

            // ★ Metadata の Version に suffix を付与（例: "Extreme" -> "Extreme taiko convert"）
            after = OsuTextUtils.AppendSuffixToDifficultyVersion(after, OutputNaming.BuildTaikoConvertVersionSuffix());


            // ★ TimingPoints を入力のものに戻す
            // TimingPoints を input -> encoded に処理した際の差異は以下の通り
            // １：SVの正規化（0.1以下のSV、10以上のSVをクランプする）                   ：実装済み
            // ２：近すぎるSV線を削除・統合する                                         ：未実装（これを実装してしまうと大幅に見た目が変わってしまうので不採用）
            // ３：time（時刻）を拍境界/内部単位に量子化する（＋小数化）                 ：未実装（必要かどうかは現在のところ判断できない）
            // ４：一定の判断基準で effects に 8/9 （omit first barline）を付与する     ：未実装（必要かどうかは現在のところ判断できない）
            // ５：その他の処理                                                       ：未実装（必要かどうかは現在のところ判断できない）
            bool USE_INPUT_TIMINGPOINTS_FOR_OUTPUT = true;
            if (USE_INPUT_TIMINGPOINTS_FOR_OUTPUT)
            {
                var clampedInputTiming = TimingPointsEditor.ClampSvInTimingPoints(inputOsuText);
                if (LAZER_SAFE_OUTPUT)
                    clampedInputTiming = LazerSanitizer.SanitizeTimingPointsForLazer(clampedInputTiming);

                after = TimingPointsEditor.ReplaceTimingPointsSection(after, clampedInputTiming);
            }
            else
            {
                // 何もしない（encoded TimingPoints を保持）
            }


            // SVA適用フラグ
            var segs = new List<StableVisualAssist.SvaSegment>();
            bool svaApplied = false;

            // constant speed / SVA を当てる前の本文を保存
            // （この時点の after は「taiko convert ＋ TimingPoints 差し戻し」済み）
            string beforeConstantSpeed = after;

            if (constantSpeed)
            {
                // 1) base: クランプあり（通常の constant speed 出力）
                after = ConstantSpeedTools.ApplyConstantSpeedTimingPoints(
                    beforeConstantSpeed,
                    svMin: 0.01,
                    svMax: 10.0,
                    emitClampComments: true
                );

                if (enableSva)
                {
                    // 2) SVA区間検出（ここで segs を埋める）
                    //    ⇒ 「クランプあり constant speed」をもとに high/low SV 区間を検出
                    segs = StableVisualAssist.DetectSvaSegments(after, svMax: 10.0, svMin: 0.01);

                    if (segs.Count > 0)
                    {
                        // 3) adjusted 用の base: 上限クランプ無しで constant speed を作り直す
                        //    （ここが「完全等速（理想）」の基準になる）
                        string csNoClamp = ConstantSpeedTools.ApplyConstantSpeedTimingPoints(
                            beforeConstantSpeed,
                            svMin: 0.0,
                            svMax: double.PositiveInfinity,  // ★SV 上限クランプ無し
                            emitClampComments: false         // ★adjusted には [ConstantSpeed] コメント不要
                        );

                        Console.WriteLine("[DEBUG] after cs(no clamp): " +
                            csNoClamp.Split('\n').FirstOrDefault(l => l.StartsWith("96348,") && l.Contains(",-")));

                        // 4) SVA: 赤線倍化 + 低SVクランプ対応などで TimingPoints を調整
                        string afterSva = StableVisualAssist.ApplyRedlineDoublingOnly(csNoClamp, segs);

                        // 5) SVA による BPM/SV 変更後も、
                        //    constant speed (csNoClamp) と同じ「時間長さ」を維持するよう
                        //    slider/drumroll の pixelLength を再スケール
                        after = ConstantSpeedTools.ApplySliderDurationFixForSva(csNoClamp, afterSva);

                        svaApplied = true;
                    }
                }

                // suffix は「実際にSVAを適用したか」で決める
                string csSuffix =
                    OutputNaming.BuildDifficultyVersionSuffix(constantSpeed: true, svaApplied: svaApplied);
                after = OsuTextUtils.AppendSuffixToDifficultyVersion(after, csSuffix);
            }

            // ★ OutputMode に応じて .osu file format version を設定（これがないとLazer仕様のv128になり、osu!stableで読み込むとエラーが出る可能性がある）
            int inputVersion = OsuTextUtils.GetInputBeatmapVersion(inputOsuText);

            int outVersion = outputMode switch
            {
                OutputMode.Lazer => 128,
                OutputMode.Stable => 14,
                OutputMode.Original => inputVersion,
                _ => 128
            };

            after = OsuTextUtils.RewriteOsuFileVersion(after, outVersion);
            if (LAZER_SAFE_OUTPUT)
            {
                // lazer-safe: HitObjects 側の「極端 slider length」など致命的な壊れ値だけ救済
                after = LazerSanitizer.SanitizeHitObjectsForLazer(after);
            }


            if (svaApplied && segs.Count > 0)
            {
                // 警告：SVA範囲内にスライダー
                Console.WriteLine("[SVA] running warning scan...");
                var warnings = StableVisualAssist.DetectLongObjectsInSvaSegments(after, segs, sampleLimit: 5);
                Console.WriteLine($"[SVA] warning scan result = {warnings.Count}");
                StableVisualAssist.PrintSvaWarningsToConsole(warnings);

                // 警告：SVA範囲内に緑線
                var effWarns = StableVisualAssist.DetectGreenlineEffectWarnings(after, segs, multiplierHint: 0, insertedGreenlineCount: 0);
                StableVisualAssist.PrintGreenlineEffectWarningsToConsole(effWarns);
            }


            File.WriteAllText(outputPath, after, new UTF8Encoding(false));
            Console.WriteLine($"OutputMode={outputMode}, osu file format v{outVersion}");

            Console.WriteLine($"PostProcess: sliders {slidersBefore} -> {slidersAfter}");
            Console.WriteLine("PostProcess: slider->hit split applied.");

            Console.WriteLine("OK: 出力しました -> " + outputPath);
        }


    }
}
