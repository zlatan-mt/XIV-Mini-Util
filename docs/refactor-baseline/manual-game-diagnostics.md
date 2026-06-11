# Manual game diagnostics baseline

作成日: 2026-06-10 / 取得日: 2026-06-11

次の診断はゲーム内で実行し、出力をこのディレクトリに保存します。

## 取得対象

- `/xmu diag`
- `/xmutbgdiag`
- `/xmutbgcheck`

## 保存ファイル名

- `2026-06-11-xmu-diag.txt`
- `2026-06-11-xmutbgdiag.txt`
- `2026-06-11-xmutbgcheck.txt`

## 取得結果 (2026-06-11, v0.3.8 / Phase 0-7 リファクタ後)

- 13 コマンドすべて応答、バージョン表示 0.3.8
- `/xmutbgdiag`: 旧 `phase2M.*` / `phase2N.*` キー 236 行と新 `characterPlacement.*` / `delivery.*` alias 185 行が併記されていることを確認
- `/xmutbgcheck`: WARN (background=applied, post-login leak=none, character=要目視 — 既知の制限どおり)
- 設定タブ全カテゴリ表示・操作正常、旧診断 (Legacy experiments) UI の削除を確認
- 背景差し替え (n4f4) 動作確認済み

## 既知の観測 (2026-06-11)

ログイン画面で放置するとカメラがキャラクター寄りへ移動する。これはゲーム自身のロビーカメラカーブアニメーション (`SetCameraCurveMidPoint` / `CalculateCameraCurveLowAndHighPoint` はゲーム側から呼ばれ、プラグインは hook で値を調整するのみ。診断では callCount=689 / generatedCurveOverrideEffective=not-observed) によるもので、プラグインの per-frame カメラ書き込み (禁止事項) ではない。scene 差し替えによりカーブの軌道が既定シーンと異なるため動きが目立つ可能性がある。
