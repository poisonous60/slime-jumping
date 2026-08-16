using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SlimeJumping.Controller;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.UI
{
    /// Playable slime pen for the main menu: invisible floor/side walls sized to the
    /// camera view, plus jelly surfaces projected from the menu's buttons and title
    /// glyphs. Letters spawn MenuLetterJelly from baked ink contours (the TMP title
    /// itself goes alpha 0 and only keeps the layout); buttons spawn MenuButtonJelly
    /// with an on-image deformer. Falls back to rigid ink-box colliders when the
    /// contour bake is missing a glyph.
    public class MenuSlimeArena : MonoBehaviour
    {
        private const int GroundLayer = 7;

        [SerializeField] private UnityEngine.Camera arenaCamera;

        [Tooltip("Optional override of the shared config. Leave null to auto-load " +
                 "Assets/Resources/MenuSlimeArenaConfig.asset.")]
        [SerializeField] private MenuSlimeArenaConfig configOverride;

        private MenuSlimeArenaConfig config;
        private Transform bumperRoot;
        private MenuGlyphContours contours;
        private SlimeController slime;
        private Material letterMaterial;
        private TMP_Text hiddenTitle;

        private IEnumerator Start()
        {
            if (arenaCamera == null)
            {
                enabled = false;
                yield break;
            }
            config = MenuSlimeArenaConfig.Resolve(configOverride);
            if (config == null) config = ScriptableObject.CreateInstance<MenuSlimeArenaConfig>();

            BuildWalls();

            // 버튼 위치는 첫 캔버스 레이아웃 뒤에야 확정된다.
            yield return null;
            yield return null;
            BuildUiColliders();
        }

        private void BuildWalls()
        {
            float halfH = arenaCamera.orthographicSize;
            float halfW = halfH * arenaCamera.aspect;
            Vector2 center = arenaCamera.transform.position;
            float t = config.wallThickness;

            var walls = new GameObject("Walls");
            walls.transform.SetParent(transform, false);
            CreateBox(walls.transform, "Floor",
                new Vector2(center.x, center.y - halfH - t * 0.5f),
                new Vector2(halfW * 2f + t * 2f, t));

            // 벽은 화면 위 낙하 통로까지 이어져 스폰 슬라임이 옆으로 새지 않는다.
            float wallHeight = halfH * 8f;
            float wallCenterY = center.y - halfH + wallHeight * 0.5f;
            CreateBox(walls.transform, "WallLeft",
                new Vector2(center.x - halfW - t * 0.5f, wallCenterY), new Vector2(t, wallHeight));
            CreateBox(walls.transform, "WallRight",
                new Vector2(center.x + halfW + t * 0.5f, wallCenterY), new Vector2(t, wallHeight));
        }

        private static GameObject CreateBox(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name) { layer = GroundLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<BoxCollider2D>().size = size;
            return go;
        }

        private void BuildUiColliders()
        {
            var manager = UIManager.Instance;
            var panel = manager != null ? manager.Get<MainMenuPanel>() : null;
            if (panel == null) return;

            bumperRoot = new GameObject("UiBumpers").transform;
            bumperRoot.SetParent(transform, false);
            contours = MenuGlyphContours.Load();
            slime = FindFirstObjectByType<SlimeController>();

            foreach (var button in panel.GetComponentsInChildren<Button>())
            {
                var rt = (RectTransform)button.transform;
                ProjectRect(rt, out Vector2 min, out Vector2 max);
                var jelly = MenuButtonJelly.Create(bumperRoot, "Bumper_" + button.name, GroundLayer,
                    min, max, config, slime, button.targetGraphic, arenaCamera);
                if (jelly == null)
                    CreateBox(bumperRoot, "Bumper_" + button.name, (min + max) * 0.5f, max - min);
            }
            foreach (var slot in panel.GetComponentsInChildren<UISlot>())
                if (slot.slotType == UISlotType.Title)
                    BuildTitleColliders(slot);
        }

        private void BuildTitleColliders(UISlot slot)
        {
            var text = slot.GetComponent<TMP_Text>();
            if (text == null) return;
            text.ForceMeshUpdate();
            var info = text.textInfo;

            if (contours != null && text.font != null && contours.fontName != text.font.name)
            {
                Debug.LogWarning($"MenuSlimeArena: contour bake is for '{contours.fontName}' " +
                                 $"but title font is '{text.font.name}' — rigid fallback. Re-bake.", this);
                contours = null;
            }

            bool anyJelly = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;

                // 잉크 박스 — 렌더 쿼드는 SDF 패딩 9px 가 붙어 글자 사이 틈을 막는다.
                float scale = ch.scale;
                var metrics = ch.textElement.glyph.metrics;
                float xMin = ch.origin + metrics.horizontalBearingX * scale;
                float xMax = xMin + metrics.width * scale;
                float yMax = ch.baseLine + metrics.horizontalBearingY * scale;
                float yMin = yMax - metrics.height * scale;

                Vector2 minScreen = text.transform.TransformPoint(new Vector2(xMin, yMin));
                Vector2 maxScreen = text.transform.TransformPoint(new Vector2(xMax, yMax));
                Vector2 min = arenaCamera.ScreenToWorldPoint(minScreen);
                Vector2 max = arenaCamera.ScreenToWorldPoint(maxScreen);

                var entry = contours != null ? contours.Find(ch.character) : null;
                if (entry == null || metrics.width <= 0f)
                {
                    CreateBox(bumperRoot, "Letter_" + ch.character, (min + max) * 0.5f, max - min);
                    continue;
                }

                Vector2 originScreen = text.transform.TransformPoint(new Vector2(ch.origin, ch.baseLine));
                Vector2 originWorld = arenaCamera.ScreenToWorldPoint(originScreen);
                float worldPerFontUnit = (max.x - min.x) / metrics.width;

                var glyphRect = ch.textElement.glyph.glyphRect;
                var render = new MenuLetterJelly.GlyphRenderData
                {
                    inkMin = new Vector2(metrics.horizontalBearingX, metrics.horizontalBearingY - metrics.height),
                    inkMax = new Vector2(metrics.horizontalBearingX + metrics.width, metrics.horizontalBearingY),
                    paddingFu = text.font.atlasPadding,
                };
                render.uvAdd = new Vector2(glyphRect.x, glyphRect.y) - render.inkMin;
                render.uvScale = new Vector2(1f / text.font.atlasWidth, 1f / text.font.atlasHeight);

                var jelly = MenuLetterJelly.Create(bumperRoot, "Letter_" + ch.character, GroundLayer,
                    originWorld, worldPerFontUnit, entry, render,
                    GetLetterMaterial(text), config, slime);
                if (jelly == null)
                {
                    CreateBox(bumperRoot, "Letter_" + ch.character, (min + max) * 0.5f, max - min);
                    continue;
                }
                anyJelly = true;
            }
            // alpha 0 은 TMP 글로우 패스에 안 먹어 잔상 띠가 남는다 — 컴포넌트를 꺼야 한다(레이아웃은 LayoutElement 몫).
            if (anyJelly)
            {
                hiddenTitle = text;
                text.enabled = false;
            }
        }

        private Material GetLetterMaterial(TMP_Text text)
        {
            if (letterMaterial != null) return letterMaterial;
            var shader = Shader.Find("SlimeJumping/UI/MenuGlyphJelly");
            letterMaterial = new Material(shader);
            letterMaterial.SetTexture("_MainTex", text.font.atlasTexture);
            letterMaterial.SetColor("_FaceColor", text.color);
            if (contours != null)
                letterMaterial.SetFloat("_EdgeThreshold", contours.edgeThreshold);
            // 글로우 소등 — 변형 중 엣지 halo 가 흰 줄로 읽힌다(유저 반려). 노브는 셰이더에 남김.
            letterMaterial.SetColor("_GlowColor", Color.clear);
            return letterMaterial;
        }

        private void OnDestroy()
        {
            if (letterMaterial != null) Destroy(letterMaterial);
            if (hiddenTitle != null) hiddenTitle.enabled = true;
        }

        /// 오버레이 캔버스의 월드 코너 = 스크린 픽셀 → ortho 역투영.
        private void ProjectRect(RectTransform rt, out Vector2 min, out Vector2 max)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            min = arenaCamera.ScreenToWorldPoint(corners[0]);
            max = arenaCamera.ScreenToWorldPoint(corners[2]);
        }
    }
}
