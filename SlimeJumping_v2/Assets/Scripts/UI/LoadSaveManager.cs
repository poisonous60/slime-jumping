using System;
using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.UI
{
    /// <summary>
    /// MainScene 세이브 주체. 저장 대상은 슬라임 월드 위치와 RunClock 경과뿐이고, 씬의 나머지는 로드 시 초기 상태로 온다.
    /// RunClock 을 실시간으로 돌리는 것도 여기다 — MainScene 에만 있는 단일 인스턴스라 중복 틱이 없다.
    /// Trail 은 TrailPersistence 오토세이브가 이미 실행 간 유지하므로 여기서 다시 직렬화하지 않는다.
    /// 저장 시점은 두 곳: 앱 종료, GameFlow.LoadMainMenu. 엔딩 진입은 저장 대신 삭제한다.
    /// OnDestroy 는 쓰지 않는다 — 씬 언로드 파괴 순서가 미정이라 그 시점의 씬 상태를 믿을 수 없다.
    /// </summary>
    public class LoadSaveManager : MonoBehaviour
    {
        [Serializable]
        private class SaveData
        {
            public float posX;
            public float posY;
            public float elapsed;
        }

        [Serializable]
        private class ProgressData
        {
            public bool cleared;
        }

        [Tooltip("비워두면 런타임에 씬에서 찾는다.")]
        [SerializeField] private SlimeController slime;

        private static LoadSaveManager instance;

        // Domain Reload off 면 지난 세션의 fake-null 참조가 남아 SaveNow 가 파괴된 인스턴스를 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private static string SavePath => Path.Combine(Application.persistentDataPath, "slimesave.json");
        private static string TrailRoot => Path.Combine(Application.persistentDataPath, "trail");
        private static string ProgressPath => Path.Combine(Application.persistentDataPath, "progress.json");

        public static bool HasSave => File.Exists(SavePath);

        /// 1회 클리어 여부. 세이브와 달리 New Game·엔딩 삭제 대상이 아니라 계정에 영구히 남는다.
        public static bool HasCleared
        {
            get
            {
                try
                {
                    if (!File.Exists(ProgressPath)) return false;
                    var data = JsonUtility.FromJson<ProgressData>(File.ReadAllText(ProgressPath));
                    return data != null && data.cleared;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[LoadSave] Progress unreadable: " + e.Message);
                    return false;
                }
            }
        }

        public static void MarkCleared()
        {
            try
            {
                File.WriteAllText(ProgressPath, JsonUtility.ToJson(new ProgressData { cleared = true }, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoadSave] Progress save failed: " + e.Message);
            }
        }

        public static void SaveNow()
        {
            if (instance != null) instance.WriteSave();
        }

        /// Start New Game 과 엔딩 진입용. Trail 은 파일이 남아 있으면 TryLoad 가 되살리므로 하나도 남기지 않는다.
        public static void DeleteSave()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning("[LoadSave] Save delete failed: " + e.Message); }

            if (!Directory.Exists(TrailRoot)) return;

            // 파일 단위로 지운다 — 통짜 재귀 삭제는 하나만 잠겨도 전부 남기고 이유도 안 알려준다.
            foreach (string path in Directory.GetFiles(TrailRoot, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(path); }
                catch (Exception e) { Debug.LogError("[LoadSave] Trail file survived New Game: " + path + " — " + e.Message); }
            }
            try { Directory.Delete(TrailRoot, true); } catch { /* 남은 파일은 위에서 이미 보고됐다 */ }
        }

        private void Awake() => instance = this;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void OnApplicationQuit() => WriteSave();

        private void Update() => RunClock.Tick(Time.unscaledDeltaTime);

        private void Start()
        {
            // 씬 로드는 statics 를 안 지운다 — 되돌리지 않으면 지난 판 기록이 이어진다.
            RunClock.Restart(0f);
            if (!GameFlow.ConsumePendingLoad()) return;
            if (!ResolveSlime()) return;
            if (!File.Exists(SavePath)) return;

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoadSave] Save unreadable: " + e.Message);
                return;
            }
            if (data == null) return;

            RunClock.Restart(data.elapsed);
            Vector2 before = slime.Position;
            slime.TeleportTo(new Vector2(data.posX, data.posY), Vector2.zero);
            // 카메라도 즉시 워프 — 없으면 씬 저장 위치에서 복원 지점까지 댐핑 팬이 도중 존들을 지난다.
            CinemachineCore.OnTargetObjectWarped(slime.transform, (Vector3)(slime.Position - before));
        }

        private void WriteSave()
        {
            if (!ResolveSlime()) return;
            Vector2 pos = slime.Position;
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(
                    new SaveData { posX = pos.x, posY = pos.y, elapsed = RunClock.Elapsed }));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoadSave] Save failed: " + e.Message);
            }
        }

        private bool ResolveSlime()
        {
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            return slime != null;
        }
    }
}
