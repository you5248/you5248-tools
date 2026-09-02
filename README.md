# you5248 Tools

VRChatワールド制作用のエディタ拡張・Udonギミック・シェーダーを集めたVPMパッケージ。

## 動作環境

- Unity **2022.3.22f1**（VRChat公式指定バージョン）
- **Built-in Render Pipeline（BiRP）専用**。URP / HDRP は対象外
- VRChat **Worlds** SDK（UdonSharp同梱の現行版）。ワールドプロジェクト専用でアバター用途は想定しない
- 一部シェーダーはBakeryのMonoSHライトマップに対応（Bakery自体は必須ではない）

## 導入

1. VCC（VRChat Creator Companion）の Settings → Packages → Add Repository に
   `https://you5248.github.io/vpm-listing/index.json` を追加する。
2. ワールドプロジェクトに **you5248 Tools** を追加する。
   依存パッケージの [dhk Shaders](https://github.com/you5248/dhk-shaders) も自動で導入される。

## 収録ツール

### エディタ拡張（Unity上部メニュー: `you5248`）

| ツール | メニュー | 概要 |
|---|---|---|
| LOD Generator | `you5248/LOD Generator` | メッシュ簡略化によるLOD自動生成（ビルボードベイク・プリセット対応） |
| LOD Batch Tool | `you5248/LOD Batch Tool` | LOD設定の一括適用 |
| Shadow LOD Generator | `you5248/Shadow LOD Generator (Built-in)` | 影描画専用の軽量LOD生成 |
| Mesh Array Combiner | `you5248/Mesh Array Combiner` | 同一マテリアル群のメッシュ配列結合（専用PBRシェーダー同梱） |
| Mesh Atlas Merge | `you5248/Mesh Atlas Merge` | メッシュ統合＋テクスチャアトラス化 |
| Mesh Merge And Replace | `you5248/Mesh Merge And Replace` | メッシュ結合と参照差し替え |
| Mesh Similarity Finder | `you5248/Mesh Similarity Finder` | 類似メッシュ検索（メッシュ共有化の下調べ） |
| Object Painter | `you5248/Object Painter` | オブジェクト散布ペインタ |
| Scene → Prefab Batcher | `you5248/Scene -> Prefab Batcher` | シーン上のGameObjectを自己完結プレハブへ一括変換 |
| Terrain Generator | `you5248/Terrain Generator` | 地形メッシュ生成 |
| Pickup Kit | `you5248/Pickup Kit` | ピックアップの一括設定。詳細は [Runtime/PickupKit/README.md](Runtime/PickupKit/README.md) |
| Quest Port | `you5248/Quest Port Config` ほか | モバイル対応ビルド支援（下記の注意を参照） |
| Bakery Converter | `you5248/Bakery Converter` | シーンのライトをBakery用に変換（下記の注意を参照） |

このほか同じ `you5248` メニューに Recovery（Mesh Array Combiner の復旧用）や Quest Port の個別コマンドがある。
各ツールの生成物は既定で `Assets/you5248/` 配下に出力される（フォルダは自動作成される）。

> **Quest Port の注意**: モバイルビルド時のシーン差し替え・オブジェクト除去は
> `Quest Port Config` の設定内容に従って**ビルド時に自動適用**される。導入しただけでは何もしない。
> 一部コマンド（`Generate Quest Materials` / `Generate Reduced Book Meshes`）には作者ワールド固有の前提が
> 残っており、1.0系では第三者の利用を想定していない。Config / Audit / Texture Overrides 系は汎用に使える。

> **Bakery Converter の注意**: Bakery導入環境専用。Scripting Define Symbols に `BAKERY_INCLUDED` が
> 定義されるまでメニューに現れない（定義されていない場合は Player Settings から手動で追加する）。

### Udonギミック（Runtime）

| ギミック | 使い方 | 概要 |
|---|---|---|
| AutoOpenDoor | ドアにコンポーネント追加＋IsTriggerのCollider | プレイヤー接近で開く自動ドア |
| Pickup Kit | メニュー `you5248/Pickup Kit` から一括設定 | ピックアップの帰還管理・位置フリーズ・ペン呼び寄せ（QvPen非依存） |
| Loop System | プレハブ `Runtime/LoopSystem/Prefabs/[_]Loop_System.prefab` を配置 | シームレスループテレポート |
| Debug Area Teleport | オブジェクトにコンポーネント追加（Interactでワープ） | 制作中のエリア移動用デバッグギミック |

### シェーダー

マテリアルのShaderドロップダウンに表示される実際の名前:

- `you5248/LeafCover Standard` / `you5248/LeafCover StandardCutout` — MonoSH対応の葉・植生
- `you5248/FloatingPetals` / `you5248/FloatingPetalsMeniscus` — 浮遊する花弁
- `you5248/SoyogiCleanroom` — 植生の揺れ
- `you5248/MeshArrayPBR` 系 — Mesh Array Combiner の出力用
- `you5248/Quest/...` / `you5248/SimpleWaterQuest` — モバイル向け軽量版

## 依存関係

- `com.vrchat.worlds` — VRChat Worlds SDK（VCCが解決する）
- `com.you5248.dhk-shaders` — [dhk Shaders](https://github.com/you5248/dhk-shaders)（自動導入。Quest Portのマテリアル生成が使用）

## ライセンス

MIT License。詳細はリポジトリルートの [LICENSE](LICENSE) を参照。

## 同梱サードパーティ

- `Editor/LODGenerator/ThirdParty/UnityMeshSimplifier` — [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier)
  (MIT License, Copyright (c) 2017-2021 Mattias Edlund)。ライセンス全文は同フォルダの LICENSE.md を参照。

---

> **旧手動コピー版（`Assets/you5248/`）からの移行**（作者・共同制作者向けの注記）:
> パッケージ導入後、`Assets/you5248` のうちパッケージ収録分と重複するファイルは削除してよい（GUIDを維持しているため参照は保たれる）。
> `Generated/` や `Prefabs/` などパッケージ非収録の生成物・素材は削除しないこと。
