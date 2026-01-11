namespace OsuStdToTaiko
{
    internal static class SplitDiagnostics
    {
        // ---- Split decision log (official-compatible, minimal) ----
        internal static void SplitDiagOfficial(
            int startTime,
            string curve,
            int beatmapVersion,
            int repeats,              // spanCount
            double pixelLength,       // .osu pixelLength (ExpectedDistance)
            double calculatedDistance,// SliderPath.CalculatedDistance
            double sliderPathDistance,// SliderPath.Distance (scaled to ExpectedDistance)
            double timingBL,          // BaseBeatLengthAt(startTime) (redline)
            double sliderVel,         // SvAt(startTime) (greenline multiplier)
            double bpmMultiplier,     // clamp(-sliderVelocityAsBeatLength)/100 or 1
            double beatLength0,       // timingBL * bpmMultiplier (speed-adjusted)
            double beatLength,        // (v8+ ? timingBL : beatLength0) for tick/rhs
            double sliderMultiplier,
            double sliderTickRate,
            double distScaled,        // sliderPathDistance * VELOCITY_MULTIPLIER * spans
            double sliderScoringPointDistance,
            double taikoVelocity,
            int taikoDuration,        // (int)(distScaled / taikoVelocity * beatLength0)
            double osuVelocity,       // taikoVelocity * (1000f / beatLength0)
            double tickSpacing,
            double lhs,               // distScaled / osuVelocity * 1000
            double rhs,               // 2 * beatLength
            bool shouldConvertToHits
        )
        {
            char ctype = (curve != null && curve.Length > 0) ? curve[0] : '?';
            double diff = lhs - rhs;

            Console.WriteLine(
                "[SplitUsed] t={0} type={1} v={2} spans={3} " +
                "px={4:F6} calc={5:F6} path={6:F6} " +
                "BL={7:F6} sv={8:F6} bpmMul={9:F6} BL0={10:F6} BLcmp={11:F6} " +
                "SM={12:F6} TR={13:F6} dist={14:F6} spd={15:F6} tv={16:F6} dur={17} " +
                "osuVel={18:F12} tick={19:F12} lhs={20:F12} rhs={21:F12} diff={22:+0.000000000000;-0.000000000000;0.000000000000} -> split={23}",
                startTime, ctype, beatmapVersion, repeats,
                pixelLength, calculatedDistance, sliderPathDistance,
                timingBL, sliderVel, bpmMultiplier, beatLength0, beatLength,
                sliderMultiplier, sliderTickRate, distScaled, sliderScoringPointDistance, taikoVelocity, taikoDuration,
                osuVelocity, tickSpacing, lhs, rhs, diff,
                shouldConvertToHits ? 1 : 0
            );
        }
    }
}
