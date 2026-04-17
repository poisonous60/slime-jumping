using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlimeJumping.Rendering
{
    /// Orchestrates real-time save/load for every registered ITrailTarget.
    /// Single-flight AsyncGPUReadback + worker-thread atomic file write, so the
    /// main thread never stalls during gameplay. See docs-md-peaceful-breeze.md
    /// "Persistence" section.
    public class TrailPersistence
    {
        public const int SaveVersion = 1;
        public const string Format = "R8";

        private readonly float autoSaveInterval;
        private readonly string root;             // Application.persistentDataPath + "/trail"
        private readonly List<ITrailTarget> targets = new List<ITrailTarget>();

        private float timer;
        private bool saveInFlight;
        private bool cancelled;
        private int rrIndex;

        public TrailPersistence(float autoSaveInterval)
        {
            this.autoSaveInterval = Mathf.Max(1f, autoSaveInterval);
            this.root = Path.Combine(Application.persistentDataPath, "trail");
        }

        public string RootPath => root;

        public void Register(ITrailTarget target)
        {
            if (target == null || targets.Contains(target)) return;
            targets.Add(target);
        }

        public void Unregister(ITrailTarget target)
        {
            if (target == null) return;
            targets.Remove(target);
        }

        public void Cancel() => cancelled = true;

        public void Tick()
        {
            if (cancelled || saveInFlight) return;
            timer += Time.unscaledDeltaTime;
            if (timer < autoSaveInterval) return;

            for (int i = 0; i < targets.Count; i++)
            {
                int idx = (rrIndex + i) % targets.Count;
                if (targets[idx].Dirty) { StartSave(targets[idx]); rrIndex = (idx + 1) % targets.Count; timer = 0f; return; }
            }
            // Nothing dirty — reset the timer so we check again next interval.
            timer = 0f;
        }

        // ------------------------------------------------------------------ load

        public void TryLoad(ITrailTarget t)
        {
            if (t == null) return;
            string datPath = PathFor(t.SaveKey) + ".dat";
            string metaPath = PathFor(t.SaveKey) + ".meta";
            if (!File.Exists(datPath) || !File.Exists(metaPath)) return;

            TrailTargetMeta saved;
            try
            {
                saved = JsonUtility.FromJson<TrailTargetMeta>(File.ReadAllText(metaPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Trail] Failed to parse save meta for '{t.SaveKey}': {e.Message}. Starting fresh.");
                return;
            }

            var expected = t.BuildMeta();
            if (!MetaMatches(saved, expected))
            {
                Debug.LogWarning($"[Trail] Save metadata mismatch for '{t.SaveKey}'. Starting fresh. " +
                                 $"(saved {saved.resolutionX}x{saved.resolutionY} @ ({saved.originX},{saved.originY}) size ({saved.sizeX},{saved.sizeY}) " +
                                 $"vs expected {expected.resolutionX}x{expected.resolutionY} @ ({expected.originX},{expected.originY}) size ({expected.sizeX},{expected.sizeY}))");
                return;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(datPath); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Trail] Failed to read save data for '{t.SaveKey}': {e.Message}. Starting fresh.");
                return;
            }

            int expectedBytes = expected.resolutionX * expected.resolutionY;
            if (bytes.Length != expectedBytes)
            {
                Debug.LogWarning($"[Trail] Save data size mismatch for '{t.SaveKey}' ({bytes.Length} vs {expectedBytes}). Starting fresh.");
                return;
            }

            var tex = new Texture2D(expected.resolutionX, expected.resolutionY, TextureFormat.R8, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.LoadRawTextureData(bytes);
            tex.Apply(false, true);
            Graphics.Blit(tex, t.Rt);
            UnityEngine.Object.Destroy(tex);

            t.OnLoadedInto();
            t.ClearDirty();
        }

        // --------------------------------------------------------------- snapshots

        public void SaveNamedSlotSync(ITrailTarget t, string slot)
        {
            if (t == null || string.IsNullOrEmpty(slot)) return;
            string path = Path.Combine(root, "snapshots", Sanitize(t.SaveKey) + "__" + Sanitize(slot));
            byte[] bytes = ReadPixelsSync(t);
            string meta = JsonUtility.ToJson(t.BuildMeta());
            WriteAtomic(path, bytes, meta);
        }

        public bool LoadNamedSlotSync(ITrailTarget t, string slot)
        {
            if (t == null || string.IsNullOrEmpty(slot)) return false;
            string path = Path.Combine(root, "snapshots", Sanitize(t.SaveKey) + "__" + Sanitize(slot));
            if (!File.Exists(path + ".dat") || !File.Exists(path + ".meta")) return false;

            TrailTargetMeta saved;
            try { saved = JsonUtility.FromJson<TrailTargetMeta>(File.ReadAllText(path + ".meta")); }
            catch { return false; }

            if (!MetaMatches(saved, t.BuildMeta())) return false;

            byte[] bytes = File.ReadAllBytes(path + ".dat");
            var tex = new Texture2D(saved.resolutionX, saved.resolutionY, TextureFormat.R8, false, true);
            tex.LoadRawTextureData(bytes);
            tex.Apply(false, true);
            Graphics.Blit(tex, t.Rt);
            UnityEngine.Object.Destroy(tex);

            t.MarkDirty();  // loaded content should replace whatever is on disk next auto-save
            return true;
        }

        // ----------------------------------------------------------------- save path

        private void StartSave(ITrailTarget t)
        {
            saveInFlight = true;
            t.ClearDirty();
            // Capture target & key locally — target may unregister during readback.
            var capturedTarget = t;
            var capturedMeta = JsonUtility.ToJson(t.BuildMeta());
            var capturedPath = PathFor(t.SaveKey);
            AsyncGPUReadback.Request(t.Rt, 0, TextureFormat.R8, req => OnReadback(req, capturedMeta, capturedPath));
        }

        private void OnReadback(AsyncGPUReadbackRequest req, string metaJson, string basePath)
        {
            try
            {
                if (cancelled) return;
                if (req.hasError) { Debug.LogWarning("[Trail] Async readback reported error."); return; }

                // Must copy out of the NativeArray before it is disposed next frame.
                byte[] bytes = req.GetData<byte>().ToArray();
                Task.Run(() =>
                {
                    try { WriteAtomic(basePath, bytes, metaJson); }
                    catch (Exception e) { Debug.LogWarning("[Trail] Worker-thread save failed: " + e.Message); }
                });
            }
            finally { saveInFlight = false; }
        }

        public void FlushSync()
        {
            if (cancelled) return;
            // Drain any in-flight readbacks so their callbacks don't run post-shutdown on a torn-down state.
            AsyncGPUReadback.WaitAllRequests();

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (!t.Dirty) continue;
                try
                {
                    t.ClearDirty();
                    byte[] bytes = ReadPixelsSync(t);
                    string meta = JsonUtility.ToJson(t.BuildMeta());
                    WriteAtomic(PathFor(t.SaveKey), bytes, meta);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Trail] Sync flush failed for '{t.SaveKey}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static byte[] ReadPixelsSync(ITrailTarget t)
        {
            var tex = new Texture2D(t.Resolution.x, t.Resolution.y, TextureFormat.R8, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = t.Rt;
            tex.ReadPixels(new Rect(0, 0, t.Resolution.x, t.Resolution.y), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            byte[] bytes = tex.GetRawTextureData();
            UnityEngine.Object.Destroy(tex);
            return bytes;
        }

        private static void WriteAtomic(string basePath, byte[] bytes, string metaJson)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(basePath));
            string datTmp  = basePath + ".dat.tmp";
            string metaTmp = basePath + ".meta.tmp";
            string datFinal  = basePath + ".dat";
            string metaFinal = basePath + ".meta";

            File.WriteAllBytes(datTmp, bytes);
            File.WriteAllText (metaTmp, metaJson);

            // Rename data first, then meta. If the process dies between them, the next load
            // sees "data present, meta missing" and treats it as "start fresh" (safer than
            // loading data with assumed defaults).
            if (File.Exists(datFinal)) File.Delete(datFinal);
            File.Move(datTmp, datFinal);
            if (File.Exists(metaFinal)) File.Delete(metaFinal);
            File.Move(metaTmp, metaFinal);
        }

        private static bool MetaMatches(TrailTargetMeta a, TrailTargetMeta b)
        {
            return a.version == b.version
                && a.resolutionX == b.resolutionX
                && a.resolutionY == b.resolutionY
                && Mathf.Approximately(a.originX, b.originX)
                && Mathf.Approximately(a.originY, b.originY)
                && Mathf.Approximately(a.sizeX, b.sizeX)
                && Mathf.Approximately(a.sizeY, b.sizeY)
                && a.format == b.format;
        }

        private string PathFor(string saveKey) => Path.Combine(root, Sanitize(saveKey));

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var buf = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') buf.Append(c);
                else buf.Append('_');
            }
            // Keep path components bounded.
            const int maxLen = 120;
            if (buf.Length > maxLen) buf.Length = maxLen;
            return buf.ToString();
        }
    }
}
