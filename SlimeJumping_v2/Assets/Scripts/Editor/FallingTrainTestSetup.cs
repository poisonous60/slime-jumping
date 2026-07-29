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
            // Must clear selection BEFORE destroying, else inspectors pointing at the old
            // root's children throw SerializedObjectNotCreatableException for a frame.
            Selection.activeObject = null;

            var existing = GameObject.Find("FallingTrainTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("FallingTrainTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Falling Train Test");

            var train = new GameObject("Train_Carriage");
            train.transform.SetParent(root.transform, false);
            train.transform.position = new Vector3(0f, 4f, 0f);

            var rb = train.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic; // Becomes Dynamic at fall start.
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var solid = train.AddComponent<BoxCollider2D>();
            solid.size = new Vector2(6f, 1.4f);

            // Placeholder — replace with the imported FBX.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            DestroyAnyColliders(visual);
            visual.transform.SetParent(train.transform, false);
            visual.transform.localScale = new Vector3(6f, 1.4f, 1.4f);

            var sensorGo = new GameObject("RiderSensor");
            sensorGo.transform.SetParent(train.transform, false);
            sensorGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var sensorCol = sensorGo.AddComponent<BoxCollider2D>();
            sensorCol.isTrigger = true;
            sensorCol.size = new Vector2(6f, 2f);
            var sensor = sensorGo.AddComponent<PlatformRiderSensor>();

            var leftConn = new GameObject("Connector_Left");
            leftConn.transform.SetParent(train.transform, false);
            leftConn.transform.localPosition = new Vector3(-3f, 0f, 0f);

            var rightConn = new GameObject("Connector_Right");
            rightConn.transform.SetParent(train.transform, false);
            rightConn.transform.localPosition = new Vector3(3f, 0f, 0f);

            var creakSrc = train.AddComponent<AudioSource>();
            creakSrc.playOnAwake = false;
            creakSrc.loop = true;
            creakSrc.spatialBlend = 1f;

            var snapImpulseGo = new GameObject("SnapImpulse");
            snapImpulseGo.transform.SetParent(train.transform, false);
            snapImpulseGo.AddComponent<CinemachineImpulseSource>();
            var snapImpulse = snapImpulseGo.AddComponent<ImpulseEmitter>();

            var impactImpulseGo = new GameObject("ImpactImpulse");
            impactImpulseGo.transform.SetParent(train.transform, false);
            impactImpulseGo.AddComponent<CinemachineImpulseSource>();
            var impactImpulse = impactImpulseGo.AddComponent<ImpulseEmitter>();

            // Placeholder debris — swap for a real prefab.
            var debrisTemplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debrisTemplate.name = "Debris_Template";
            DestroyAnyColliders(debrisTemplate);
            debrisTemplate.transform.localScale = Vector3.one * 0.25f;
            debrisTemplate.AddComponent<BoxCollider2D>();
            var debrisRb = debrisTemplate.AddComponent<Rigidbody2D>();
            debrisRb.gravityScale = 1.5f;
            debrisTemplate.SetActive(false);
            debrisTemplate.transform.SetParent(root.transform, false);

            // Non-zero bounciness is intentional: the post-impact micro-tremble is killed by
            // FallingTrainPlatform's rest-snap, not by zeroing bounciness here.
            var heavyMat = new PhysicsMaterial2D("Train_Heavy")
            {
                friction = 0.7f,
                bounciness = 0.1f
            };

            // Placeholder — delete or move once real terrain exists.
            var ground = new GameObject("PlaceholderGround");
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(0f, -3.5f, 0f);
            var groundCol = ground.AddComponent<BoxCollider2D>();
            groundCol.size = new Vector2(20f, 1f);

            var falling = train.AddComponent<FallingTrainPlatform>();
            var so = new SerializedObject(falling);
            so.FindProperty("riderSensor").objectReferenceValue = sensor;
            so.FindProperty("creakSource").objectReferenceValue = creakSrc;
            so.FindProperty("snapImpulse").objectReferenceValue = snapImpulse;
            so.FindProperty("impactImpulse").objectReferenceValue = impactImpulse;
            so.FindProperty("debrisPrefab").objectReferenceValue = debrisTemplate;
            so.FindProperty("fallPhysicsMaterial").objectReferenceValue = heavyMat;

            var connectorsProp = so.FindProperty("connectors");
            connectorsProp.arraySize = 2;
            ConfigConnector(connectorsProp.GetArrayElementAtIndex(0), leftConn.transform,  preDelay: 0.6f, sagDeg: 5f, sagDur: 0.45f, jolt: 0.35f);
            ConfigConnector(connectorsProp.GetArrayElementAtIndex(1), rightConn.transform, preDelay: 1.0f, sagDeg: 7f, sagDur: 0.30f, jolt: 0.55f);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(falling);

            Selection.activeGameObject = train;
            Debug.Log("[FallingTrainTestSetup] Created at (0,4) above placeholder ground at (0,-3.5). " +
                      "Schedule: Connector_Left snaps at 0.6s (sag 5°), Connector_Right at 1.05s (sag 7°), " +
                      "0.2s silence, then physics-driven fall. " +
                      "Iteration: adjust Train_Carriage.transform.position to set the START — physics determines the END. " +
                      "Use ContextMenu 'DEBUG: Trigger Fall Now' on the FallingTrainPlatform component to skip telegraph. " +
                      "Set 'groundLayers' inspector field to exclude the slime layer if you have one. " +
                      "Assign creakSource.clip, per-connector snapSound, crackSound/groanSound/dustSettleSound, " +
                      "and snap/impact VFX prefabs.");
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
