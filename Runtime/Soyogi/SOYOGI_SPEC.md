# Soyogi シェーダー仕様書 v1.0（クリーンルーム実装用）

これは Unity Built-in RP 用の植生シェーダー「you5248/Soyogi」の機能仕様である。
**この仕様書のみから新規に実装すること。既存実装のコードを参照・検索・引用してはならない。**

## 0. 成果物

- 単一の `.shader` ファイル（ShaderLab + CG/HLSL）
- シェーダー名: `"you5248/SoyogiCleanroom"`（検証用の仮名。合格後に本名へ差し替える）
- 末尾: `Fallback "Diffuse"`、`CustomEditor "SoyogiGUI"`
- コメントは日本語可。可読性最優先で、機能ブロックごとに見出しコメントを付けること

## 1. 目的と設計方針

VRChat PCワールド（DX11・VR＝シングルパスステレオ・MSAA）向けのカットアウト植生シェーダー。
草・花・蔦・樹冠など105マテリアルで共用される。設計方針:

- **Unityサーフェスシェーダーとして実装**（`#pragma surface`）。ライトマップ/SH/フォグ/ステレオ/インスタンシングはUnity生成に任せる
- カスタムライティング関数（Lambert + 逆光透過）を使う。PBR(GGX)スペキュラは持たない
- 機能はすべて `shader_feature_local` でOFF時ゼロコスト化
- `#pragma target 3.0`。**補間レジスタ(TEXCOORD)は最悪ケースでも10本以内**（頂点カラーはCOLORセマンティクスを使えばTEXCOORD枠を消費しない）

## 2. プロパティ契約（名前・型は厳守。既存マテリアルがこの名前で値を保持している）

### 風
| プロパティ | 型 | 既定値 | 意味 |
|---|---|---|---|
| `_WindOff` | Toggle → keyword `_WIND_OFF` | 0 | ONで風の頂点変形を完全無効化 |
| `_LocalWindPower` | Range(0,5) | 3 | 幹スウェイ振幅 |
| `_LocalWindSpeed` | Float | 1 | 幹スウェイ角速度 |
| `_LocalWindBurstsPower` | Range(0,10) | 0.5 | 突風の寄与 |
| `_LocalWindBurstsSpeed` | Float | 5 | 突風場の流れ速度 |
| `_LocalWindBurstsScale` | Float | 10 | 突風場の空間スケール（値/100 を座標に乗算） |
| `_LocalMicroPower` | Range(0,1) | 0.1 | 葉さざめき振幅（m） |
| `_LocalMicroSpeed` | Float | 1 | さざめきドリフト速度 |
| `_LocalMicroFrequency` | Float | 3 | さざめき波の空間周波数 |
| `_WindMultiplier` | Float | 1 | 幹スウェイ最終倍率 |
| `_MicroWindMultiplier` | Float | 1 | さざめき最終倍率 |
| `_MicroWindYDamp` | Range(0,1) | 0 | 0=頂点法線方向に変位 / 1=水平(XZ)のみの向き非依存変位 |
| `_WindTrunkPosition` | Float | 0 | 幹マスクの指数（後述） |
| `_WindTrunkContrast` | Float | 10 | 幹マスクのコントラスト |
| `_WindMaskHeightMode` | Toggle → `_WINDMASK_HEIGHT` | 0 | ONで頂点カラーの代わりにローカルY高さランプをマスクに使う |
| `_WindMaskMin` | Float | 0 | 高さランプ下端(ローカルY) |
| `_WindMaskMax` | Float | 0.3 | 高さランプ上端(ローカルY) |
| `_WindDistanceFade` | Toggle → `_WINDFADE_ON` | 0 | ONでカメラ距離により風振幅をフェード |
| `_WindFadeStart` | Float | 8 | フェード開始距離(m) |
| `_WindFadeEnd` | Float | 16 | 完全静止距離(m) |

