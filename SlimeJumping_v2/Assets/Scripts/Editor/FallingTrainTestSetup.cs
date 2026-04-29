using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class FallingTrainTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Falling Train Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("FallingTrainTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("FallingTrainTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Falling Train Test");

            // Train carriage (placeholder cube — replace mesh+collider when 3D model arrives).
            var train = new GameObject("Train_Carriage");
            train.transform.SetParent(root.transform, false);
            train.transform.position = new Vector3(0f, 4f, 0f);

            var rb = train.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var solid = train.AddComponent<BoxCollider2D>();
            solid.size = new Vector2(6f, 1.4f);

            // Visual root — placeholder cube. Replace with imported FBX later.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            DestroyAnyColliders(visual);
            visual.transform.SetParent(train.transform, false);
            visual.transform.localScale = new Vector3(6f, 1.4f, 1.4f);

            // Rider sensor — sits above the carriage so the slime entering from above is detected.
            var sensorGo = new GameObject("RiderSensor");
            sensorGo.transform.SetParent(train.transform, false);
            sensorGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var sensorCol = sensorGo.AddComponent<BoxCollider2D>();
            sensorCol.isTrigger = true;
            sensorCol.size = new Vector2(6f, 2f);
            var sensor = sensorGo.AddComponent<PlatformRiderSensor>();

            // Land target (designer adjusts in scene).
            var landGo = new GameObject("LandTarget");
            landGo.transform.SetParent(root.transform, false);
            landGo.transform.position = new Vector3(2f, -3f, 0f);

            // Connector anchors — left and right couplings.
            var leftConn = new GameObject("Connector_Left");
            leftConn.transform.SetParent(train.transform, false);
            leftConn.transform.localPosition = new Vector3(-3f, 0f, 0f);

            var rightConn = new GameObject("Connector_Right");
            rightConn.transform.SetParent(train.transform, false);
            rightConn.transform.localPosition = new Vector3(3f, 0f, 0f);

            // Audio source for creak loop.
            var creakSrc = train.AddComponent<AudioSource>();
            creakSrc.playOnAwake = false;
            creakSrc.loop = true;
            creakSrc.spatialBlend = 1f;

            // Cinemachine impulses — share two sources: one for snaps, one for impact.
            var snapImpulseGo = new GameObject("SnapImpulse");
            snapImpulseGo.transform.SetParent(train.transform, false);
            snapImpulseGo.AddComponent<CinemachineImpulseSource>();
            var snapImpulse = snapImpulseGo.AddComponent<ImpulseEmitter>();

            var impactImpulseGo = new GameObject("ImpactImpulse");
            impactImpulseGo.transform.SetParent(train.transform, false);
            impactImpulseGo.AddComponent<CinemachineImpulseSource>();
            var impactImpulse = impactImpulseGo.AddComponent<ImpulseEmitter>();

            // Debris placeholder prefab — small dynamic cube. User can swap for a real prefab.
            var debrisTemplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debrisTemplate.name = "Debris_Template";
            DestroyAnyColliders(debrisTemplate);
            debrisTemplate.transform.localScale = Vector3.one * 0.25f;
            debrisTemplate.AddComponent<BoxCollider2D>();
            var debrisRb = debrisTemplate.AddComponent<Rigidbody2D>();
            debrisRb.gravityScale = 1.5f;
            debrisTemplate.SetActive(false);
            debrisTemplate.transform.SetParent(root.transform, false);

            // Configure the train script.
            var falling = train.AddComponent<FallingTrainPlatform>();
            var so = new SerializedObject(falling);
            so.FindProperty("riderSensor").objectReferenceValue = sensor;
            so.FindProperty("landTarget").objectReferenceValue = landGo.transform;
            so.FindProperty("creakSource").objectReferenceValue = creakSrc;
            so.FindProperty("snapImpulse").objectReferenceValue = snapImpulse;
            so.FindProperty("impactImpulse").objectReferenceValue = impactImpulse;
            so.FindProperty("debrisPrefab").objectReferenceValue = debrisTemplate;

            var connectorsProp = so.FindProperty("connectors");
            connectorsProp.arraySize = 2;
            ConfigConnector(connectorsProp.GetArrayElementAtIndex(0), leftConn.transform,  preDelay: 0.6f, sagDeg: 5f, sagDur: 0.45f, jolt: 0.35f);
            ConfigConnector(connectorsProp.GetArrayElementAtIndex(1), rightConn.transform, preDelay: 1.0f, sagDeg: 7f, sagDur: 0.30f, jolt: 0.55f);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(falling);

            Selection.activeGameObject = train;
            Debug.Log("[FallingTrainTestSetup] Created. Schedule: Connector_Left snaps at 0.6s (sag 5°), " +
                      "Connector_Right at 1.05s (sag 7°), 0.2s silence, then drop. " +
                      "Assign creakSource.clip, per-connector snapSound, crackSound/groanSound/dustSettleSound, " +
                      "snap/impact VFX prefabs in the inspector. landTarget at (2,-3) — drag to your shortcut spot.");
        }

        private static void ConfigConnector(SerializedProperty elem, Transform anchor, float preDelay, float sagDeg, float sagDur, float jolt)
        {
            elem.FindPropertyRelative("anchor").objectReferenceValue = anchor;
            elem.FindPropertyRelative("preDelay").floatValue = preDelay;
            elem.FindPropertyRelative("sagDegrees").floatValue = sagDeg;
            elem.FindPropertyRelative("sagDuration").floatValue = sagDur;
            elem.FindPropertyRelative("joltForce").floatValue = jolt;
        }

        private static void DestroyAnyColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Collider2D>(true))
                Object.DestroyImmediate(c);
        }
    }
}
