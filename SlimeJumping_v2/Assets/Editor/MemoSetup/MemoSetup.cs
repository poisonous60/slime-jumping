using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using SlimeJumping.Interaction;

namespace SlimeJumping.EditorTools
{
    public static class MemoSetup
    {
        const string PREFAB_DIR = "Assets/Prefabs/Memo";
        const string MEMO_DIR = "Assets/Memos";
        const string SCENE_PATH = "Assets/Scenes/MemoTest.unity";

        public static string BuildAll()
        {
            EnsureFolders();
            string basePath = BuildBasePrefab();
            string textOnly = BuildVariant(basePath, "MemoPanel_TextOnly", hideIllustration: true, hideTitle: false, hideBody: false, bodyExpand: true);
            string imageOnly = BuildVariant(basePath, "MemoPanel_ImageOnly", hideIllustration: false, hideTitle: true, hideBody: true, bodyExpand: false, illustrationFull: true);
            string imageTop = BuildVariant(basePath, "MemoPanel_ImageTop", hideIllustration: false, hideTitle: false, hideBody: false, bodyExpand: false, illustrationTop: true);
            return $"Base={basePath} TextOnly={textOnly} ImageOnly={imageOnly} ImageTop={imageTop}";
        }

        public static string BuildProfiles()
        {
            EnsureFolders();
            CreateProfile("Memo_TestText", "MemoPanel_TextOnly",
                title: "", body: "Follow this path down. There is soft water below.", illustration: null);
            CreateProfile("Memo_TestImageText", "MemoPanel_ImageTop",
                title: "Hot spring trace", body: "Warm water gently ripples here.", illustration: FindBuiltinIllustration());
            CreateProfile("Memo_TestImage", "MemoPanel_ImageOnly",
                title: "", body: "", illustration: FindBuiltinIllustration());
            return "profiles created";
        }

        public static string BuildTestScene()
        {
            EnsureFolders();
            var scenePath = SCENE_PATH;
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0, -2.5f, 0);
            ground.transform.localScale = new Vector3(30, 1, 4);
            var groundCol = ground.GetComponent<Collider>();
            if (groundCol != null) Object.DestroyImmediate(groundCol);
            var groundRb = ground.AddComponent<BoxCollider2D>();
            groundRb.size = new Vector2(30, 1);
            groundRb.offset = Vector2.zero;

            var camGO = GameObject.Find("Main Camera");
            if (camGO != null)
            {
                camGO.transform.position = new Vector3(0, 0, -10);
                var cam = camGO.GetComponent<UnityEngine.Camera>();
                if (cam != null) { cam.orthographic = true; cam.orthographicSize = 5; }
            }

            var slimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prebs/Slime.prefab");
            if (slimePrefab != null)
            {
                var slimeInstance = (GameObject)PrefabUtility.InstantiatePrefab(slimePrefab);
                slimeInstance.transform.position = new Vector3(-6, -1, 0);
            }

            CreateZone("MemoZone_Text", new Vector3(-4, 0.5f, -0.5f), "MemoPanel_TextOnly", "Memo_TestText");
            CreateZone("MemoZone_ImageText", new Vector3(0, 0.5f, -0.5f), "MemoPanel_ImageTop", "Memo_TestImageText");
            CreateZone("MemoZone_Image", new Vector3(4, 0.5f, -0.5f), "MemoPanel_ImageOnly", "Memo_TestImage");

            CreateLabel("Zone A: TextOnly", new Vector3(-4, 2.5f, -0.5f));
            CreateLabel("Zone B: Image+Text", new Vector3(0, 2.5f, -0.5f));
            CreateLabel("Zone C: ImageOnly", new Vector3(4, 2.5f, -0.5f));
            CreateLabel("(Slime start)", new Vector3(-6, -0.8f, -0.5f));

