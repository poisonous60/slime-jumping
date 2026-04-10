using System;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    public class CrumblingPlatform : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds after first attach before crumbling starts")]
        [SerializeField] float triggerDelay = 0.5f;
        [Tooltip("Visual crumble effect duration")]
        [SerializeField] float crumbleDuration = 0.2f;
        [Tooltip("Seconds before respawn. Negative = permanent (one-shot)")]
        [SerializeField] float respawnTime = 3.0f;

        [Header("Angle Filter")]
        [SerializeField] bool useAngleLimit;
        [Tooltip("0 = floor, 90 = wall, 180 = ceiling")]
        [SerializeField] float minSurfaceAngle;
        [SerializeField] float maxSurfaceAngle = 180f;

        [Header("Visuals")]
        [SerializeField] Transform visualRoot;
        [SerializeField] Renderer visualRenderer;
        [Tooltip("Maximum shake displacement")]
        [SerializeField] float maxShakeOffset = 0.05f;

        [Header("Respawn")]
        [Tooltip("Layers that block respawn when overlapping")]
        [SerializeField] LayerMask blockingLayers = ~0;

        enum CrumbleState { Idle, Triggered, Crumbling, Gone }

        CrumbleState state = CrumbleState.Idle;
        float timer;
        bool readyToRespawn;

        bool IsOneShot => respawnTime < 0f;

        SlimeController slime;
        Collider2D[] myColliders;
        Collider2D[] overlapResults = new Collider2D[4];

        MaterialPropertyBlock mpb;
        Color originalColor;
        static readonly int ColorID = Shader.PropertyToID("_Color");

        void Awake()
        {
            myColliders = GetComponents<Collider2D>();
            mpb = new MaterialPropertyBlock();
            if (visualRenderer != null)
            {
                visualRenderer.GetPropertyBlock(mpb);
                originalColor = mpb.GetColor(ColorID);
                if (originalColor.a == 0f)
                    originalColor = Color.white;
            }
        }

        void FixedUpdate()
        {
            if (slime == null)
            {
                slime = FindAnyObjectByType<SlimeController>();
                if (slime == null) return;
            }

            float dt = Time.fixedDeltaTime;

            switch (state)
            {
                case CrumbleState.Idle:
                    UpdateIdle();
                    break;
                case CrumbleState.Triggered:
                    UpdateTriggered(dt);
                    break;
                case CrumbleState.Crumbling:
                    UpdateCrumbling(dt);
                    break;
                case CrumbleState.Gone:
                    UpdateGone(dt);
                    break;
            }
        }

        // --- State Updates ---

        void UpdateIdle()
        {
            if (slime.CurrentState != SlimeController.SlimeState.Attached) return;
            if (Array.IndexOf(myColliders, slime.SurfaceCollider) < 0) return;

            if (useAngleLimit)
            {
                float angle = Mathf.Acos(Vector2.Dot(slime.SurfaceNormal, slime.GravityUp)) * Mathf.Rad2Deg;
                if (angle < minSurfaceAngle || angle > maxSurfaceAngle) return;
            }

            SetState(CrumbleState.Triggered);
        }

        void UpdateTriggered(float dt)
        {
            timer -= dt;
            if (timer <= 0f)
            {
                SetState(CrumbleState.Crumbling);
                return;
            }

            // Visual: color lerp + mild shake (ramps up to 40% of max)
            float t = 1f - timer / triggerDelay;
            ApplyVisual(Color.Lerp(originalColor, Color.red, t * 0.6f), 1f, maxShakeOffset * 0.4f * t);
        }

        void UpdateCrumbling(float dt)
        {
            timer -= dt;
            if (timer <= 0f)
            {
                SetState(CrumbleState.Gone);
                return;
            }

            // Visual: stronger shake (capped at max) + alpha fade
            float t = 1f - timer / crumbleDuration;
            ApplyVisual(Color.Lerp(Color.red, new Color(1f, 0f, 0f, 0f), t), 1f - t, maxShakeOffset);
        }

        void UpdateGone(float dt)
        {
            if (IsOneShot) return; // enabled = false set in SetState

            if (!readyToRespawn)
            {
                timer -= dt;
                if (timer <= 0f)
                    readyToRespawn = true;
                return;
            }

            // Check for overlap before respawning
            foreach (var col in myColliders) col.enabled = true;
            bool blocked = false;
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = blockingLayers,
                useTriggers = false
            };
            foreach (var col in myColliders)
            {
                if (col.Overlap(filter, overlapResults) > 0)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                foreach (var col in myColliders) col.enabled = false;
                return;
            }

            // Clear — colliders already enabled, proceed to Idle
            SetState(CrumbleState.Idle);
        }

        // --- State Transitions ---

        void SetState(CrumbleState newState)
        {
            state = newState;

            switch (newState)
            {
                case CrumbleState.Idle:
                    readyToRespawn = false;
                    if (visualRenderer != null) visualRenderer.enabled = true;
                    if (visualRoot != null) visualRoot.localPosition = Vector3.zero;
                    ApplyVisual(originalColor, 1f, 0f);
                    break;

                case CrumbleState.Triggered:
                    timer = triggerDelay;
                    if (triggerDelay <= 0f)
                    {
                        SetState(CrumbleState.Crumbling);
                        return;
                    }
                    break;

                case CrumbleState.Crumbling:
                    timer = crumbleDuration;
                    if (crumbleDuration <= 0f)
                    {
                        SetState(CrumbleState.Gone);
                        return;
                    }
                    break;

                case CrumbleState.Gone:
                    foreach (var col in myColliders) col.enabled = false;
                    if (visualRenderer != null) visualRenderer.enabled = false;
                    if (visualRoot != null) visualRoot.localPosition = Vector3.zero;
                    readyToRespawn = false;
                    timer = respawnTime;
                    if (IsOneShot)
                        enabled = false; // skip FixedUpdate; state remains Gone
                    break;
            }
        }

        // --- Visuals ---

        void ApplyVisual(Color color, float alpha, float shakeMagnitude)
        {
            if (visualRenderer != null)
            {
                color.a = alpha;
                mpb.SetColor(ColorID, color);
                visualRenderer.SetPropertyBlock(mpb);
            }

            if (visualRoot != null && shakeMagnitude > 0f)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * shakeMagnitude;
                visualRoot.localPosition = new Vector3(offset.x, offset.y, 0f);
            }
            else if (visualRoot != null)
            {
                visualRoot.localPosition = Vector3.zero;
            }
        }

        // --- Editor Helpers ---

        void Reset()
        {
            visualRoot = transform.Find("Visual");
            if (visualRoot != null)
                visualRenderer = visualRoot.GetComponent<Renderer>();
        }
    }
}
