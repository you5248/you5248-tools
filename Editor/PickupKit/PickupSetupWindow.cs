using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using UdonSharpEditor;
using you5248.PickupKit;

namespace you5248.PickupKit.EditorTools
{
    // 選択オブジェクトに VRChat ピックアップ設定を一括適用するエディタウィンドウ。
    //
    // 設計方針（個人ツールの流儀）:
    //   - 正直な UI : 何が追加/変更されるかを適用前に明示。コライダー無しなど掴めない条件は警告する。
    //   - 予測可能  : 戻りマネージャはシーンに1つだけ自動生成し、勝手に量産しない。
    //   - 一貫した Undo : 追加コンポーネント・既存変更・マネージャ生成まで1回の Undo でまとめて戻せる。
    public class PickupSetupWindow : EditorWindow
    {
        private enum MoveMode
        {
            ZeroGravityFloat, // 無重力フロート
            Gravity,          // 通常の重力
            FixedInPlace,     // 置いた場所に固定（kinematic）
            HybridFreeze,     // 掴むと物理ON / 離すと固定
        }

        private MoveMode _mode = MoveMode.Gravity;
        private bool _syncPosition = true;
        private bool _enableReturn = false;
        private float _returnMinutes = 5f;

        private float _mass = 1f;
        private float _drag = 0f;
        private float _angularDrag = 0.05f;
        private float _floatDrag = 0.6f;

        private bool _excludeQvPen = true;

        private Vector2 _scroll;

        private const string UndoGroup = "Pickup Kit 一括設定";

        [MenuItem("you5248/Pickup Kit")]
        private static void Open()
        {
            var w = GetWindow<PickupSetupWindow>("Pickup Kit");
            w.minSize = new Vector2(360f, 460f);
            w.Show();
        }

        private void OnSelectionChange() => Repaint();

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("ピックアップ一括設定", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "選択したオブジェクトに Rigidbody / VRC Pickup（＋任意で位置同期・自動戻り）を一括適用します。",
                MessageType.None);

            EditorGUILayout.Space();

            // ---- 動きモード ----
            EditorGUILayout.LabelField("動きモード", EditorStyles.boldLabel);
            _mode = (MoveMode)EditorGUILayout.EnumPopup("モード", _mode);
            EditorGUILayout.LabelField(ModeDescription(_mode), EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                _mass = EditorGUILayout.FloatField("質量 (mass)", _mass);
                if (_mode == MoveMode.ZeroGravityFloat)
                {
                    _floatDrag = EditorGUILayout.FloatField("空気抵抗 (drag)", _floatDrag);
                }
                else if (_mode == MoveMode.Gravity || _mode == MoveMode.HybridFreeze)
                {
                    _drag = EditorGUILayout.FloatField("空気抵抗 (drag)", _drag);
                    _angularDrag = EditorGUILayout.FloatField("回転抵抗 (angularDrag)", _angularDrag);
                }
            }

            EditorGUILayout.Space();