            EditorSceneManager.SaveScene(scene, scenePath);
            return $"Saved scene: {scenePath}";
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PREFAB_DIR))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Memo");
            if (!AssetDatabase.IsValidFolder(MEMO_DIR))
                AssetDatabase.CreateFolder("Assets", "Memos");
        }

        static string BuildBasePrefab()
        {
            var root = new GameObject("MemoPanel_Base", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            var cg = root.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;
            cg.alpha = 0f;

            var rootSlot = root.AddComponent<MemoSlot>();
            rootSlot.slotType = MemoSlotType.PanelRoot;

            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(300, 200);
            rootRt.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            rootRt.localRotation = Quaternion.Euler(0, 0, -2f);

            Sprite sliced = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            AddChildImage(root, "Shadow", sliced,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(6, -6), new Vector2(6, -6),
                new Color(0, 0, 0, 0.35f), MemoSlotType.PanelRoot, addSlot: false);

            var bgGo = AddChildImage(root, "Background", sliced,
                new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.96f, 0.90f, 0.78f, 1f), MemoSlotType.Background, addSlot: true);

            var illGo = AddChildImage(root, "Illustration", null,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -20), Vector2.zero,
                Color.white, MemoSlotType.Illustration, addSlot: true);
            var illRt = (RectTransform)illGo.transform;
            illRt.pivot = new Vector2(0.5f, 1f);
            illRt.sizeDelta = new Vector2(120, 100);
            var illImg = illGo.GetComponent<Image>();
            illImg.preserveAspect = true;
            illGo.SetActive(false);

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(root.transform, false);
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "Title";
            titleTmp.fontSize = 24;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.color = new Color(0.12f, 0.10f, 0.08f, 1f);
            var titleRt = (RectTransform)titleGo.transform;
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0, -15);
            titleRt.sizeDelta = new Vector2(-30, 35);
            var titleSlot = titleGo.AddComponent<MemoSlot>();
            titleSlot.slotType = MemoSlotType.Title;

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(root.transform, false);
            var bodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            bodyTmp.text = "Body text...";
            bodyTmp.fontSize = 16;
            bodyTmp.alignment = TextAlignmentOptions.TopLeft;
            bodyTmp.color = new Color(0.15f, 0.12f, 0.10f, 1f);
            var bodyRt = (RectTransform)bodyGo.transform;
            bodyRt.anchorMin = new Vector2(0, 0);
            bodyRt.anchorMax = new Vector2(1, 1);
            bodyRt.offsetMin = new Vector2(20, 15);
            bodyRt.offsetMax = new Vector2(-20, -55);
            var bodySlot = bodyGo.AddComponent<MemoSlot>();
            bodySlot.slotType = MemoSlotType.Body;

            string path = $"{PREFAB_DIR}/MemoPanel_Base.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab != null ? path : null;
        }

        static GameObject AddChildImage(GameObject parent, string name, Sprite sprite,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
            Color color, MemoSlotType slotType, bool addSlot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var img = go.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
            }
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            if (addSlot)
            {
                var s = go.AddComponent<MemoSlot>();
                s.slotType = slotType;
            }
            return go;
        }

        static string BuildVariant(string basePath, string variantName,
            bool hideIllustration, bool hideTitle, bool hideBody,
            bool bodyExpand, bool illustrationFull = false, bool illustrationTop = false)
        {
            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (baseAsset == null) return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);

            var slots = instance.GetComponentsInChildren<MemoSlot>(true);
            foreach (var s in slots)
            {
                if (hideIllustration && s.slotType == MemoSlotType.Illustration) s.gameObject.SetActive(false);
                if (hideTitle && s.slotType == MemoSlotType.Title) s.gameObject.SetActive(false);
                if (hideBody && s.slotType == MemoSlotType.Body) s.gameObject.SetActive(false);

                if (illustrationFull && s.slotType == MemoSlotType.Illustration)
                {
                    s.gameObject.SetActive(true);
                    var rt = (RectTransform)s.transform;
                    rt.anchorMin = new Vector2(0.05f, 0.05f);
                    rt.anchorMax = new Vector2(0.95f, 0.95f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = Vector2.zero;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
                if (illustrationTop && s.slotType == MemoSlotType.Illustration)
                {
                    s.gameObject.SetActive(true);
                    var rt = (RectTransform)s.transform;
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0, -15);
                    rt.sizeDelta = new Vector2(160, 90);
                }
                if (illustrationTop && s.slotType == MemoSlotType.Title)
                {
                    var rt = (RectTransform)s.transform;
                    rt.anchoredPosition = new Vector2(0, -115);
                }
                if (illustrationTop && s.slotType == MemoSlotType.Body)
                {
                    var rt = (RectTransform)s.transform;
                    rt.offsetMax = new Vector2(-20, -155);
                }

                if (bodyExpand && s.slotType == MemoSlotType.Body)
                {
                    var rt = (RectTransform)s.transform;
                    rt.offsetMax = new Vector2(-20, -55);
                }
            }

            string variantPath = $"{PREFAB_DIR}/{variantName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            Object.DestroyImmediate(instance);
            return saved != null ? variantPath : null;
        }

        static Sprite FindBuiltinIllustration()
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        }

        static void CreateProfile(string assetName, string variantName, string title, string body, Sprite illustration)
        {
            string path = $"{MEMO_DIR}/{assetName}.asset";
            var p = ScriptableObject.CreateInstance<MemoProfile>();
            p.layoutPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PREFAB_DIR}/{variantName}.prefab");
            p.titleText = title;
            p.bodyText = body;
            p.illustration = illustration;
            AssetDatabase.CreateAsset(p, path);
        }

        static void CreateZone(string zoneName, Vector3 pos, string variantName, string profileName)
        {
            var profile = AssetDatabase.LoadAssetAtPath<MemoProfile>($"{MEMO_DIR}/{profileName}.asset");
            if (profile == null) return;

            var zone = new GameObject(zoneName);
            zone.transform.position = pos;
            var col = zone.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(3, 2);

            var displayGo = new GameObject("Display");
            displayGo.transform.SetParent(zone.transform, false);
            displayGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var display = displayGo.AddComponent<MemoDisplay>();

            var trigger = zone.AddComponent<MemoTrigger>();
            var triggerType = typeof(MemoTrigger);
            triggerType.GetField("profile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(trigger, profile);
            triggerType.GetField("display", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(trigger, display);
        }

        static void CreateLabel(string text, Vector3 pos)
        {
            var go = new GameObject($"Label_{text}");
            go.transform.position = pos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 2;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }
    }
}
