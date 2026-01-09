using System.Globalization;

// ppy.osu.Game
using osu.Game.Rulesets.Objects;              // HitObject（全モード共通の基底）
using osu.Game.Rulesets.Objects.Types;        // IHasDuration, IHasPath などのインターフェース
using osuTK;                                  // osu!lazer が内部で使っている 数学 / ベクトルライブラリ。SliderPath の制御点計算用

namespace OsuStdToTaiko
{
    // 「SliderPath.cs」をほぼそのまま流用
    internal static class LazerSliderPathDistance
    {
        public static (double calculatedDistance, double distance) Compute(
            string curve, int startX, int startY, double pixelLength)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));

            var cps = BuildControlPointsFromLegacy(curve, startX, startY);

            // ExpectedDistance = .osu の pixelLength（公式の前提）
            var path = new SliderPath(cps, expectedDistance: pixelLength);

            return (path.CalculatedDistance, path.Distance);
        }

        private static PathControlPoint[] BuildControlPointsFromLegacy(string curve, int startX, int startY)
        {
            // curve: "B|x:y|x:y|..." / "C|..." / "P|..." / "L|..."
            char ctype = curve.Length > 0 ? char.ToUpperInvariant(curve[0]) : 'L';

            PathType type = ctype switch
            {
                'B' => PathType.BEZIER,        // B3 等は一旦 BEZIER として扱う（まず公式経路優先）
                'C' => PathType.CATMULL,
                'P' => PathType.PERFECT_CURVE,
                _ => PathType.LINEAR,
            };

            // legacyの点は絶対座標なので、SliderPathの期待（開始点相対）に合わせて相対化する
            var points = new List<Vector2>
        {
            Vector2.Zero // start point relative
        };

            var toks = curve.Split('|');
            for (int i = 1; i < toks.Length; i++)
            {
                var xy = toks[i].Split(':');
                if (xy.Length != 2) continue;

                if (float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ax) &&
                    float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ay))
                {
                    points.Add(new Vector2(ax - startX, ay - startY));
                }
            }

            var cps = new List<PathControlPoint>(points.Count);

            if (points.Count == 0)
                return cps.ToArray();

            // 最初は type を付ける（ここがセグメントの開始）
            cps.Add(new PathControlPoint(points[0], type));

            for (int i = 1; i < points.Count; i++)
            {
                var p = points[i];
                var prev = points[i - 1];

                // legacyの「重複点＝ベジェセグメント境界」を SliderPath に伝える
                // 同一点が来たら、同じ点を2回入れて「次のセグメント開始(Typeあり)」を明示する
                if (ctype == 'B' && p == prev && i < points.Count - 1)
                {
                    cps.Add(new PathControlPoint(p, null)); // 前セグメント終端
                    cps.Add(new PathControlPoint(p, type)); // 次セグメント開始
                    continue;
                }

                cps.Add(new PathControlPoint(p, null));
            }

            return cps.ToArray();
        }
    }
}