            // ---- 位置同期 ----
            EditorGUILayout.LabelField("ネットワーク", EditorStyles.boldLabel);
            _syncPosition = EditorGUILayout.ToggleLeft(
                "位置を同期する（VRCObjectSync を追加）", _syncPosition);
            EditorGUILayout.LabelField(
                _syncPosition
                    ? "全員が同じ位置を見る。誰かが動かすと全員に反映。"
                    : "同期なし。ピックアップ自体は掴めるが、置いた位置は各自バラバラになり得る。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();

            // ---- 自動戻り ----
            EditorGUILayout.LabelField("自動で原点へ戻す", EditorStyles.boldLabel);
            _enableReturn = EditorGUILayout.ToggleLeft(
                "N分後に元の位置へ戻す（PickupReturnTarget を追加）", _enableReturn);
            using (new EditorGUI.DisabledScope(!_enableReturn))
            using (new EditorGUI.IndentLevelScope())
            {
                _returnMinutes = EditorGUILayout.FloatField("戻るまでの時間（分）", _returnMinutes);
                if (_returnMinutes < 0f) _returnMinutes = 0f;
                EditorGUILayout.LabelField(
                    "ドロップして原点から離れ、放置されてから計測。掴んでいる間/原点付近ではカウントしません。",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "各オブジェクトに自己完結型の PickupReturnTarget を付けます（マネージャ不要・約2秒間隔で軽量判定）。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space();

            // ---- 選択ヘルパー ----
            EditorGUILayout.LabelField("選択ヘルパー", EditorStyles.boldLabel);
            _excludeQvPen = EditorGUILayout.ToggleLeft("QvPen を除外する", _excludeQvPen);
            if (GUILayout.Button(
                    _excludeQvPen ? "シーンの全Pickupを選択（QvPen除く）" : "シーンの全Pickupを選択"))
            {
                SelectScenePickups(excludeQvPen: _excludeQvPen, qvPenOnly: false);
            }
            EditorGUILayout.LabelField(
                "非アクティブも含めてシーン上の VRC Pickup を一括選択します。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();

            // ---- 選択状況 & 警告（正直な UI） ----
            DrawSelectionSummary();

            EditorGUILayout.Space();

            var targets = GetSelectedSceneObjects();
            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button(
                        targets.Count > 0
                            ? "選択 " + targets.Count + " 個に適用"
                            : "オブジェクトを選択してください",
                        GUILayout.Height(32f)))
                {
                    Apply(targets);
                }
            }

            EditorGUILayout.Space();
            DrawQvPenSection();

            EditorGUILayout.EndScrollView();
        }

        private static string ModeDescription(MoveMode m)
        {
            switch (m)
            {
                case MoveMode.ZeroGravityFloat:
                    return "重力オフ・低Drag。掴んで離すとその場に漂い、慣性でゆっくり流れる。";
                case MoveMode.Gravity:
                    return "通常の物理。離すと落下・転がる普通のピックアップ。";
                case MoveMode.FixedInPlace:
                    return "isKinematic。掴んで動かせるが、離すとその場に固定（落ちない・動かない）。";
                case MoveMode.HybridFreeze:
                    return "掴んでいる間だけ物理ON、離した瞬間にその場で固定（PickupHybridFreeze を追加）。";
                default:
                    return string.Empty;
            }
        }

        private void DrawSelectionSummary()
        {
            var targets = GetSelectedSceneObjects();
            EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);
            if (targets.Count == 0)
            {
                EditorGUILayout.HelpBox("Hierarchy でシーン上のオブジェクトを選択してください。", MessageType.Info);
                return;
            }

            int noCollider = 0;
            int qvPenCount = 0;
            foreach (var go in targets)
            {
                if (go.GetComponent<Collider>() == null) noCollider++;
                var p = go.GetComponent<VRC_Pickup>();
                if (p != null && IsQvPenPickup(p)) qvPenCount++;
            }

            EditorGUILayout.LabelField("選択中: " + targets.Count + " 個");
            if (qvPenCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "うち " + qvPenCount + " 個が QvPen ペンです。Apply しても QvPen オブジェクトはスキップされます（元の設定を保護）。",
                    MessageType.Warning);
            }
            if (noCollider > 0)
            {
                EditorGUILayout.HelpBox(
                    "うち " + noCollider + " 個に Collider がありません。Collider が無いと掴めません。" +
                    "（種類を勝手に決めないため自動追加はしません。手動で追加してください）",
                    MessageType.Warning);
            }
        }

