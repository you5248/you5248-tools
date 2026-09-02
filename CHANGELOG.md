# Changelog

## [1.0.0] - 2026-09-02

- 初回リリース。`Assets/you5248/` の手動コピー運用からVPMパッケージへ移行
- 収録: LODGenerator / LOD_BatchTool / VRCShadowLODGenerator / MeshArrayCombiner / MeshAtlasTool /
  MeshMergeAndReplaceWindow / MeshSimilarityFinder / ObjectPainter / SceneToPrefabBatcher /
  TerrainGenerator / QuestPort / BakeryConverter（要Bakery） /
  AutoOpenDoor / PickupKit / LoopSystem / DebugAreaTeleport /
  FloatingPetals・Soyogi・Quest向け各シェーダー
- 移行に伴う変更:
  - LeafCover系シェーダーの include を `Packages/com.you5248.tools/...` パスへ変更
  - BakeryConverter を `BAKERY_INCLUDED` シンボルでガード（Bakery未導入環境でのコンパイルエラー回避）
  - SceneToPrefabBatcher の設定アセット保存先フォルダを親から再帰作成するよう修正
- 除外（Assets版にあってパッケージに無いもの）:
  - dhkStandard / dhkStandardQuest シェーダー → `com.you5248.dhk-shaders` パッケージへ移管（依存宣言済み）
  - CreatorBoard / Fake_LTCGI（ConcreteHallアセット依存） / SetupHexBokehFlare / 各Generated / Prefabsライブラリ /
    White House専用デバッグスクリプト（DebugAirDuctFingerprints等）
