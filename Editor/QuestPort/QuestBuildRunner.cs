using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor;
using VRC.SDK3.Editor;

namespace you5248.QuestPort
{
    /// <summary>
    /// VRChat SDK のワールドビルドを外部（unicli）から起動し、結果をコンソールへ
    /// [QuestBuild] タグで出力する検証用ランナー。アップロードは行わない。
    /// </summary>
    public static class QuestBuildRunner
    {
        const string AutomationRequestRelativePath = "Library/QuestBuild.request";
        const string AutomationResultRelativePath = "Library/QuestBuild.result";
        const string IosBuildRequestRelativePath = "Library/IosBuild.request";
        static int _builderRetryCount;
        static bool _sdkPanelOpenRequested;

        [InitializeOnLoadMethod]
        static void StartRequestedBuildAfterReload()
        {
            string requestPath = ProjectPath(AutomationRequestRelativePath);
            if (!File.Exists(requestPath)) return;

            File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                "STARTING " + DateTime.Now.ToString("O"));
            EditorApplication.delayCall += StartBuild;
        }

        [InitializeOnLoadMethod]
        static void ContinueRequestedIosBuildAfterReload()
        {
            if (!File.Exists(ProjectPath(IosBuildRequestRelativePath))) return;
            ContinueRequestedIosBuild();
        }

        [MenuItem("you5248/Quest Port/Switch to iOS and Build World Bundle (No Upload)")]
        public static void StartIosBuild()
        {
            File.WriteAllText(ProjectPath(IosBuildRequestRelativePath),
                "REQUESTED " + DateTime.Now.ToString("O"));
            File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                "SWITCHING_TO_IOS " + DateTime.Now.ToString("O"));
            ContinueRequestedIosBuild();
        }

        static void ContinueRequestedIosBuild()
        {
            string requestPath = ProjectPath(IosBuildRequestRelativePath);
            if (!File.Exists(requestPath)) return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ContinueRequestedIosBuild;
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                bool switching = EditorUserBuildSettings.SwitchActiveBuildTargetAsync(
                    BuildTargetGroup.iOS, BuildTarget.iOS);
                if (!switching)
                {
                    const string message = "Could not start switching the active build target to iOS.";
                    File.Delete(requestPath);
                    File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                        "FAILED " + DateTime.Now.ToString("O") + Environment.NewLine + message);
                    Debug.LogError("[iOSBuild] FAILED: " + message);
                }
                else
                {
                    Debug.Log("[iOSBuild] Switching active build target to iOS...");
                }
                return;
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                const string message = "Script compilation failed after switching to iOS.";
                File.Delete(requestPath);
                File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                    "FAILED " + DateTime.Now.ToString("O") + Environment.NewLine + message);
                Debug.LogError("[iOSBuild] FAILED: " + message);
                return;
            }

            // リクエストを先に消費し、SDKビルド中のドメインリロードによる二重起動を防ぐ。
            File.Delete(requestPath);
            File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                "BUILDING_IOS " + DateTime.Now.ToString("O"));
            Debug.Log("[iOSBuild] iOS script compilation succeeded. Starting no-upload world build...");
            if (Application.isBatchMode)
            {
                // RunAsync 完了時に適切な終了コードでヘッドレスUnityを閉じるため、
                // 既存の automation フラグを利用する。StartBuild 呼び出し直前なので二重起動しない。
                File.WriteAllText(ProjectPath(AutomationRequestRelativePath),
                    "IOS_BATCH " + DateTime.Now.ToString("O"));
            }
            StartBuild();
        }

        [MenuItem("you5248/Quest Port/Build World Bundle (No Upload)")]
        public static void StartBuild()
        {
            Debug.Log("[QuestBuild] start: target=" + EditorUserBuildSettings.activeBuildTarget);

            // SDKのBuilder取得だけ成功しても、パネルが閉じているとBuild()側で拒否される。
            // 先にパネルを表示し、初期化が走るEditor更新を1回待ってからBuilderを呼ぶ。
            if (!_sdkPanelOpenRequested)
            {
                var panel = EditorWindow.GetWindow<VRCSdkControlPanel>("VRChat SDK");
                panel.Show();
                panel.Repaint();
                _sdkPanelOpenRequested = true;
                EditorApplication.delayCall += StartBuild;
                return;
            }

            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder))
            {
                if (_builderRetryCount++ < 30)
                {
                    Debug.LogWarning("[QuestBuild] Waiting for SDK builder initialization...");
                    EditorApplication.delayCall += StartBuild;
                }
                else
                {
                    const string message = "SDK builder initialization timed out.";
                    File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                        "FAILED " + DateTime.Now.ToString("O") + Environment.NewLine + message);
                    Debug.LogError("[QuestBuild] FAILED: " + message);
                    _builderRetryCount = 0;
                    _sdkPanelOpenRequested = false;
                }
                return;
            }
            _builderRetryCount = 0;
            _sdkPanelOpenRequested = false;
            string requestPath = ProjectPath(AutomationRequestRelativePath);
            bool automation = File.Exists(requestPath);
            if (automation) File.Delete(requestPath);
            RunAsync(builder, automation);
        }

        private static async void RunAsync(IVRCSdkWorldBuilderApi builder, bool automation)
        {
            try
            {
                string bundlePath = await builder.Build();
                var fi = new FileInfo(bundlePath);
                File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                    "SUCCESS " + DateTime.Now.ToString("O") + Environment.NewLine +
                    bundlePath + Environment.NewLine +
                    (fi.Exists ? fi.Length.ToString() : "MISSING"));
                Debug.Log("[QuestBuild] SUCCESS: " + bundlePath +
                          " | size=" + (fi.Exists ? (fi.Length / 1048576.0).ToString("F1") + "MB" : "?"));
                if (automation) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(ProjectPath(AutomationResultRelativePath),
                    "FAILED " + DateTime.Now.ToString("O") + Environment.NewLine + e);
                Debug.LogError("[QuestBuild] FAILED: " + e.Message);
                if (automation) EditorApplication.Exit(1);
            }
        }

        static string ProjectPath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
