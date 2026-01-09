
## 未実施の項目
 - 一定の判断基準で TimingPoints の effects に 8/9 （omit first barline）を付与する（LegacyBeatmapEncoder と同じ処理）
 - [TimingPoints] の time（時刻）を拍境界/内部単位に量子化する（＋小数化）（LegacyBeatmapEncoder と同じ処理）

## Lazerでも未実施の項目
 - stable には「次オブジェクトが近い場合、ドラムロール末尾tickを非必須にする」挙動があり、lazer側は TODO として未実装

## 特定のマップ

### RiraN - Unshakable [Aspire]
https://osu.ppy.sh/beatmapsets/1237363#taiko/2571858

 - 異常な長さのスライダー（PixelLengthが9.8E+304 等）がdecoderで落ちる
 - 制御点座標が極端な範囲外のスライダーがdecoderで落ちる
 - 異常な量の制御点を持つスライダーがdecoderで落ちる：最後のスライダーが変換後消えてしまう
 - 80352ms 付近の異常ロングスライダー（NaN 赤線含む）が、constant speed＋SVA 適用時に等速基準（csNoClamp）へ揃えられる過程で時間長さが圧縮され、元の (taiko convert) より大幅に短くなってしまう
 - 負のPixelLengthのスライダーを、長さ0.1のスライダーとして救済したが実際の長さは不明（Autoが叩くか叩かないかの判定が異なるためおそらく違う）

### KNOWER - Time Traveler [Tijdmachine]
https://osu.ppy.sh/beatmapsets/1236988#taiko/2573493

 - decoder を通らない Slider が存在するため、constant-speed-adjusted モードでスライダーの時間的長さ異常に延長されてしまう


## 現在のところは問題ない点

### HitObjects
 - 負の startTime で始まるスライダーの変換：「負の小数→(int)で0方向へ切り捨て」のせいで、split判定が覆る可能性あり
 - 制御点が1点しかないスライダー（UnshakableやTime Traveler）：制御点を勝手に補っているが、現在は動作に問題なし
 - Slider の形状が変になってしまう問題：Legacy encoder で勝手に中心座標に変換されるので仕方がない 

### TimingPoints
 - ヒットオブジェクトのtime整数化処理により、「TimingPoints にピッタリ乗らなくなって前の赤/緑を参照してしまう」現象：公式でも起こり得るとのことなので現状は気にしないこととする
- constant-speed-adjusted モードで、TimingPoints の順番が乱れてしまう問題：整列処理を行っているが、もしかしたら不具合を起こす可能性あり
 - 拍数が負の場合、絶対値で救済している：絶対値で良いかは不明

### その他
 - 出力の file format は v14で固定している。入力と同じバージョンにしたほうが良いかどうかは不明（過去のバージョンは一度でも編集を加えると v14 に変化するため、ソロ楽これで問題ないと思う）
 - 各種マジックナンバー


## 実施するか検討中の項目
 - suffix を outputMode（lazer/stable/original）で変える分岐
 - 赤線ペア異常（同時刻多重、倍率不一致）を検出して警告するログ