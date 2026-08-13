# Changelog

このプロジェクトのすべての注目すべき変更をこのファイルに記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、[Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### Added

- リポジトリ初期化とプロジェクト構成（`PhotoMapStudio.slnx` / `App` / `Core` / `Core.Tests` / `App.Tests`）
- `Directory.Build.props` によるアナライザ設定の集約（`AnalysisMode=AllEnabledByDefault` / `TreatWarningsAsErrors`）
- CI（`dotnet format` / `build` / `test` / Codecov OIDC 連携）
- x64 / ARM64 の MSIX パッケージング検証を CI に追加
- lefthook による pre-commit フック（`dotnet format --verify-no-changes`）
- Renovate 設定（`github>scottlz0310/renovate-config` を extend）
- 移植仕様書 `docs/photo-map-studio-migration-spec.md`（[auto-map-generator](https://github.com/scottlz0310/auto-map-generator) から抽出）