        private static List<GameObject> GetSelectedSceneObjects()
        {
            var list = new List<GameObject>();
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                // プロジェクト（プレハブアセット）ではなくシーン上のものだけ対象
                if (EditorUtility.IsPersistent(go)) continue;
                if (!list.Contains(go)) list.Add(go);
            }
            return list;
        }

        // シーン上の VRC Pickup を一括選択する。
        //   excludeQvPen: QvPen 関連のピックアップを除外（ユーザー要望の主目的）
        //   qvPenOnly   : 逆に QvPen のペンだけ選択（QvPenSummoner 作成用）
        private void SelectScenePickups(bool excludeQvPen, bool qvPenOnly)
        {
            var all = Object.FindObjectsOfType<VRC_Pickup>(true);
            var result = new List<GameObject>();
            foreach (var p in all)
            {
                if (p == null) continue;
                if (EditorUtility.IsPersistent(p)) continue; // プレハブアセット等は除外

                bool isQv = IsQvPenPickup(p);
                if (qvPenOnly && !isQv) continue;
                if (!qvPenOnly && excludeQvPen && isQv) continue;

                var go = p.gameObject;
                if (!result.Contains(go)) result.Add(go);
            }

            Selection.objects = result.ToArray();
            if (result.Count > 0) EditorGUIUtility.PingObject(result[0]);

            string label = qvPenOnly ? "QvPen のペン" : (excludeQvPen ? "Pickup（QvPen除く）" : "Pickup");
            Debug.Log("[Pickup Kit] " + label + " を " + result.Count + " 個選択しました。");
        }

        // ピックアップが QvPen 由来かどうか。
        // QvPen の型に直接依存しないため、自分＋親のコンポーネントの namespace が "QvPen" で
        // 始まるかで判定する。スクリプト欠落などで型が読めない場合は名前でフォールバック。
        private static bool IsQvPenPickup(VRC_Pickup pickup)
        {
            var comps = pickup.GetComponentsInParent<Component>(true);
            foreach (var c in comps)
            {
                if (c == null) continue;
                var ns = c.GetType().Namespace;
                if (!string.IsNullOrEmpty(ns) && ns.StartsWith("QvPen")) return true;
            }
            // フォールバック: 自分の名前 or ルート名に "qvpen" を含む
            if (pickup.gameObject.name.ToLowerInvariant().Contains("qvpen")) return true;
            var root = pickup.transform.root;
            if (root != null && root.name.ToLowerInvariant().Contains("qvpen")) return true;
            return false;
        }

        private void Apply(List<GameObject> targets)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoGroup);
            int group = Undo.GetCurrentGroup();

            int hybridConflicts = 0; // 非ハイブリッドモードなのに古いハイブリッド挙動が残っている数
            int syncLeftovers = 0;   // 同期OFFなのに既存の VRCObjectSync が残っている数
            int qvPenSkipped = 0;    // QvPen オブジェクトはスキップして元の設定を保護

            foreach (var go in targets)
            {
                // QvPen ペンは誤適用から保護する（Rigidbody 設定を上書きしない）
                var vrcPickup = go.GetComponent<VRC_Pickup>();
                if (vrcPickup != null && IsQvPenPickup(vrcPickup))
                {
                    qvPenSkipped++;
                    continue;
                }
                // --- Rigidbody ---
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
                else Undo.RecordObject(rb, UndoGroup);
                ConfigureRigidbody(rb);

                // --- VRC Pickup ---
                var pickup = go.GetComponent<VRCPickup>();
                if (pickup == null) pickup = Undo.AddComponent<VRCPickup>(go);
                else Undo.RecordObject(pickup, UndoGroup);
                pickup.pickupable = true;

                // --- 位置同期 ---
                VRCObjectSync sync = go.GetComponent<VRCObjectSync>();
                if (_syncPosition)
                {
                    if (sync == null) sync = Undo.AddComponent<VRCObjectSync>(go);
                }
                else if (sync != null)
                {
                    // 同期OFFだが既存の VRCObjectSync が残っている（勝手には消さない）
                    syncLeftovers++;
                }

                // --- ハイブリッド固定（モード専用の U# 挙動）---
                if (_mode == MoveMode.HybridFreeze)
                {
                    var hybrid = go.GetComponent<PickupHybridFreeze>();
                    if (hybrid == null)
                    {
                        hybrid = UdonSharpUndo.AddComponent<PickupHybridFreeze>(go);
                        UdonSharpEditorUtility.CopyProxyToUdon(hybrid);
                    }
                }
                else if (go.GetComponent<PickupHybridFreeze>() != null)
                {
                    // 非ハイブリッドモードだが古い PickupHybridFreeze が残っている。
                    // U# コンポーネントを自動削除すると裏の UdonBehaviour が孤立し得るため、
                    // 消さずに警告だけ出す（手動で削除してもらう）。
                    hybridConflicts++;
                }

                // --- 自動戻り ---
                // PickupReturnTarget は自己完結（自前 tick ＋ 実行時 GetComponent）。
                // 確実にシリアライズされる minutes だけ設定すればよい。参照配線は不要。
                if (_enableReturn)
                {
                    var ret = go.GetComponent<PickupReturnTarget>();
                    if (ret == null) ret = UdonSharpUndo.AddComponent<PickupReturnTarget>(go);
                    else Undo.RecordObject(ret, UndoGroup);

                    ret.minutes = _returnMinutes;
                    UdonSharpEditorUtility.CopyProxyToUdon(ret);
                }

                EditorUtility.SetDirty(go);
            }

            Undo.CollapseUndoOperations(group);

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            if (qvPenSkipped > 0)
                Debug.Log("[Pickup Kit] QvPen ペン " + qvPenSkipped + " 個をスキップしました（元の設定を保護）。");

            Debug.Log("[Pickup Kit] " + targets.Count + " 個に適用しました（モード: " + _mode +
                      " / 同期: " + _syncPosition + " / 戻り: " + (_enableReturn ? _returnMinutes + "分" : "なし") + "）");

            if (hybridConflicts > 0)
                Debug.LogWarning("[Pickup Kit] " + hybridConflicts +
                    " 個に PickupHybridFreeze が残っています（現在のモードと競合）。不要なら手動で削除してください。");
            if (syncLeftovers > 0)
                Debug.LogWarning("[Pickup Kit] " + syncLeftovers +
                    " 個に VRCObjectSync が残っています（同期OFF設定）。同期不要なら手動で削除してください。");
        }

        private void ConfigureRigidbody(Rigidbody rb)
        {
            rb.mass = _mass;
            switch (_mode)
            {
                case MoveMode.ZeroGravityFloat:
                    rb.isKinematic = false;
                    rb.useGravity = false;
                    rb.drag = _floatDrag;
                    rb.angularDrag = _floatDrag;
                    break;
                case MoveMode.Gravity:
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.drag = _drag;
                    rb.angularDrag = _angularDrag;
                    break;
                case MoveMode.FixedInPlace:
                    rb.useGravity = false;
                    rb.isKinematic = true;
                    break;
                case MoveMode.HybridFreeze:
                    // 待機中は固定。掴むと PickupHybridFreeze が物理ONにする。
                    rb.useGravity = false;
                    rb.isKinematic = true;
                    rb.drag = _drag;
                    rb.angularDrag = _angularDrag;
                    break;
            }
        }

        // ---------------------------------------------------------------
        // QvPen 召喚ギミックのセットアップ補助
        // ---------------------------------------------------------------
        private bool _qvFoldout = false;

        private void DrawQvPenSection()
        {
            _qvFoldout = EditorGUILayout.Foldout(_qvFoldout, "QvPen 召喚ギミック", true);
            if (!_qvFoldout) return;

            EditorGUILayout.HelpBox(
                "VR で右スティック上を長押しすると、空いているペンを右手へ呼び寄せます。\n" +
                "QvPen など VRC_Pickup を持つペンを Hierarchy で選択し、下のボタンで QvPenSummoner を作成します。",
                MessageType.None);

            if (GUILayout.Button("シーンの QvPen ペンを選択"))
                SelectScenePickups(excludeQvPen: false, qvPenOnly: true);

            // 選択中から VRC_Pickup を収集（子も探索）
            var pens = new List<VRC_Pickup>();
            foreach (var go in Selection.gameObjects)
            {
                if (go == null || EditorUtility.IsPersistent(go)) continue;
                var found = go.GetComponentsInChildren<VRC_Pickup>(true);
                foreach (var p in found)
                    if (p != null && !pens.Contains(p)) pens.Add(p);
            }

            EditorGUILayout.LabelField("選択から見つかったペン: " + pens.Count + " 本");

            using (new EditorGUI.DisabledScope(pens.Count == 0))
            {
                if (GUILayout.Button("選択中のペンで QvPenSummoner を作成", GUILayout.Height(26f)))
                    CreateSummoner(pens);
            }
        }

        private void CreateSummoner(List<VRC_Pickup> pens)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("QvPenSummoner 作成");
            int group = Undo.GetCurrentGroup();

            var go = new GameObject("QvPenSummoner");
            Undo.RegisterCreatedObjectUndo(go, "QvPenSummoner 作成");
            var summoner = UdonSharpUndo.AddComponent<QvPenSummoner>(go);
            summoner.pens = pens.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(summoner);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = go;

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log("[Pickup Kit] QvPenSummoner を作成しました（ペン " + pens.Count + " 本を登録）。");
        }
    }
}
