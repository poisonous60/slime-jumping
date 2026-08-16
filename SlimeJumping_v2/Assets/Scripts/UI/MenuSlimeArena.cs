using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SlimeJumping.UI.Framework;
using SlimeJumping.UI.Panels;

namespace SlimeJumping.UI
{
    /// Playable slime pen for the main menu: invisible floor/side walls sized to the
    /// camera view, plus world colliders projected from the menu's buttons and title
    /// glyphs. Title colliders use the glyph ink box (no SDF padding), so the slime
    /// falls through letter gaps. UI touch reactions are not implemented yet — see
    /// docs/menu-letter-wobble handoff.
    public class MenuSlimeArena : MonoBehaviour
    {
        private const int GroundLayer = 7;

        [SerializeField] private UnityEngine.Camera arenaCamera;

        [Tooltip("Optional override of the shared config. Leave null to auto-load " +
                 "Assets/Resources/MenuSlimeArenaConfig.asset.")]
        [SerializeField] private MenuSlimeArenaConfig configOverride;

        private MenuSlimeArenaConfig config;
        private Transform bumperRoot;

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

            foreach (var button in panel.GetComponentsInChildren<Button>())
            {
                var rt = (RectTransform)button.transform;
                ProjectRect(rt, out Vector2 min, out Vector2 max);
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
                CreateBox(bumperRoot, "Letter_" + ch.character, (min + max) * 0.5f, max - min);
            }
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