### メイン
| プロパティ | 型 | 既定値 | 意味 |
|---|---|---|---|
| `_CullMode` | Enum(Off=0,Front=1,Back=2) | 2 | `Cull [_CullMode]` |
| `_Cutoff` | Float | 0.5 | アルファクリップ閾値 |
| `_MainColor` | Color | (1,1,1,0) | ティント色。**αはティント適用強度**（後述） |
| `_Diffuse` | 2D (Tiling/Offset有効) | white | アルベド+アルファ |
| `_VertexOcclusionPower` | Range(0,1) | 0 | 頂点カラーAをAOとして使う際の指数 |
| `_NormalMapEnabled` | Toggle → `_NORMALMAP_ON` | 1 | ノーマルマップ有効 |
| `_Normal` | 2D (Tiling/Offset有効) | bump | ノーマルマップ |
| `_NormalPower` | Range(0,1) | 0.45 | ノーマル強度（UnpackScaleNormal相当） |

### グラデーション（keyword `_GRADIENT_ON`、Toggleプロパティ名 `_GradientEnabled`）
| プロパティ | 型 | 既定値 |
|---|---|---|
| `_GradientColor` | Color | (1,1,1,0) |
| `_GradientFalloff` | Range(0,2) | 2 |
| `_GradientPosition` | Range(0,1) | 0.5 |
| `_InvertGradient` | Toggle → `_INVERTGRADIENT_ON` | 0 |

### 色ムラ（keyword `_COLORVARIATION_ON`、Toggleプロパティ名 `_ColorVariationEnabled`）
| プロパティ | 型 | 既定値 |
|---|---|---|
| `_ColorVariation` | Color | (1,0,0,0) |
| `_ColorVariationPower` | Range(0,1) | 1 |
| `_ColorVariationNoise` | 2D | white |
| `_NoiseScale` | Float | 0.5 |

### 逆光透過（Translucency、常時有効）
| プロパティ | 型 | 既定値 |
|---|---|---|
| `_Translucency` | Range(0,50) | 1 |
| `_TransNormalDistortion` | Range(0,1) | 0.1 |
| `_TransScattering` | Range(1,50) | 2 |
| `_TransDirect` | Range(0,1) | 1 |
| `_TransAmbient` | Range(0,1) | 0.2 |
| `_TransShadow` | Range(0,1) | 0.9 |

### 側面隠し（keyword `_HIDESIDES_ON`、Toggleプロパティ名 `_HideSides`）
| プロパティ | 型 | 既定値 |
|---|---|---|
| `_HidePower` | Float | 2.5 |

### 追加キーワード（プロパティなし、マテリアル側でEnable/Disableされる）
- `_TINT_ON` … OFFのときティント/グラデ/色ムラの計算を丸ごとスキップしアルベド=Diffuseそのまま（`_MainColor.a==0`のマテリアル用fast path）
- `_COLORVAR_NOISETEX_ON` … OFFのときノイズテクスチャをフェッチせず定数1として扱う

## 3. 頂点カラー規約

- **R** = 葉マスク（さざめき振幅・逆光透過強度・ティント適用強度に乗算）
- **G** = 幹ウェイト（幹スウェイのマスク素材。値が大きいほど根本＝動かない）
- **B** = 未使用
- **A** = 焼き込みAO（`_VertexOcclusionPower`で適用）

頂点カラーを持たないメッシュではUnityが白(1,1,1,1)を供給するため、全頂点が全力で揺れて破綻する。
その救済が `_WINDMASK_HEIGHT`（§4）。

## 4. 風の頂点変形（object space、`_WIND_OFF`時は全て省略）

時刻 t = `_Time.y`、頂点のワールド位置 wp（変形前に計算）。

### 4.1 マスク
- 通常: `leafMask = 頂点カラーR`、`trunkRaw = 頂点カラーG`
- `_WINDMASK_HEIGHT` 時: `h = saturate((ローカルY - _WindMaskMin) / max(_WindMaskMax - _WindMaskMin, 1e-4))` として `leafMask = h`、幹マスクも h をそのまま使う（4.3の式を通さない）

### 4.2 突風場（安価な流走ノイズ）
0..1 の滑らかなスカラー場 `G(p)` を、無理数比の2方向サイン和などフルPerlinより大幅に安い方法で実装する（厳密な波形は問わないが、滑らかで流れて見えること）。

`gust = G( (wp.xz + t * _LocalWindBurstsSpeed) * _LocalWindBurstsScale / 100 )`

