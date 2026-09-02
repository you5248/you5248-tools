# you5248 Tools

VRChatワールド制作用の自作ツール群（VPMパッケージ）。

## 導入

VCCにコミュニティリポジトリ `https://you5248.github.io/vpm-listing/index.json` を登録し、
プロジェクトに **you5248 Tools** を追加する。

`Assets/you5248/` に旧来の手動コピー版がある既存プロジェクトでは、
パッケージ導入後に旧フォルダを削除すること（GUIDは維持しているため、シーン・マテリアルの参照は保たれる。
ただし `Generated/` 等の生成物・`Prefabs/` は本パッケージに含まれないので、使っている場合は残すこと）。

## 収録ツール

### エディタツール（メニュー: you5248 / Tools）
| ツール | 概要 |
|---|---|
| LODGenerator | メッシュ簡略化によるLOD自動生成（ビルボードベイク・プリセット対応） |
| LOD_BatchTool | LOD設定の一括適用 |
| VRCShadowLODGenerator | 影専用LODの生成 |
| MeshArrayCombiner | 同一マテリアル群のメッシュ配列結合（専用PBRシェーダー同梱） |
| MeshAtlasTool | メッシュ統合＋テクスチャアトラス化 |
| MeshMergeAndReplaceWindow | メッシュ結合と参照差し替え |
| MeshSimilarityFinder | 類似メッシュ検索（共有化の下調べ） |
| ObjectPainter | オブジェクト散布ペインタ |
| SceneToPrefabBatcher | シーン上のGameObjectを自己完結プレハブへ一括変換 |
| TerrainGenerator | 地形生成 |
| QuestPort | Quest向けビルド差し替え（マテリアル生成・メッシュ削減・ポリシー監査） |
| BakeryConverter | ライトのBakery変換（**Bakery導入環境のみ**。`BAKERY_INCLUDED` シンボルで有効化） |

### Udonギミック（Runtime）
| ギミック | 概要 |
|---|---|
| AutoOpenDoor | 接近で開く自動ドア |
| PickupKit | ピックアップの帰還管理・ハイブリッドフリーズ・ペン呼び寄せ（QvPen非依存） |
| LoopSystem | シームレスループテレポート |
| DebugAreaTeleport | デバッグ用エリアテレポート |

### シェーダー
- FloatingPetals / LeafCover 系（MonoSH対応の葉・花弁）
- Soyogi（そよぎ）
- MeshArrayCombiner用 PBR 各種
- Quest向け軽量シェーダー（SimpleWaterQuest / Glass系 / LeafCover系）

## 依存関係
- VRChat Worlds SDK（VCCが解決）
- [dhk Shaders](https://github.com/you5248/dhk-shaders)（`com.you5248.dhk-shaders`・vpmDependenciesで自動導入。QuestPortのマテリアル生成が使用）

## サードパーティ
- `Editor/LODGenerator/ThirdParty/UnityMeshSimplifier` — [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier)
  (MIT License, Copyright (c) 2017-2020 Mattias Edlund)。ライセンス全文は同フォルダの LICENSE.md を参照。
