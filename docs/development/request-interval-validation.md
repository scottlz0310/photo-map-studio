# タイル要求の最小間隔の検証

2026-10-23、Windows x64 / .NET SDK 10.0.402で検証。#57の作業中に別の既存テストの失敗を見つけ、所有者の承認を得て別PRで対処した。

## 原因と修正

`ThrottledTileClientTests.最小間隔を空けて発行する` は、100ms間隔で3要求を送った実時間が200ms以上かを判定していた。HTTPを使わない同じ経路を30回計測すると、200ms未満が4回（最短194.7378ms）だった。StopwatchとUTC時計の値はほぼ一致し、時計同士の差で説明できなかった。

`ThrottledTileClient` と `RequestIntervalGate` はどちらも、タイマー通知後に期限到達を確認せず要求を通す実装だった。通知後も残時間が正なら待ち直す。1ms未満の残時間はミリ秒へ切り上げ、0ms待機の空回りを避ける。待機時間の設定、同時実行数、キャンセル、キャッシュ、HTTPの再試行回数は変更しない。

## 検証

- 実時刻を使わず、指定待機時間の半分で通知する `FakeTimeProvider` のラッパーを使用。旧コードではラスター／ベクターの100ms／1000msの全4ケースが、期限前に通過したことを示して失敗した。
- 修正後は100ms／101ms／1000msで、早い通知後も待機し、期限を満たせば通過する。101msは残時間50.5msを切り上げる場合を確認する。
- 残時間を待っている途中のキャンセルで例外が返り、その後の要求が通過することを確認。PR89のキャンセル回帰も対象に含めた。
- 修正後の同じ実時計計測30回は200ms未満0回、最短201.2952ms。これは補助計測であり、仮想時刻での再現テストを主要な回帰検証とする。
- CI相当のrestore・format検証・Release/x64 solution build（警告0／エラー0）、coverage付きCore532件／App239件が全成功（失敗・スキップ0）。
- 新規worktreeのformat読み込み警告は、参照先のCore Debug DLL未生成が原因。Core Debug build後の同じformat検証で警告なしを確認した。検査の無効化や設定変更は行っていない。
- 既存の他の実時計テストすべてを置き換える作業は含めない。通信断の実機試験とも区別する。

ローカル計測データはDropboxの `photo-map-studio/v0.3.0-chk/issue57/existing-minimum-interval-timing.json` と `minimum-interval-timing-after.json`。検証用CLIはTEMPの `pms-issue57-visual` に保持している（ProjectReferenceはこの修正worktree）。HTTP通信・実写真・配布版設定を使わない計測。