### 4.3 幹スウェイ（BaseWind）
- 幹マスク: `m = saturate( pow(abs(1 - trunkRaw), _WindTrunkPosition) * (0.5 + 0.5 * _WindTrunkContrast) )`
  - **注意**: `_WindTrunkPosition == 0` のとき pow を評価せず 1 とする（pow(0,0)未定義動作の回避、100/105マテがこの値）
- 振幅: `amp = _LocalWindPower * gust * _LocalWindBurstsPower * m`
- 変位: `base = float3( sin(t*_LocalWindSpeed) * amp, 0, cos(t*_LocalWindSpeed) * amp * 0.5 ) * _WindMultiplier`

### 4.4 葉さざめき（MicroWind）
- ドリフト: `d = G( wp.xz + t * _LocalMicroSpeed )`（スカラー）
- 波: `w = sin( _LocalMicroFrequency * (wp + d) )`（float3、per成分）
- 方向: `dir = lerp( w * 頂点法線, float3(w.x, 0, w.z), _MicroWindYDamp )`
- 変位: `micro = dir * _LocalMicroPower * leafMask * _MicroWindMultiplier`

### 4.5 距離フェード（`_WINDFADE_ON`）
`fade = saturate( (_WindFadeEnd - distance(wp, _WorldSpaceCameraPos)) / max(_WindFadeEnd - _WindFadeStart, 1e-3) )`
最終変位 `(base + micro) * fade` を頂点座標に加算。OFF時は fade=1。

### 4.6 その他
- 変形後 `v.vertex.w = 1` を保証すること
- ShadowCasterパスでも**同一の風変形**を適用すること（影と本体がズレないため）

## 5. フラグメント（surf）

処理順を厳守（早期クリップが目的）:

1. `_Diffuse` をサンプル → `alpha = tex.a`
2. **`_HIDESIDES_ON` でない場合、ここで `clip(alpha - _Cutoff)`**（以降の計算を透明ピクセルでスキップ）
3. `_NORMALMAP_ON` 時のみ `_Normal` をサンプルし `o.Normal` に設定（強度は `_NormalPower`）
4. アルベド:
   - `_TINT_ON` でない → `albedo = diffuse.rgb`
   - `_TINT_ON`:
     a. ティント色 `tint`:
        - `_GRADIENT_ON`: ワールド法線（ノーマルマップ適用前の頂点由来TBNのN軸）のYを `ny` とし、`_INVERTGRADIENT_ON` なら `ny = 1-ny`。
          `bias = -2 + _GradientPosition * 3`（＝[0,1]→[-2,1]のリマップ）
          `g = saturate( (ny + bias) / max(_GradientFalloff, 1e-4) )`
          `tint = lerp(_MainColor, _GradientColor, g)`
        - OFF: `tint = _MainColor`
     b. `_COLORVARIATION_ON` 時、ColorDodgeブレンド:
        `dodge = saturate( lerp(_ColorVariation, _ColorVariation / max(1 - tint, 1e-5), _ColorVariationPower) )`
        ノイズ: `_COLORVAR_NOISETEX_ON` 時 `n = abs(tex2D(_ColorVariationNoise, wp.xz * _NoiseScale/100))` の **3乗**（pow不使用で乗算展開）。OFF時 `n = 1`
        `tint2 = lerp(tint, dodge, _ColorVariationPower * n)`、OFF時 `tint2 = tint`
     c. `albedo = lerp(diffuse.rgb, tint2.rgb * diffuse.rgb, _MainColor.a * 頂点カラーR)`
5. `o.Occlusion`: `_VertexOcclusionPower == 0` なら 1、それ以外 `pow(abs(頂点カラーA), _VertexOcclusionPower)`
6. `o.Translucency = 頂点カラーR`（float3にブロードキャスト）
7. `_HIDESIDES_ON` 時のみ、最後に側面隠しクリップ（§7）

## 6. カスタムライティング

SurfaceOutput拡張（Albedo, Normal, Emission, Occlusion, Alpha, Translucency）。

