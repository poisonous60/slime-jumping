using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.EditorTools
{
    /// <summary>
    /// Web 배포용 씬 쌍(MainScene_Web / MainMenuScene_Web)을 원본에서 다시 만든다.
    /// 원본은 읽기만 하고 복사본에서만 지우므로, 원본이 바뀌면 다시 돌려서 갱신한다.
    /// 지우는 기준 셋: 2스테이지 이상 콘텐츠 / 배경 / WebGL 에서 깨지는 SpaceWarpDraw 셰이더를 쓰는 렌더러.
    /// 마지막 기준만 이름이 아니라 셰이더로 판정한다 — 이름은 바뀌어도 조건은 그대로다.
    /// </summary>
    public static class WebSceneBuilder
    {
        private const string GameplaySrc = "Assets/Scenes/MainScene.unity";
        private const string GameplayDst = "Assets/Scenes/MainScene_Web.unity";
        private const string MenuSrc     = "Assets/Scenes/MainMenuScene.unity";
        private const string MenuDst     = "Assets/Scenes/MainMenuScene_Web.unity";
        private const string EndSrc      = "Assets/Scenes/EndScene.unity";
        private const string EndDst      = "Assets/Scenes/EndScene_Web.unity";

        private static readonly string[] RootsToDelete =
        {
            "stage1_background",
            "stage2_floor1", "stage2_floor2", "stage2_background",
            "stage3_floor1", "stage3_floor2_left", "stage3_floor2_right", "stage3_floor3",
            "stage3_right_hidden", "stage3_background",
            "stage4_floor1", "stage4_background",
            "TerrainMaker (6)", "TerrainMaker (9)", "TerrainMaker (10)",
        };

        private static readonly string[] EtcChildrenToDelete =
        {
            "SnowEmitterArea", "Snow (Stage2)", "ColdZone", "SnowstormTest", "SnowAmbient",
            "GraniteBlast_Stage2_Test", "GraniteBlast_Attackable_Test",
            "Rain", "RainAmbient", "RainSplash", "WetGroundBand", "RainZone", "Stage3ReflectionProbe",
            "SpaceAmbient",
        };

        /// WebGL2 는 이 패스를 지원하지 않아 렌더러가 통째로 에러 셰이더(분홍)로 대체된다.
        private static readonly string[] BrokenShaders =
        {
            "SlimeJumping/Rain_Refraction",
            "SlimeJumping/SpaceWarp_Distortion",
            "SlimeJumping/SpaceWarp_BulbShatter",
            "SlimeJumping/Sandevistan_EnvTint",
            "SlimeJumping/BellWave_Distortion",
        };

        /// 데스크톱 전용 옵션 행. Web 은 소리만 남긴다.
        private static readonly string[] OptionRowsToHide =
        {
            "ResolutionRow", "WindowModeRow", "FrameLimitRow", "VSyncRow", "BackgroundCameraRow",
        };

        /// 고도 하늘 연출. 1스테이지에선 보이지 않는데 전용 블룸 카메라를 매 프레임 돌린다.
        private static readonly string[] ManagerChildrenToDisable =
        {
            "StarFieldOverlay", "NebulaOverlay",
        };

        /// 폭은 원본 그대로 둔다. 좁히면 Actions 의 New Game 버튼이 두 줄로 접힌다.
        private const float WebOptionsBoxWidth = 900f;

        private static readonly UISlotType[] MenuSlotsToHide =
        {
            UISlotType.LoadButton, UISlotType.QuitButton,
        };

        /// Web 빌드에 MainScene 이 남아 있으면 상위 스테이지 에셋이 전부 딸려와 감축이 사라진다.
        [MenuItem("SlimeJumping/Web/Switch Build List to Web")]
        public static void SwitchBuildListToWeb() => ApplyBuildList(true);

        [MenuItem("SlimeJumping/Web/Switch Build List to Desktop")]
        public static void SwitchBuildListToDesktop() => ApplyBuildList(false);

        private static void ApplyBuildList(bool web)
        {
            var wanted = new Dictionary<string, bool>
            {
                { MenuSrc,     !web }, { GameplaySrc, !web }, { EndSrc, !web },
                { MenuDst,      web }, { GameplayDst,  web }, { EndDst,  web },
            };

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var present = new HashSet<string>();
            foreach (var s in list) present.Add(s.path);

            foreach (var kv in wanted)
                if (!present.Contains(kv.Key) && System.IO.File.Exists(kv.Key))
                    list.Add(new EditorBuildSettingsScene(kv.Key, false));

            var log = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                bool on;
                if (!wanted.TryGetValue(list[i].path, out on)) continue;
                list[i] = new EditorBuildSettingsScene(list[i].path, on);
            }

            // Web 은 메뉴 씬이 먼저 와야 첫 씬으로 뜬다.
            list.Sort((a, b) => Rank(a.path, web).CompareTo(Rank(b.path, web)));
            EditorBuildSettings.scenes = list.ToArray();

            foreach (var s in EditorBuildSettings.scenes)
                log.AppendLine((s.enabled ? "  [x] " : "  [ ] ") + s.path);
            Debug.Log("[WebScene] 빌드 씬 목록 = " + (web ? "WEB" : "DESKTOP") + "\n" + log);
        }

        private static int Rank(string path, bool web)
        {
            if (path == (web ? MenuDst : MenuSrc)) return 0;
            if (path == (web ? GameplayDst : GameplaySrc)) return 1;
            return 2;
        }

        [MenuItem("SlimeJumping/Web/Build Web Scenes")]
        public static void BuildWebScenes()
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                Debug.LogError("[WebScene] 열려 있는 씬에 저장 안 된 변경이 있다. 저장하고 다시 실행.");
                return;
            }

            var log = new StringBuilder();
            BuildGameplayScene(log);
            BuildMenuScene(log);
            BuildEndScene(log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(GameplaySrc);
            Debug.Log("[WebScene] 생성 완료\n" + log);
        }

        private static void BuildGameplayScene(StringBuilder log)
        {
            var scene = CopyAndOpen(GameplaySrc, GameplayDst, log);
            var roots = scene.GetRootGameObjects();

            var byName = new Dictionary<string, GameObject>();
            foreach (var r in roots) byName[r.name] = r;

            int removed = 0;
            foreach (var name in RootsToDelete)
            {
                GameObject go;
                if (!byName.TryGetValue(name, out go)) { log.AppendLine("  MISSING root: " + name); continue; }
                Object.DestroyImmediate(go);
                removed++;
            }
            log.AppendLine("  루트 삭제 " + removed + "/" + RootsToDelete.Length);

            GameObject etc;
            if (byName.TryGetValue("etc", out etc) && etc != null)
            {
                int n = 0;
                foreach (var name in EtcChildrenToDelete)
                {
                    var t = etc.transform.Find(name);
                    if (t == null) { log.AppendLine("  MISSING etc/" + name); continue; }
                    Object.DestroyImmediate(t.gameObject);
                    n++;
                }
                log.AppendLine("  etc 자식 삭제 " + n + "/" + EtcChildrenToDelete.Length);
                foreach (Transform leftover in etc.transform) log.AppendLine("  etc 잔존: " + leftover.name);
            }

            log.AppendLine("  깨진 셰이더 렌더러 삭제 " + StripBrokenShaderObjects(scene));

            var slime = GameObject.Find("Slime");
            if (slime == null) log.AppendLine("  WARN: Slime 없음 — WebStageExit 미부착");
            else if (slime.GetComponent<WebStageExit>() != null) log.AppendLine("  WebStageExit 이미 있음");
            else { slime.AddComponent<WebStageExit>(); log.AppendLine("  WebStageExit 부착"); }

            GameObject manager;
            if (byName.TryGetValue("Manager", out manager) && manager != null)
            {
                foreach (var name in ManagerChildrenToDisable)
                {
                    var t = manager.transform.Find(name);
                    if (t == null) { log.AppendLine("  MISSING Manager/" + name); continue; }
                    t.gameObject.SetActive(false);
                    log.AppendLine("  Manager/" + name + " 비활성");
                }
            }

            log.AppendLine("  옵션 행 숨김 " + HideDesktopOptionRows(scene));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// EndScene 은 데스크톱과 공유라 여기서 지우면 Windows 옵션창이 같이 망가진다. 사본에서만 정리한다.
        [MenuItem("SlimeJumping/Web/Build Web End Scene")]
        public static void BuildWebEndScene()
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                Debug.LogError("[WebScene] 열려 있는 씬에 저장 안 된 변경이 있다. 저장하고 다시 실행.");
                return;
            }

            var log = new StringBuilder();
            BuildEndScene(log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WebScene] EndScene_Web 생성\n" + log);
        }

        private static void BuildEndScene(StringBuilder log)
        {
            var scene = CopyAndOpen(EndSrc, EndDst, log);
            log.AppendLine("  옵션 행 숨김 " + HideDesktopOptionRows(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void BuildMenuScene(StringBuilder log)
        {
            var scene = CopyAndOpen(MenuSrc, MenuDst, log);

            int hidden = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var slot in root.GetComponentsInChildren<UISlot>(true))
                {
                    if (System.Array.IndexOf(MenuSlotsToHide, slot.slotType) < 0) continue;
                    slot.gameObject.SetActive(false);
                    hidden++;
                }
            }
            log.AppendLine("  메뉴 버튼 숨김 " + hidden + " (Load / Quit)");
            log.AppendLine("  옵션 행 숨김 " + HideDesktopOptionRows(scene));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static UnityEngine.SceneManagement.Scene CopyAndOpen(string src, string dst, StringBuilder log)
        {
            AssetDatabase.DeleteAsset(dst);
            AssetDatabase.CopyAsset(src, dst);
            AssetDatabase.Refresh();
            log.AppendLine(System.IO.Path.GetFileName(dst));
            return EditorSceneManager.OpenScene(dst);
        }

        private static int StripBrokenShaderObjects(UnityEngine.SceneManagement.Scene scene)
        {
            var doomed = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null) continue;
                        if (System.Array.IndexOf(BrokenShaders, m.shader.name) < 0) continue;
                        doomed.Add(r.gameObject);
                        break;
                    }
                }
            }
            foreach (var go in doomed) if (go != null) Object.DestroyImmediate(go);
            return doomed.Count;
        }

        /// OptionsPanel 은 UIRoot 프리팹 안이라 자식을 지울 수 없다 — 비활성화가 유일한 씬 로컬 수단이다.
        /// SidePanel 은 비활성화만으로 부족하다. OptionsPanel 이 Awake 에서 클리어 여부로 되켜므로
        /// 참조 자체를 비워 코드가 손댈 수 없게 만든다.
        private static int HideDesktopOptionRows(UnityEngine.SceneManagement.Scene scene)
        {
            int hidden = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var panel in root.GetComponentsInChildren<Transform>(true))
                {
                    if (panel.name != "OptionsPanel") continue;

                    var box = panel.Find("Box");
                    if (box != null)
                    {
                        foreach (var rowName in OptionRowsToHide)
                        {
                            var row = box.Find(rowName);
                            if (row == null) continue;
                            row.gameObject.SetActive(false);
                            hidden++;
                        }
                        ShrinkOptionsBox(box as RectTransform);
                    }

                    var side = panel.Find("SidePanel");
                    if (side != null) { side.gameObject.SetActive(false); hidden++; }

                    var op = panel.GetComponent<SlimeJumping.UI.Panels.OptionsPanel>();
                    if (op == null) continue;
                    var so = new SerializedObject(op);
                    var sp = so.FindProperty("sidePanel");
                    if (sp == null) continue;
                    sp.objectReferenceValue = null;
                    so.ApplyModifiedProperties();
                }
            }
            return hidden;
        }

        /// 행이 다섯 개 빠져 세로가 남는다. 높이는 내용에 맡기고 폭만 좁힌다.
        private static void ShrinkOptionsBox(RectTransform box)
        {
            if (box == null) return;

            var fitter = box.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter == null) fitter = box.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;

            box.sizeDelta = new Vector2(WebOptionsBoxWidth, box.sizeDelta.y);
            box.anchoredPosition = Vector2.zero;
        }
    }
}
