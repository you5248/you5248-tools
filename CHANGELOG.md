# Changelog

## [1.0.1] - 2026-09-02

- ドキュメントを第三者向けに全面改訂（導入手順・動作環境・実際のメニューパス・各ツールの使い方）
- LICENSE（MIT）を追加し、`package.json` に `license` を明記
- `vpmDependencies` に `com.vrchat.worlds` を追加。非標準フィールド `vrchatVersion` を削除
- 開発時のバックアップファイル（`*.bak` 等）・テスト用プリセット・空フォルダのmetaをリポジトリから除去

## [1.0.0] - 2026-09-02

- 初回公開（VPM）
- エディタ拡張: LOD Generator / LOD Batch Tool / Shadow LOD Generator / Mesh Array Combiner /
  Mesh Atlas Merge / Mesh Merge And Replace / Mesh Similarity Finder / Object Painter /
  Scene → Prefab Batcher / Terrain Generator / Pickup Kit / Quest Port /
  Bakery Converter（要Bakery＋`BAKERY_INCLUDED`）
- Udonギミック: AutoOpenDoor / Pickup Kit / Loop System / Debug Area Teleport
- シェーダー: LeafCover系 / FloatingPetals系 / SoyogiCleanroom / MeshArrayPBR系 / モバイル向け軽量版
- 依存: VRChat Worlds SDK、`com.you5248.dhk-shaders` ^1.4.1
