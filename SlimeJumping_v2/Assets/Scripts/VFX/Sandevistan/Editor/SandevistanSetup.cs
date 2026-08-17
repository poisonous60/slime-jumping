using UnityEditor;
using UnityEngine;
using SlimeJumping.VFX.Sandevistan;

namespace SlimeJumping.EditorTools
{
    /// 산데비스탄 매니저를 새 루트로 세운다. 기존 씬 오브젝트는 읽지도 고치지도 않고,
    /// 환경색 머티리얼만 없으면 만들어 붙인다. 루트 삭제 = 완전 롤백.
    public static class SandevistanSetup
    {
        const string RootName = "SandevistanManager";
        const string EnvMaterialPath = "Assets/Art/Shader/Mat_SandevistanEnvTint.mat";
        const string EnvShaderName = "SlimeJumping/Sandevistan_EnvTint";
        const string GhostMaterialPath = "Assets/Art/Shader/Mat_SlimeAfterimage.mat";

        [MenuItem("SlimeJumping/Mechanism/Create Sandevistan Manager")]
        public static void Setup()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogError($"[Sandevistan] '{RootName}' 루트가 이미 있다. 삭제 후 다시 실행.");
                return;
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Sandevistan Manager");
            var mgr = root.AddComponent<SandevistanManager>();

            var so = new SerializedObject(mgr);
            so.FindProperty("envTintMaterial").objectReferenceValue = EnsureEnvMaterial();

            var ghost = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (ghost == null)
                Debug.LogWarning($"[Sandevistan] 잔상 머티리얼 없음: {GhostMaterialPath} — 인스펙터에서 직접 물려라.");
            so.FindProperty("ghostMaterial").objectReferenceValue = ghost;
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("[Sandevistan] 매니저 배치 완료. 잔상색·간격·환경색은 이 컴포넌트에서 조절한다. " +
                      "옵션창 'Use a rudimentary implant' 로 켜고 끈다.", root);
        }

        static Material EnsureEnvMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(EnvMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find(EnvShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Sandevistan] 셰이더 없음: {EnvShaderName}");
                return null;
            }
            var mat = new Material(shader) { name = "Mat_SandevistanEnvTint" };
            AssetDatabase.CreateAsset(mat, EnvMaterialPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(EnvMaterialPath);
        }
    }
}
