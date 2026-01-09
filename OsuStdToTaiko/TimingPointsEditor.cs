using System.Globalization;
using System.Text;

namespace OsuStdToTaiko
{
    internal static class TimingPointsEditor
    {
        // 出力用 [TimingPoints] を整形する：
        // - 赤線（uninherited==1）は「NaN/Inf の削除」以外は保持（負値/極端値/0/非正規化も保持）
        // - 緑線（uninherited==0）のみ SV を 0.1～10 にクランプ（= beatLength を [-1000, -10] 相当へ）。ただし beatLength が正の値の場合はそのまま保持
        // - 赤線/緑線どちらでも beatLength が NaN/±Infinity の行は削除
        // ■ 赤線の処理
        // ・uninherited == 1（赤線）のみを BPM 線として扱う
        // ・beatLength = NaN            → 削除（表示上は読める場合もあるが不安定）
        // ・beatLength = ±Infinity      → 削除（正常に読み込めない）
        // ・それ以外（負値 / 極端値 / 非正規化 / 0）は入力をそのまま維持
        internal static string ClampSvInTimingPoints(string text)
        {
            var inv = CultureInfo.InvariantCulture;

            // text がファイル全体の場合もあれば、[TimingPoints] 部分だけの場合もあり得るので両対応する
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            var outLines = new List<string>(lines.Length);
            bool inTiming = false;
            bool foundHeader = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // セクション判定
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    // 次のセクションに入ったら TimingPoints 終了
                    if (inTiming && line != "[TimingPoints]")
                        inTiming = false;

                    if (line == "[TimingPoints]")
                    {
                        inTiming = true;
                        foundHeader = true;
                        outLines.Add(line);
                        continue;
                    }

                    // TimingPoints 以外のヘッダはそのまま（全体テキストの場合）
                    if (!foundHeader) outLines.Add(line);
                    else if (!inTiming) outLines.Add(line);

                    continue;
                }

                // TimingPoints セクション外（全体テキストの場合はそのまま返す）
                if (!inTiming)
                {
                    // 「TimingPoints 部分だけ渡されている」ケースでは、ここには何も入れず後で処理する
                    if (!foundHeader)
                        outLines.Add(line);
                    else
                        outLines.Add(line);

                    continue;
                }

                // TimingPoints セクション内：空行はそのまま
                if (string.IsNullOrWhiteSpace(line))
                {
                    outLines.Add(line);
                    continue;
                }

                // カンマ区切り（8列想定）
                // time, beatLength, meter, sampleSet, sampleIndex, volume, uninherited, effects
                var parts = line.Split(',');
                if (parts.Length < 8)
                {
                    // 壊れている行は保持（削除しない）
                    outLines.Add(line);
                    continue;
                }

                // meter（列3）は lazer では 1 以上必須
                // Aspire 譜面では 0 / 負値が出るため、出力時のみ最低限正規化する
                if (int.TryParse(parts[2], NumberStyles.Integer, inv, out int meter))
                {
                    if (meter <= 0)
                        parts[2] = "4"; // デフォルト 4/4（意味を壊しにくい）
                }

                // beatLength を parse（NaN/Infinity も parse は成功する）
                if (!double.TryParse(parts[1], NumberStyles.Float, inv, out double beatLen))
                {
                    // parse 不能なら保持（削除しない）
                    outLines.Add(line);
                    continue;
                }

                // NaN / Infinity は赤線・緑線どちらでも削除
                if (double.IsNaN(beatLen) || double.IsInfinity(beatLen))
                    continue;

                // uninherited 判定（列7）
                if (!int.TryParse(parts[6], NumberStyles.Integer, inv, out int uninherited))
                {
                    // 判定不能なら保持（削除しない）
                    outLines.Add(line);
                    continue;
                }

