# Pickup Kit (you5248)

VRChat ワールドのピックアップ（持てるもの）まわりをまとめて設定するツール群。
VRChat Worlds SDK 3.10.x / UdonSharp 同梱版で動作確認を想定。

---

## 要件定義（このツールが満たすこと）

ユーザー要望をもとに確定した仕様。

### 1. ピックアップ一括設定（エディタ）
選択した複数オブジェクトに、選んだ設定でピックアップをまとめて付ける。

- **動きモード**（4種・排他）
  | モード | Rigidbody | 挙動 |
  |---|---|---|
  | 無重力フロート | useGravity=off / 低Drag | 離すとその場に漂い、慣性でゆっくり流れる |
  | 通常の重力あり | useGravity=on | 離すと落下・転がる普通のピックアップ |
  | 置いた場所に固定 | isKinematic=true | 掴んで動かせるが離すとその場に固定（落ちない） |
  | 掴むと物理ON/離すと固定 | 待機kinematic→掴むと物理 | `PickupHybridFreeze` が掴み/離しで切替 |
- **位置同期トグル**（全モード共通）: ON で `VRCObjectSync` を追加。OFF なら非同期。
- **N分後に原点へ戻すトグル**: ON で `PickupReturnTarget` を追加し、戻りマネージャに登録。

### 2. N分後に元の位置へ戻る（ランタイム・軽量・自己完結）
- タイミング: **ドロップ後N分**（持っている間・原点付近ではカウントしない／原点を離れて放置されてから計測）。
- 同期モデル: **自動で両対応**。
  - 同期ピックアップ（`VRCObjectSync` あり）→ 必要ならオーナーを取得して `Respawn()` し全員に反映。
  - 非同期 → ローカルで原点へ戻す（速度も消す）。
- 軽量化: 各 `PickupReturnTarget` が `Update` ではなく `SendCustomEventDelayedSeconds` で
  約2秒間隔の自前 tick を回す自己完結型。判定は OnDrop 非依存の「位置ベース」。
- **マネージャ不要**: 以前あった中央 `PickupReturnManager`（targets[] 参照配列）は、エディタからの
  参照シリアライズが壊れやすく "永遠に戻らない" 原因になり得たため廃止。`pickup`/`objectSync` も
  実行時 `GetComponent` で取得するので、確実にシリアライズされる `minutes`（float）だけ合えば動く。
- 原因切り分け用に `debugLog` トグルあり（計測開始・経過秒・戻り実行を Console 出力）。

### 3. QvPen 召喚ギミック（ランタイム・VR）
- VR で**右スティック上を一定秒数（既定0.6s）押し続ける**と、空いているペンを右手へ呼び寄せる。
- 呼ぶのは**誰にも持たれていない**ペンだけ（`VRC_Pickup.IsHeld` で除外）。
- **原点付近にあるペンを最優先**（散らかったペンより未使用ペンを呼ぶ）。
- QvPen 専用型に依存せず `VRC_Pickup` 参照のみ。QvPen でも自作ピックアップペンでも動く。

---

## ファイル構成

```
PickupKit/
  Editor/
    PickupSetupWindow.cs     # 一括設定ウィンドウ（メニュー: you5248/Pickup Kit）
  Scripts/
    PickupReturnTarget.cs    # 各ピックアップの原点記憶＋戻り（自己完結・自前tick）
    PickupReturnManager.cs   # 【任意・非必須】旧・中央巡回の名残。無くても動く（削除可）
    PickupHybridFreeze.cs    # 「掴むと物理ON/離すと固定」モード用
    QvPenSummoner.cs         # 右スティック上長押しでペン召喚
  README.md
```

---

## 使い方

### ピックアップ一括設定
1. メニュー **you5248 > Pickup Kit** を開く。
2. 対象を選ぶ。手で選んでもよいし、**「選択ヘルパー」**の
   **「シーンの全Pickupを選択（QvPen除く）」**で、非アクティブ含むシーン上の
   全 VRC Pickup を一括選択できる（アイテムが大量にあるとき用）。
   - QvPen 判定はコンポーネントの namespace（`QvPen…`）＋名前フォールバックで行うため、
     QvPen をアンインストールしても壊れない。
3. 動きモード・位置同期・自動戻りを設定し、**「選択 N 個に適用」**。
4. すべて1回の Undo（Ctrl+Z）でまとめて元に戻せる。

> ⚠️ Collider が無いオブジェクトは掴めません。Collider の種類は用途で変わるため、
> このツールは自動追加せず警告のみ出します。手動で追加してください。

### QvPen 召喚
1. 同ウィンドウ下部の **「QvPen 召喚ギミック」** を開く。
2. ペンを選ぶ。**「シーンの QvPen ペンを選択」**ボタンで QvPen のペンだけ一括選択できる
   （または手動で Hierarchy 選択）。
3. **「選択中のペンで QvPenSummoner を作成」** を押すと、`QvPenSummoner` を持つ
   GameObject がシーンに作られ、`pens[]` に自動登録される。
4. Inspector で長押し秒数・しきい値・手元オフセットを調整可能。

---

## 既知の制約・注意（正直なメモ）

- **手に強制装着はできない**: Udon にピックアップを手へ握らせる API は無い。召喚は
  「ペンを右手位置へ瞬間移動して掴める状態にする」まで。掴むのはプレイヤー操作。
- **右スティック上 = `InputLookVertical`（VRの右スティック縦・上が正）**。機種/設定で
  上下が逆なら `QvPenSummoner.handleUpIsPositive` を OFF に。デスクトップでは視点操作と
  競合するため無効化済み。
- **非同期モードの戻り/位置はクライアントごと**。全員で同じ見え方にしたいなら位置同期を ON に。
- **モードを変えて再適用**しても、古い `PickupHybridFreeze` や不要な `VRCObjectSync` は
  自動削除しない（U# コンポーネント削除での UdonBehaviour 孤立を避けるため）。
  競合時は警告ログを出すので、不要分は手動で削除する。
- **VR 実機での挙動（右スティック長押し・手元位置）は未検証**。デスクトップ＋ClientSim で
  ロジック確認後、実機で詰める想定。

---

## 戻らないときの確認（トラブルシュート）

1. **Udon が動く環境で試しているか**。UdonSharp は素の Play モードでは動かない。
   ClientSim（SDK の Play モード）か、アップロードしたワールドで確認すること。
2. 対象オブジェクトの `PickupReturnTarget` で **`debugLog` を ON** にして Play / アップロード。
   Console（またはワールドログ）に「計測開始 → 経過 N/300s → Respawn()/原点へ」が出れば動作中。
   - 何も出ない → そのオブジェクトに `PickupReturnTarget` が付いていない or Udon が動いていない。
   - 「計測開始」が出ない → まだ原点付近（`homeThreshold` 内）か、掴みっぱなし判定。
3. 旧バージョンで作った `PickupReturnManager` がシーンに残っていても無害。新コードでは各 Target が
   自前で動くため、**再適用しなくても再コンパイル後に動き出す**（minutes は維持される）。不要なら
   `PickupReturnManager` GameObject は削除してよい。
