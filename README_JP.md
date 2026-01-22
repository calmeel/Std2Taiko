# Std2Taiko
<p align="left"><img src="Images/Logo.png"></p>  
<p align="left"><img src="Images/UserInterface.png"></p>
<p align="left"><img src="Images/screenshot.jpg"></p>

osu!standard の taiko コンバート譜面を osu!taiko フォーマットに変換するツールです。  
オプションで等速（constant-speed）譜面の出力にも対応。

[English README](README.md)


## 📥 ダウンロード
[最新の Windows 実行ファイルはこちら](https://github.com/calmeel/Std2Taiko/releases/latest)

## 🧾 特徴 / Features

 - osu!standard → osu!taiko の変換  
 - constant-speed 譜面の出力に対応（追加オプション）  

## ⭐ このツールの利点

公式の osu!standard → taiko の convert は便利ですが、実際の練習用途ではいくつかの制約があります:

1. osu!stable では任意の位置から練習できない  
   → osu!stable では convert 譜面を Editor で開いて任意のタイミングから再生することができず、練習効率が悪いです。  
   → 本ツールで生成した譜面は Editor 上で任意位置から再生・練習できます。

2. SV 変化が激しくリズム把握が難しい  
   → convert 譜面は slider や BPM 変化に由来する速度変化が多く、リズムの視認性が低下します。  
   → 本ツールは Editor 上で snap 間隔が確認可能であるため、リズムが視覚的に把握できます。

3. constant speed でのプレイは osu!lazer でしか対応していない  
   → osu!lazer では convert 譜面を constant speed mod でスクロールさせることができますが、osu!stable では非対応です。  
   → 本ツールは constant speed での出力に対応しており、osu!stable でも constant speed でプレイ可能です。

## 📘 GUI での使い方
追加の .NET ランタイム等は不要です。  
使い方：

1. `.osu` ファイルをアプリにドラッグ＆ドロップ
2. 変換設定を選択
3. **変換開始** を押す
4. 変換後の `.osu` ファイルが入力ファイルと同じフォルダに生成されます


## 📗 CLI での使い方
```bash
Std2Taiko.exe input.osu output.osu --mode stable
```

## 🧩 対応環境
Std2Taiko は self-contained な 64bit Windows 実行ファイルとして配布されています。

 - インストール不要
 - .NET ランタイム不要
 - 任意のフォルダから実行可能（portable）
 - 対応 OS: Windows 10 / 11 (64bit)


## 🔧 仕組み（概要）
Std2Taiko は osu!lazer の一部ソースコードを利用し、公式 taiko ruleset の挙動を再現する形で hitobject の変換を行います。

内部パイプライン（簡略版）：

1. `.osu` → legacy decoder
2. legacy encoder で `HitObjects` と metadata を正規化
3. taiko conversion logic を適用
   - スライダーの split 判定
   - ヒットオブジェクトの配置
   - nested object（drumroll / swell 等）の生成
4. `[TimingPoints]` の正規化
   - `NaN`, `-1`, 極端な指数値などの排除
   - SV 0.1 ~ 10 へクランプ
   - Aspire譜面で見られる異常値への対処
5. 最終 `.osu` を出力

「スライダーの split 判定」と「ヒットオブジェクトの配置」の挙動は osu!lazer の `TaikoBeatmapConverter` と同じですが、
`[TimingPoints]` の扱いについては stable 互換を重視した処理により stable 側に近い結果を得る場合があります。


## ⚠️ 制限事項 / Limitations
- 基本的には stable に近い挙動を目指していますが、全ての譜面での完全互換は保証されません。
- Std2Taiko は osu!lazer ベースの decode パイプラインに依存しているため、lazer 側で変換できない譜面は本ツールでも変換に失敗する可能性があります。
- timing sanitation と Aspire rescue の挙動は lazer と異なるため、場合によって stable 側に近い結果になります。
- 未実装の項目については `TODO.md` を参照してください。


## 📣 バグ報告 / Reporting Issues
不具合や疑わしい出力を見つけた場合は、開発者への連絡または GitHub の issue までお願いします。

報告の際は可能であれば以下をご提供ください：

- 譜面リンク または `.osu` ファイル
- 出力モード（例: stable / lazer）
- 出力オプション（例: constant-speed / constant-speed-adjusted）
- 期待される挙動（分かる場合）

再現性の向上に役立ち、原因の特定が容易になります。


## 📝 補足事項
- 管理者権限は不要
- osu!本体のインストールは不要
- osu! クライアントのメモリや anti-cheat には一切アクセスしません
- 標準的な `.osu` ファイルを出力します


## 📚 ライセンス
本ツールは MIT License で公開しています。詳細は `LICENSE` を参照してください。