```
NdotL = max(0, N·L)
lightAtten = DIRECTIONALの場合 lerp(_LightColor0.rgb, gi.light.color, _TransShadow)、それ以外 gi.light.color
ldir = gi.light.dir + N * _TransNormalDistortion
x = saturate( dot(viewDir, -ldir) )
scatter = xの_TransScattering乗（ただし _TransScattering==1 なら x、==2 なら x*x として pow を回避）
trans = lightAtten * ( scatter * _TransDirect + gi.indirect.diffuse * _TransAmbient ) * s.Translucency
color = albedo * ( gi.light.color * NdotL + gi.indirect.diffuse * occlusion )
      + albedo * trans * _Translucency
alpha = 1（不透明）
```

GI関数は `UnityGlobalIllumination(data, occlusion, normal)` をそのまま使う（反射プローブ不要）。

## 7. 側面隠し（HideSides、`_HIDESIDES_ON`）

板ポリを横から見たとき（面がエッジオンのとき）にスクリーンディザで消す機能。

- 幾何法線: `gn = normalize( cross( ddy(worldPos), ddx(worldPos) ) )`
- `f = |dot(視線方向, gn)|`（視線方向=カメラへ向かう正規化ベクトル）
- `edge = saturate( (1 - (1-f)*2) * alpha * _HidePower )` … 正面=残す/側面=消す係数
- `target = lerp(alpha, edge, 頂点カラーR)`（葉のみ適用、幹は素通し）
- スクリーン座標（ピクセル単位）から 8×8 Bayer ディザ値 d(0..1) を求め、`clip( step(d, target) - _Cutoff )`
- スクリーンUVは補間した `ComputeScreenPos` 由来（**この補間レジスタは `_HIDESIDES_ON` バリアントのみに存在させること**）
- ディザのZ成分・ニアクリップ距離には依存させない（VRChatの動的ニアクリップでちらつくため）

## 8. パスと変種の構成

- サーフェスシェーダー本体: `keepalpha fullforwardshadows exclude_path:deferred dithercrossfade vertex:(風関数)`、`multi_compile_instancing`＋`instancing_options assumeuniformscaling`、`multi_compile _ LOD_FADE_CROSSFADE`
- **shader_feature宣言はサーフェスCGPROGRAM内に置く**（CGINCLUDEに置くと専用ShadowCasterへ全機能のバリアントが波及するため）
- **専用ShadowCasterパス**（手書き）: 風変形＋`_Diffuse`のアルファ（Tiling/Offset適用）で `clip(a - _Cutoff)` するだけの軽量実装。ライティング計算なし。宣言する機能キーワードは風系3つ（`_WIND_OFF` `_WINDMASK_HEIGHT` `_WINDFADE_ON`）のみ。`multi_compile_shadowcaster`・インスタンシング対応・`V2F_SHADOW_CASTER`系マクロを正しく使うこと
- RenderType=`TransparentCutout`、Queue=`Geometry`、`Cull [_CullMode]`

## 9. 受入基準（実装後に発注側が検証する）

1. Unity 2022.3 / DX11 でコンパイルエラー・警告（未初期化等）なし
2. 補間レジスタ超過なし（`_HIDESIDES_ON`+全機能ONの最悪バリアント含む）
3. 既存マテリアルをこのシェーダーに差し替えたとき、プロパティ値・キーワードが**無変換で**引き継がれる（名前契約の遵守）
4. 風OFF状態の静止画で、現行実装とのピクセル差分が実質ゼロ（アンチエイリアス誤差程度）— 機能キーワードの全組み合わせ代表8ケースで確認
5. 風ONの挙動が仕様§4の通り（幹=水平楕円スウェイ、葉=場ベースのさざめき、マスク・フェードが効く）
6. ShadowCasterが風変形に追従し、影と本体がズレない

## 10. 実装上の注意

- サーフェスシェーダーのInput構造体で頂点カラーを受ける場合は `: COLOR` セマンティクスを使う（TEXCOORD補間枠を消費しない）
- `UNITY_INITIALIZE_OUTPUT(Input, o)` を頂点関数冒頭で呼ぶ
- uniform分岐（`_WindTrunkPosition == 0` 等）は動的分岐でよい（DX11では実質フリー）
- 命名は読みやすい英語でよい（既存実装の内部変数名を模倣する必要はない。むしろ独自に付けること）
