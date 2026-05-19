using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.Audio;
using SlimeJumping.Interaction;
using SlimeJumping.UI.Animation;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.UI.Panels
{
    /// Shared modal panel that shows the track list for whichever gramophone
    /// the player just interacted with. Rebuilt per-open from the gramophone's
    /// Playlist — one panel instance serves every gramophone in the scene.
    /// Applies the player's choice in OnAfterClose so the visual fade finishes
    /// before the disc animation / BGM crossfade kicks in.
    public class GramophonePanel : UIPanel
    {
        [Header("List")]
        [SerializeField] private RectTransform trackListContent;
        [SerializeField] private GameObject trackRowTemplate;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI subtitleLabel;

        [Header("Actions")]
        [SerializeField] private Button stopButton;
        [SerializeField] private Button cancelButton;

        [Header("Row Animation")]
        [SerializeField] private UIContainerStagger rowStagger;

        private MediaInteractable_PhonographV2 source;
        private readonly List<GameObject> spawnedRows = new List<GameObject>();

        // Deferred action applied at OnAfterClose so the panel can fade out
        // before the gramophone animates. -1 = none, -2 = stop, >=0 = track index.
        private const int PendingNone = -1;
        private const int PendingStop = -2;
        private int pendingAction = PendingNone;

        protected override void OnAfterAwake()
        {
            // Resolve via slot tags when setup script wires by tag instead of ref.
            if (trackListContent == null)
            {
                var go = FindSlotGameObject(UISlotType.TrackListContent);
                if (go != null) trackListContent = go.GetComponent<RectTransform>();
            }
            if (trackRowTemplate == null) trackRowTemplate = FindSlotGameObject(UISlotType.TrackRowTemplate);
            if (titleLabel == null) titleLabel = FindSlot<TextMeshProUGUI>(UISlotType.Title);
            if (subtitleLabel == null) subtitleLabel = FindSlot<TextMeshProUGUI>(UISlotType.Subtitle);
            if (stopButton == null) stopButton = FindSlot<Button>(UISlotType.StopButton);
            if (cancelButton == null) cancelButton = FindSlot<Button>(UISlotType.CancelButton);

            if (trackRowTemplate != null) trackRowTemplate.SetActive(false);

            if (stopButton != null) stopButton.onClick.AddListener(HandleStop);
            if (cancelButton != null) cancelButton.onClick.AddListener(HandleCancel);
        }

        /// Entry point from a gramophone's OnInteract.
        public void OpenFor(MediaInteractable_PhonographV2 gramophone)
        {
            if (gramophone == null) return;
            source = gramophone;
            pendingAction = PendingNone;
            // Open first so the hierarchy is active before we instantiate rows —
            // TextMeshProUGUI generated under an inactive parent skips its first
            // mesh build and renders blank even after we set .text.
            if (UIManager.Instance != null) UIManager.Instance.Open(this);
            else Open();
        }

        protected override void OnAfterOpen()
        {
            RebuildList();
        }

        protected override void OnAfterClose()
        {
            // Fire the deferred decision after the panel starts fading out so
            // the needle/disc tween and BGM crossfade overlap with the fade.
            var target = source;
            int action = pendingAction;
            source = null;
            pendingAction = PendingNone;
            if (target == null) return;

            if (action == PendingStop) target.StopMusic();
            else if (action >= 0) target.PlayTrack(action);
        }

        protected override void OnDestroy()
        {
            if (stopButton != null) stopButton.onClick.RemoveAllListeners();
            if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                var row = spawnedRows[i];
                if (row == null) continue;
                var btn = row.GetComponent<Button>();
                if (btn != null) btn.onClick.RemoveAllListeners();
            }
            base.OnDestroy();
        }

        private void RebuildList()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                var row = spawnedRows[i];
                if (row == null) continue;
                var btn = row.GetComponent<Button>();
                if (btn != null) btn.onClick.RemoveAllListeners();
                Destroy(row);
            }
            spawnedRows.Clear();

            if (titleLabel != null) titleLabel.text = "Phonograph";
            if (subtitleLabel != null) subtitleLabel.text = source != null && source.IsPlaying
                ? "Now playing — pick another or stop"
                : "Select a track";

            if (stopButton != null) stopButton.gameObject.SetActive(source != null && source.IsPlaying);

            if (trackListContent == null || trackRowTemplate == null || source == null) return;
            var playlist = source.Playlist;
            if (playlist == null) return;

            int count = playlist.Count;
            for (int i = 0; i < count; i++)
            {
                var entry = playlist.GetTrack(i);
                if (entry == null) continue;
                var rowGO = Instantiate(trackRowTemplate, trackListContent);
                rowGO.SetActive(true);
                rowGO.name = "Track_" + i;

                var label = rowGO.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                if (label != null)
                {
                    bool isCurrent = source.IsPlaying && source.CurrentTrackIndex == i;
                    string display = string.IsNullOrEmpty(entry.displayName)
                        ? (entry.clip != null ? entry.clip.name : "Track " + (i + 1))
                        : entry.displayName;
                    label.text = isCurrent ? "▶  " + display : display;
                }

                int captured = i;
                var button = rowGO.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => HandlePick(captured));
                }
                spawnedRows.Add(rowGO);
            }

            // Force the full layout chain to settle this frame. Rebuilding only
            // trackListContent leaves Scroll at its prefab-default sizeDelta
            // (100x100) on the first frame, so VLG would assign each row ~54px
            // width and TMP would mesh at that width. Walk up to the box (the
            // outer VerticalLayoutGroup that owns Scroll) so width propagates.
            var topRT = trackListContent as RectTransform;
            while (topRT != null && topRT.parent is RectTransform parentRT
                   && parentRT.GetComponent<LayoutGroup>() != null)
            {
                topRT = parentRT;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(topRT != null ? topRT : trackListContent);
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                var tmp = spawnedRows[i].GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                if (tmp != null) tmp.ForceMeshUpdate();
            }

            // Stagger the freshly spawned rows. The Box-level stagger fired by
            // UIPanel.Open() ran before these rows existed, so we trigger a
            // list-targeted pass here against the row spawns instead.
            if (rowStagger != null) rowStagger.PlayForList(spawnedRows);
        }

        private void HandlePick(int index)
        {
            pendingAction = index;
            CloseSelf();
        }

        private void HandleStop()
        {
            pendingAction = PendingStop;
            CloseSelf();
        }

        private void HandleCancel()
        {
            pendingAction = PendingNone;
            CloseSelf();
        }

        private void CloseSelf()
        {
            if (UIManager.Instance != null) UIManager.Instance.Close(this);
            else Close();
        }
    }
}