                // 緑線（SV線）のみクランプ
                if (uninherited == 0)
                {
                    // 緑線で beatLength が正の値の場合：入力を保持する（osu!では SV=1.0 相当表示）
                    if (beatLen > 0.0)
                    {
                        outLines.Add(line);
                        continue;
                    }

                    // SV = -100 / beatLength
                    // beatLen=0 だと Infinity になるので、ここで明示的に扱う
                    double sv;

                    if (beatLen == 0.0)
                    {
                        // -100/0 → -Infinity になる扱いに揃える
                        sv = double.NegativeInfinity;
                    }
                    else
                    {
                        sv = -100.0 / beatLen;
                    }

                    // sv が NaN は削除（ただし beatLen が NaN/Inf は上で消しているので保険）
                    if (double.IsNaN(sv))
                        continue;

                    // sv が ±Infinity の場合は仕様通りに救済
                    if (double.IsInfinity(sv))
                    {
                        sv = (sv < 0) ? 0.1 : 10.0;
                    }

                    // 最終クランプ
                    sv = Math.Clamp(sv, 0.1, 10.0);

                    // beatLength に戻す（-100/sv）
                    beatLen = -100.0 / sv;

                    // 念のため
                    if (double.IsNaN(beatLen) || double.IsInfinity(beatLen))
                        continue;

                    // 文字列表現：元の桁感を壊しにくい "G17"
                    parts[1] = beatLen.ToString("G17", inv);

                    outLines.Add(string.Join(",", parts));
                }
                else
                {
                    // 赤線（uninherited==1）は一切クランプしない（NaN/Inf は上で削除済み）
                    outLines.Add(line);
                }
            }

            // 「TimingPoints 部分だけ」入力された場合（ヘッダなし）に備えて補正：
            // - ヘッダが無い＆中身っぽい行がある場合は、[TimingPoints] を付けて返す
            //   ※ ReplaceTimingPointsSection が [TimingPoints] ヘッダ付き文字列を期待する想定
            if (!foundHeader)
            {
                // 既に outLines に [TimingPoints] が無いので先頭に付ける（入力が TimingPoints の行だけだったケース）
                var sb = new StringBuilder();
                sb.AppendLine("[TimingPoints]");
                foreach (var l in outLines)
                    sb.AppendLine(l);
                return sb.ToString();
            }

            return string.Join("\n", outLines);
        }

        // [TimingPoints] ブロックを書き換える関数
        internal static string ReplaceTimingPointsSection(string outputOsuText, string inputOsuText)
        {
            // 入力側から [TimingPoints] ブロック（中身行）を抽出
            var inputLines = inputOsuText.Replace("\r\n", "\n").Split('\n').ToList();
            int inIdxTiming = inputLines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            List<string> inputTimingBody = new List<string>();

            if (inIdxTiming >= 0)
            {
                for (int i = inIdxTiming + 1; i < inputLines.Count; i++)
                {
                    string t = inputLines[i];
                    string tt = t.Trim();

                    if (tt.StartsWith("[") && tt.EndsWith("]"))
                        break;

                    // ここは「丸ごと置換」方針なので、空行やコメント含めて保持してOK
                    // ただし末尾の余計な \r は上で除去済み
                    inputTimingBody.Add(t);
                }
            }

            // 入力に [TimingPoints] が無いなら、何もしない（安全側）
            if (inIdxTiming < 0)
                return outputOsuText;

            // 出力側の [TimingPoints] を探して置換
            var outLines = outputOsuText.Replace("\r\n", "\n").Split('\n').ToList();
            int outIdxTiming = outLines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));

            if (outIdxTiming >= 0)
            {
                // 既存の TimingPoints 範囲を削除（header の次行〜次の [Section] の直前まで）
                int start = outIdxTiming + 1;
                int end = start;
                for (; end < outLines.Count; end++)
                {
                    string tt = outLines[end].Trim();
                    if (tt.StartsWith("[") && tt.EndsWith("]"))
                        break;
                }

                // remove [start, end)
                outLines.RemoveRange(start, end - start);

                // insert input timing body
                outLines.InsertRange(start, inputTimingBody);
            }
            else
            {
                // 出力に [TimingPoints] が無い場合：基本は [HitObjects] の直前に挿入
                int idxHit = outLines.FindIndex(l => l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));
                int insertAt = (idxHit >= 0) ? idxHit : outLines.Count;

                outLines.Insert(insertAt, "[TimingPoints]");
                outLines.InsertRange(insertAt + 1, inputTimingBody);

                // もし [TimingPoints] の直後に空行が欲しければ、ここで outLines.Insert(...) してもOK
            }

            return string.Join("\n", outLines).Replace("\n", "\r\n");
        }
    }
}
