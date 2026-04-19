using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class WaterfallImpactVFXSetup
    {
        const string RootName = "WaterfallImpactVFX_Test";

        [MenuItem("SlimeJumping/Mechanism/Setup Waterfall Impact VFX")]
        public static void Setup()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var wheel = GameObject.Find("rotating/Object_4_Collider2D");
            Vector3 anchor = wheel != null ? wheel.transform.position : Vector3.zero;
            // Root는 파이프 출구 추정 위치(수차 위쪽). pathPointsLocal은 이 지점 기준 로컬 offset.
            Vector3 rootWorld = anchor + new Vector3(0f, 15f, 0f);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Waterfall Impact VFX");
            root.transform.position = rootWorld;

            var stream = CreateStream(root.transform);
            var splash = CreateSplash(root.transform);

            var vfx = root.AddComponent<WaterfallImpactVFX>();
            var so = new SerializedObject(vfx);

            // Default path: pipe가 오른쪽으로 2 유닛 나가다 꺾이는 느낌의 두 점.
            // 씬에서 핸들로 자유 편집 (shift-click 삽입, ctrl-click 삭제).
            var pathProp = so.FindProperty("pathPointsLocal");
            pathProp.arraySize = 2;
            pathProp.GetArrayElementAtIndex(0).vector2Value = Vector2.zero;
            pathProp.GetArrayElementAtIndex(1).vector2Value = new Vector2(2f, 0f);

            so.FindProperty("streamLine").objectReferenceValue = stream.GetComponent<LineRenderer>();
            so.FindProperty("splash").objectReferenceValue     = splash.GetComponent<ParticleSystem>();
            if (wheel != null)
            {
                var col = wheel.GetComponent<Collider2D>();
                if (col != null)
                    so.FindProperty("targetCollider").objectReferenceValue = col;
            }
            so.FindProperty("rayDirection").vector2Value = Vector2.down;
            so.FindProperty("maxDistance").floatValue    = 25f;
            so.FindProperty("streamColor").colorValue    = new Color(0.45f, 0.85f, 1f, 1f);
            so.FindProperty("splashColor").colorValue    = new Color(0.45f, 0.85f, 1f, 1f);
            so.FindProperty("streamZOffset").floatValue  = -0.5f;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;

            Debug.Log($"[WaterfallImpactVFX] Created '{RootName}' at {rootWorld}. " +
                      (wheel != null ? "Target wired to Object_4_Collider2D. " : "Target collider not auto-found — set manually. ") +
                      "씬뷰에서 노란 핸들로 경로 편집 (shift-click 삽입, ctrl-click 삭제).");
        }

        static GameObject CreateStream(Transform parent)
        {
            var go = new GameObject("Stream");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace    = true;
            lr.positionCount    = 2;
            lr.startWidth       = 0.45f;
            lr.endWidth         = 0.45f;
            lr.numCornerVertices = 4;
            lr.numCapVertices   = 4;
            lr.alignment        = LineAlignment.View;
            lr.textureMode      = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows   = false;
            lr.sortingOrder     = 10;
            lr.sharedMaterial   = MakeSpriteDefaultMaterial("M_WaterStream");
            return go;
        }

        static GameObject CreateSplash(Transform parent)
        {
            var go = new GameObject("Splash");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.5f);

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop              = true;
            main.playOnAwake       = false;
            main.startLifetime     = 0.35f;
            main.startSpeed        = 3.5f;
            main.startSize         = 0.18f;
            main.startColor        = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.85f, 1f, 1f));
            main.gravityModifier   = 1.2f;
            main.simulationSpace   = ParticleSystemSimulationSpace.World;
            main.maxParticles      = 200;

            var emission = ps.emission;
            emission.rateOverTime = 45f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = 45f;
            shape.radius    = 0.05f;
            shape.rotation  = new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 0.2f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode        = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial    = MakeSpriteDefaultMaterial("M_WaterSplash");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.sortingOrder      = 11;

            return go;
        }

        static Material MakeSpriteDefaultMaterial(string name)
        {
            var shader = Shader.Find("Sprites/Default");
            return new Material(shader) { name = name };
        }
    }
}
