# Changelog

## [1.1.0] - 2026-10-20

- 追加: **Simple Rope Fence（杭とロープの柵）**。メニュー `you5248/Simple Rope Fence/新規作成（シーンビューの中心に）` で、
  穴あきの木の杭にしめ縄を通した柵を作る。杭は普通の子オブジェクトで、Scene ビューで移動・回転するとロープが 0.3 秒後に付いてくる。
  設定は杭の本数・全体の長さ・たわみ・ランダム性とシード・傾きの幅・ロープの太さ・杭の高さ。
  - 生成したロープのメッシュは `Assets/you5248/SimpleRopeFence/Generated/` に保存する（フォルダは自動作成）
  - 杭・しめ縄・房のメッシュと材質（dhk Standard、512 のテクスチャ）を同梱
  - 設定のコンポーネントは EditorOnly タグの子に置き、EditorOnly の外に残っていると VRChat のビルドを止める

## [1.0.2] - 2026-09-02

- 1.0.1のリリースzipに誤って旧版zipが同梱されていた問題を修正（1.0.1は欠番扱い）

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
