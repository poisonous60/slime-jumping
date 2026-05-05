using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class RopeTestSetup
    {
        const int GroundLayer = 7;
        const string RopeMaterialPath = "Assets/Prebs/Platform/Materials/RopeMaterial.mat";

        struct RopeTuning
        {
            public int segmentCount;
            public float restLengthMul;
            public int constraintIterations;
            public float damping;
            public float edgeRadius;
            public float nodeGravity;
            public float elasticFactor;
            public float riderInfluenceSpacing;
            public float riderForceMul;
            public float maxTensionEnter;
            public float maxTensionExit;
            public int colliderSubsample;
            public int visualSubdivisions;
            public float reboundSpeedPerMeter;
            public float maxReboundSpeed;
            public float previewRiderMass;
            public int previewSteps;
        }

        static RopeTuning Telephone() => new RopeTuning
        {
            segmentCount = 15,
            restLengthMul = 1.15f,
            constraintIterations = 12,
            damping = 0.98f,
            edgeRadius = 0f,
            nodeGravity = 0.3f,
            elasticFactor = 0f,
            riderInfluenceSpacing = 4f,
            riderForceMul = 30f,
            maxTensionEnter = 1.25f,
            maxTensionExit = 1.10f,
            colliderSubsample = 2,
            visualSubdivisions = 1,
            reboundSpeedPerMeter = 0f,
            maxReboundSpeed = 18f,
            previewRiderMass = 1f,
            previewSteps = 120,
        };

        static RopeTuning Elastic() => new RopeTuning
        {
            segmentCount = 15,
            restLengthMul = 0.90f,
            constraintIterations = 2,
            damping = 0.98f,
            edgeRadius = 0f,
            nodeGravity = 0f,
            elasticFactor = 0.4f,
            riderInfluenceSpacing = 4f,
            riderForceMul = 60f,
            maxTensionEnter = 1.25f,
            maxTensionExit = 1.10f,
            colliderSubsample = 2,
            visualSubdivisions = 3,
            reboundSpeedPerMeter = 0f,
            maxReboundSpeed = 18f,
            previewRiderMass = 1f,
            previewSteps = 120,
        };

        // v2 preset — rubbery hammock-like rope that visibly sags under slime weight
        // during Charging; release converts the sag into a launch-direction speed bonus.
        static RopeTuning RubberRebound(float reboundSpeedPerMeter) => new RopeTuning
        {
            segmentCount = 15,
            restLengthMul = 1.08f,
            constraintIterations = 3,
            damping = 0.98f,
            edgeRadius = 0f,
            nodeGravity = 0.3f,
            elasticFactor = 0.3f,
            riderInfluenceSpacing = 4f,
            riderForceMul = 50f,
            maxTensionEnter = 1.25f,
            maxTensionExit = 1.10f,
            colliderSubsample = 2,
            visualSubdivisions = 3,
            reboundSpeedPerMeter = reboundSpeedPerMeter,
            maxReboundSpeed = 18f,
            previewRiderMass = 1f,
            previewSteps = 120,
        };

        [MenuItem("SlimeJumping/Mechanism/Setup Rope Test (Stage 1)")]
        public static void SetupStage1()
        {
            var root = new GameObject("RopeTest_Stage1");
            Undo.RegisterCreatedObjectUndo(root, "Create Rope Test Stage 1");

            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(30f, 1f, 1f));

            CreateRope(root.transform, "Rope_Telephone",
                new Vector3(-5f, 3f, 0f), new Vector3(5f, 3f, 0f),
                Telephone(), withWindDebug: true);

            Finish(root, "Stage 1: Verlet sag only. Arrow keys on Rope_Telephone push middle nodes.");
        }

        [MenuItem("SlimeJumping/Mechanism/Setup Rope Test (All Stages)")]
        public static void SetupAllStages()
        {
            var root = new GameObject("RopeTest_AllStages");
            Undo.RegisterCreatedObjectUndo(root, "Create Rope Test All Stages");

            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(60f, 1f, 1f));
            CreatePlatform("LaunchPad_Left", root.transform,
                new Vector3(-20f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("LaunchPad_Right", root.transform,
                new Vector3(20f, -2f, 0f), new Vector3(3f, 1f, 1f));

            CreateRope(root.transform, "Rope_Telephone",
                new Vector3(-15f, 3f, 0f), new Vector3(-7f, 3f, 0f),
                Telephone(), withWindDebug: true);

            CreateRope(root.transform, "Rope_Elastic",
                new Vector3(-3f, 4f, 0f), new Vector3(3f, 4f, 0f),
                Elastic(), withWindDebug: false);

            CreateSwingingRope(root.transform, "Rope_Swinging",
                pendulumPivot: new Vector3(13f, 6f, 0f),
                ropeEndAnchor: new Vector3(17f, 4f, 0f),
                Telephone());

            CreateRope(root.transform, "Rope_Tunnel",
                new Vector3(7f, -3f, 0f), new Vector3(13f, -3f, 0f),
                Telephone(), withWindDebug: true);
            CreatePlatform("DropPlatform", root.transform,
                new Vector3(10f, 10f, 0f), new Vector3(4f, 0.5f, 1f));

            Finish(root,
                "All-stages rope scene:\n" +
                "  Rope_Telephone  — ride + sag (stages 2a/2b/3)\n" +
                "  Rope_Elastic    — charge/launch to bounce (stage 4)\n" +
                "  Rope_Swinging   — dynamic anchor follows pendulum (stage 2c)\n" +
                "  Rope_Tunnel     — drop onto from DropPlatform while shaking with arrows (stage 2d)\n" +
                "Each Rope's parameters are on the Body GameObject's Rope component — tune in inspector.");
        }

        // ===== RopeV2 (timing-bonus) tuning =====

        struct RopeV2Tuning
        {
            public int segmentCount;
            public float restLengthMul;
            public int constraintIterations;
            public float damping;
            public float edgeRadius;
            public float nodeGravity;
            public float elasticFactor;
            public float riderInfluenceSpacing;
            public float riderForceMul;
            public float impactGain;
            public float maxTensionEnter;
            public float maxTensionExit;
            public int colliderSubsample;
            public int visualSubdivisions;
            public AnimationCurve timingBonusCurve;
            public AnimationCurve chargeMultiplierCurve;
            public float maxReboundSpeed;
            public float previewRiderMass;
            public int previewSteps;
        }

        // Hammock-like simulation base for v2 timing ropes — same physics shape
        // as v1's RubberRebound preset so v1↔v2 visual comparison stays apples-to-apples.
        // Caller supplies the two AnimationCurves to vary the rebound mechanic per rope.
        static RopeV2Tuning RubberRebound_V2(AnimationCurve timing, AnimationCurve charge) => new RopeV2Tuning
        {
            segmentCount = 15,
            restLengthMul = 1.08f,
            constraintIterations = 3,
            damping = 0.98f,
            edgeRadius = 0f,
            nodeGravity = 0.3f,
            elasticFactor = 0.3f,
            riderInfluenceSpacing = 4f,
            // Weight-only sag stays shallow (steady-state slime weight); the deep
            // dip is produced by impactGain on the airborne→attach impulse.
            riderForceMul = 10f,
            impactGain = 1f,
            maxTensionEnter = 1.25f,
            maxTensionExit = 1.10f,
            colliderSubsample = 2,
            visualSubdivisions = 3,
            timingBonusCurve = timing,
            chargeMultiplierCurve = charge,
            maxReboundSpeed = 18f,
            previewRiderMass = 1f,
            previewSteps = 120,
        };

        [MenuItem("SlimeJumping/Mechanism/Setup Rope Test V2 (Timing)")]
        public static void SetupV2Timing()
        {
            var root = new GameObject("RopeTest_V2_Timing");
            Undo.RegisterCreatedObjectUndo(root, "Create Rope Test V2 Timing");

            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(60f, 1f, 1f));
            CreatePlatform("SpawnPad_TimingOff", root.transform,
                new Vector3(-16f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("SpawnPad_LinearTiming", root.transform,
                new Vector3(0f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("SpawnPad_ChargeGated", root.transform,
                new Vector3(16f, -2f, 0f), new Vector3(3f, 1f, 1f));

            const float anchorY = 3f;
            const float halfSpan = 3.5f;

            // Profile 1: timing curve flat 0 → no bonus regardless of timing.
            var timingOff = AnimationCurve.Constant(0f, 20f, 0f);
            var chargeFlat = AnimationCurve.Constant(0f, 1f, 1f);
            BuildRopeV2(root.transform, "Rope_V2_TimingOff",
                new Vector3(-16f - halfSpan, anchorY, 0f),
                new Vector3(-16f + halfSpan, anchorY, 0f),
                RubberRebound_V2(timingOff, chargeFlat));

            // Profile 2: linear timing × flat charge → pure timing rope (default profile).
            var timingLinear = AnimationCurve.Linear(0f, 0f, 20f, 20f);
            BuildRopeV2(root.transform, "Rope_V2_LinearTiming",
                new Vector3(-halfSpan, anchorY, 0f),
                new Vector3(halfSpan, anchorY, 0f),
                RubberRebound_V2(timingLinear, AnimationCurve.Constant(0f, 1f, 1f)));

            // Profile 3: linear timing × charge ramp (0,0)→(1,1.5) → must charge AND time.
            var chargeRamp = AnimationCurve.Linear(0f, 0f, 1f, 1.5f);
            BuildRopeV2(root.transform, "Rope_V2_ChargeGated",
                new Vector3(16f - halfSpan, anchorY, 0f),
                new Vector3(16f + halfSpan, anchorY, 0f),
                RubberRebound_V2(AnimationCurve.Linear(0f, 0f, 20f, 20f), chargeRamp));

            CreateHeightMarkers(root.transform, anchorY);

            Finish(root,
                "V2 Timing test:\n" +
                "  Rope_V2_TimingOff      — timingBonusCurve flat 0 (baseline, no bonus regardless of timing)\n" +
                "  Rope_V2_LinearTiming   — timing linear (0,0)→(20,20), charge flat 1.0 (timing-only rope)\n" +
                "  Rope_V2_ChargeGated    — timing linear, charge ramp (0,0)→(1,1.5) (must charge AND time)\n" +
                "\n" +
                "How to evaluate:\n" +
                "  1. Land on a rope. Charge + launch immediately (no pump) → proj~0, bonus near 0.\n" +
                "  2. Pump rope: charge-launch-reland to start oscillation. Time launch with the upswing.\n" +
                "     Compare peak heights against gray markers (y = +4,+6,+8,+10,+12 above rope).\n" +
                "  3. Edit timingBonusCurve / chargeMultiplierCurve on each Body's RopeV2 component to taste.\n" +
                "\n" +
                "All three ropes share identical sim physics — only the two AnimationCurves differ.");
        }

        static GameObject BuildRopeV2(Transform parent, string name,
            Vector3 anchorAPos, Vector3 anchorBPos, RopeV2Tuning tuning)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var anchorA = new GameObject("AnchorA");
            anchorA.transform.SetParent(root.transform, false);
            anchorA.transform.position = anchorAPos;

            var anchorB = new GameObject("AnchorB");
            anchorB.transform.SetParent(root.transform, false);
            anchorB.transform.position = anchorBPos;

            CreateAnchorMarker(anchorA.transform);
            CreateAnchorMarker(anchorB.transform);

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.layer = GroundLayer;

            var edge = body.AddComponent<EdgeCollider2D>();
            edge.edgeRadius = 0f;

            var lineGo = new GameObject("LineRenderer");
            lineGo.transform.SetParent(body.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.08f;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
            if (mat != null) line.sharedMaterial = mat;

            var rope = body.AddComponent<RopeV2>();
            var so = new SerializedObject(rope);
            so.FindProperty("anchorA").objectReferenceValue = anchorA.transform;
            so.FindProperty("anchorB").objectReferenceValue = anchorB.transform;
            so.FindProperty("edge").objectReferenceValue = edge;
            so.FindProperty("line").objectReferenceValue = line;
            so.FindProperty("segmentCount").intValue = tuning.segmentCount;
            so.FindProperty("restLengthMul").floatValue = tuning.restLengthMul;
            so.FindProperty("constraintIterations").intValue = tuning.constraintIterations;
            so.FindProperty("damping").floatValue = tuning.damping;
            so.FindProperty("edgeRadius").floatValue = tuning.edgeRadius;
            so.FindProperty("nodeGravity").floatValue = tuning.nodeGravity;
            so.FindProperty("elasticFactor").floatValue = tuning.elasticFactor;
            so.FindProperty("riderInfluenceSpacing").floatValue = tuning.riderInfluenceSpacing;
            so.FindProperty("riderForceMul").floatValue = tuning.riderForceMul;
            so.FindProperty("impactGain").floatValue = tuning.impactGain;
            so.FindProperty("maxTensionEnter").floatValue = tuning.maxTensionEnter;
            so.FindProperty("maxTensionExit").floatValue = tuning.maxTensionExit;
            so.FindProperty("colliderSubsample").intValue = tuning.colliderSubsample;
            so.FindProperty("visualSubdivisions").intValue = tuning.visualSubdivisions;
            so.FindProperty("timingBonusCurve").animationCurveValue = tuning.timingBonusCurve;
            so.FindProperty("chargeMultiplierCurve").animationCurveValue = tuning.chargeMultiplierCurve;
            so.FindProperty("maxReboundSpeed").floatValue = tuning.maxReboundSpeed;
            so.FindProperty("previewRiderMass").floatValue = tuning.previewRiderMass;
            so.FindProperty("previewSteps").intValue = tuning.previewSteps;
            so.ApplyModifiedProperties();

            rope.Rebuild();
            return root;
        }

        [MenuItem("SlimeJumping/Mechanism/Setup Rope Test V2 (Rebound)")]
        public static void SetupV2Rebound()
        {
            var root = new GameObject("RopeTest_V2_Rebound");
            Undo.RegisterCreatedObjectUndo(root, "Create Rope Test V2 Rebound");

            // Shared floor + spawn pads under each rope.
            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(60f, 1f, 1f));
            CreatePlatform("SpawnPad_NoRebound", root.transform,
                new Vector3(-16f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("SpawnPad_Medium", root.transform,
                new Vector3(0f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("SpawnPad_Strong", root.transform,
                new Vector3(16f, -2f, 0f), new Vector3(3f, 1f, 1f));

            // Three ropes, identical shape/tuning except reboundSpeedPerMeter.
            // Same anchor heights → same initial sag under slime weight → A/B/C
            // comparison isolates the rebound multiplier only.
            const float anchorY = 3f;
            const float halfSpan = 3.5f;

            CreateRope(root.transform, "Rope_NoRebound",
                new Vector3(-16f - halfSpan, anchorY, 0f),
                new Vector3(-16f + halfSpan, anchorY, 0f),
                RubberRebound(0f), withWindDebug: false);

            CreateRope(root.transform, "Rope_Medium(×6)",
                new Vector3(-halfSpan, anchorY, 0f),
                new Vector3(halfSpan, anchorY, 0f),
                RubberRebound(6f), withWindDebug: false);

            CreateRope(root.transform, "Rope_Strong(×14)",
                new Vector3(16f - halfSpan, anchorY, 0f),
                new Vector3(16f + halfSpan, anchorY, 0f),
                RubberRebound(14f), withWindDebug: false);

            // Height markers above the ropes for visual launch-height gauge.
            // Thin wide cubes, no collider. y relative to rope anchor (3m).
            CreateHeightMarkers(root.transform, anchorY);

            Finish(root,
                "V2 Rebound test:\n" +
                "  Rope_NoRebound     — reboundSpeedPerMeter = 0 (baseline, no bonus)\n" +
                "  Rope_Medium(×6)    — reboundSpeedPerMeter = 6\n" +
                "  Rope_Strong(×14)   — reboundSpeedPerMeter = 14\n" +
                "\n" +
                "How to evaluate:\n" +
                "  1. Select any Rope_*/Body → right-click Rope component → 'Preview Max Sag'.\n" +
                "     Magenta gizmo shows the stretched shape under previewRiderMass.\n" +
                "  2. Play mode: attach slime to rope midpoint, charge, release straight up.\n" +
                "     Compare peak heights against the gray marker lines (y = +4,+6,+8,+10,+12 above rope).\n" +
                "  3. Tune reboundSpeedPerMeter on each Body to taste.\n" +
                "\n" +
                "All three ropes share identical sag physics — only the rebound multiplier differs.");
        }

        // ===== RopeV3 (trampoline-coupled) tuning =====

        struct RopeV3Tuning
        {
            public int segmentCount;
            public float restLengthMul;
            public int constraintIterations;
            public float damping;
            public float edgeRadius;
            public float nodeGravity;
            public float elasticFactor;
            public float riderInfluenceSpacing;
            public float riderEquilibriumDepth;
            public float riderSpringStiffness;
            public float riderSpringDamping;
            public float impactCompressionFromVelocity;
            public float referenceMass;
            public float maxCarryPerStep;
            public float maxTensionEnter;
            public float maxTensionExit;
            public int colliderSubsample;
            public int visualSubdivisions;
            public AnimationCurve timingBonusCurve;
            public AnimationCurve chargeMultiplierCurve;
            public float maxReboundSpeed;
            public float previewRiderMass;
            public int previewSteps;
        }

        // V3 canonical trampoline preset. Same Verlet shape as v1/v2 RubberRebound
        // (segmentCount=15, restLengthMul=1.08, elasticFactor=0.3) so behavior
        // diff from v2 isolates to the spring-based rider channel.
        static RopeV3Tuning Trampoline_V3(AnimationCurve timing, AnimationCurve charge) => new RopeV3Tuning
        {
            segmentCount = 15,
            restLengthMul = 1.08f,
            constraintIterations = 3,
            damping = 0.98f,
            edgeRadius = 0f,
            nodeGravity = 0.3f,
            elasticFactor = 0.3f,
            riderInfluenceSpacing = 4f,
            riderEquilibriumDepth = 0.6f,
            riderSpringStiffness = 60f,
            riderSpringDamping = 0.3f,
            impactCompressionFromVelocity = 0.04f,
            referenceMass = 1f,
            maxCarryPerStep = 0.25f,
            maxTensionEnter = 1.25f,
            maxTensionExit = 1.10f,
            colliderSubsample = 2,
            visualSubdivisions = 3,
            timingBonusCurve = timing,
            chargeMultiplierCurve = charge,
            maxReboundSpeed = 18f,
            previewRiderMass = 1f,
            previewSteps = 120,
        };

        [MenuItem("SlimeJumping/Mechanism/Setup Rope Test V3 (Trampoline)")]
        public static void SetupV3Trampoline()
        {
            var root = new GameObject("RopeTest_V3_Trampoline");
            Undo.RegisterCreatedObjectUndo(root, "Create Rope Test V3 Trampoline");

            CreatePlatform("Floor", root.transform,
                new Vector3(0f, -6f, 0f), new Vector3(60f, 1f, 1f));
            CreatePlatform("SpawnPad_Soft", root.transform,
                new Vector3(-16f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("SpawnPad_Trampoline", root.transform,
                new Vector3(0f, -2f, 0f), new Vector3(3f, 1f, 1f));
            CreatePlatform("SpawnPad_HeavyHammock", root.transform,
                new Vector3(16f, -2f, 0f), new Vector3(3f, 1f, 1f));

            const float anchorY = 3f;
            const float halfSpan = 3.5f;

            var timingLinear = AnimationCurve.Linear(0f, 0f, 20f, 20f);
            var chargeFlat = AnimationCurve.Constant(0f, 1f, 1f);

            // Soft — shallow dip, near-critical damping. Quick settle, gentle feel.
            var soft = Trampoline_V3(timingLinear, chargeFlat);
            soft.riderEquilibriumDepth = 0.3f;
            soft.impactCompressionFromVelocity = 0.02f;
            soft.riderSpringDamping = 0.7f;
            BuildRopeV3(root.transform, "Rope_V3_Soft",
                new Vector3(-16f - halfSpan, anchorY, 0f),
                new Vector3(-16f + halfSpan, anchorY, 0f),
                soft);

            // Trampoline — V3 default. Deep dip + lively rebound.
            BuildRopeV3(root.transform, "Rope_V3_Trampoline",
                new Vector3(-halfSpan, anchorY, 0f),
                new Vector3(halfSpan, anchorY, 0f),
                Trampoline_V3(timingLinear, chargeFlat));

            // HeavyHammock — deep static sag, slow rebound. Shows steady-state-only feel.
            var hammock = Trampoline_V3(timingLinear, chargeFlat);
            hammock.riderEquilibriumDepth = 1.2f;
            hammock.riderSpringStiffness = 30f;
            hammock.riderSpringDamping = 0.5f;
            BuildRopeV3(root.transform, "Rope_V3_HeavyHammock",
                new Vector3(16f - halfSpan, anchorY, 0f),
                new Vector3(16f + halfSpan, anchorY, 0f),
                hammock);

            CreateHeightMarkers(root.transform, anchorY);

            Finish(root,
                "V3 Trampoline test (spring-coupled):\n" +
                "  Rope_V3_Soft         — equilibrium=0.3 m, damping=0.7 (shallow, quick settle)\n" +
                "  Rope_V3_Trampoline   — equilibrium=0.6 m, damping=0.3 (default, deep dip + bounce)\n" +
                "  Rope_V3_HeavyHammock — equilibrium=1.2 m, k=30, damping=0.5 (deep sag, slow)\n" +
                "\n" +
                "How to evaluate:\n" +
                "  1. Drop slime onto rope mid-span. The slime should stay attached the entire\n" +
                "     dip-and-rebound cycle (V2 detached here — that's the bug being fixed).\n" +
                "  2. Slime should visibly travel down with the rope into the dip, then ride\n" +
                "     the rebound back up past rest-line before settling.\n" +
                "  3. Charge + release Space at the rebound apex for a high jump (timing-bonus\n" +
                "     formula is unchanged from v2 — but slime feels coupled now).\n" +
                "  4. Tune riderEquilibriumDepth (m) directly in inspector for static sag depth;\n" +
                "     riderSpringDamping for rebound liveliness; impactCompressionFromVelocity\n" +
                "     for landing-dip-overshoot magnitude.");
        }

        static GameObject BuildRopeV3(Transform parent, string name,
            Vector3 anchorAPos, Vector3 anchorBPos, RopeV3Tuning tuning)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var anchorA = new GameObject("AnchorA");
            anchorA.transform.SetParent(root.transform, false);
            anchorA.transform.position = anchorAPos;

            var anchorB = new GameObject("AnchorB");
            anchorB.transform.SetParent(root.transform, false);
            anchorB.transform.position = anchorBPos;

            CreateAnchorMarker(anchorA.transform);
            CreateAnchorMarker(anchorB.transform);

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.layer = GroundLayer;

            // Force Multi probe mode — V3's carry channel assumes the teleport
            // branch zeroes rb.linearVelocity, so single-mode would double-count.
            var probeOverride = body.AddComponent<SlimeJumping.Physics.SurfaceProbeModeOverride>();
            probeOverride.mode = SlimeJumping.Physics.SurfaceProbeMode.Multi;

            var edge = body.AddComponent<EdgeCollider2D>();
            edge.edgeRadius = 0f;

            var lineGo = new GameObject("LineRenderer");
            lineGo.transform.SetParent(body.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.08f;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
            if (mat != null) line.sharedMaterial = mat;

            var rope = body.AddComponent<RopeV3>();
            var so = new SerializedObject(rope);
            so.FindProperty("anchorA").objectReferenceValue = anchorA.transform;
            so.FindProperty("anchorB").objectReferenceValue = anchorB.transform;
            so.FindProperty("edge").objectReferenceValue = edge;
            so.FindProperty("line").objectReferenceValue = line;
            so.FindProperty("segmentCount").intValue = tuning.segmentCount;
            so.FindProperty("restLengthMul").floatValue = tuning.restLengthMul;
            so.FindProperty("constraintIterations").intValue = tuning.constraintIterations;
            so.FindProperty("damping").floatValue = tuning.damping;
            so.FindProperty("edgeRadius").floatValue = tuning.edgeRadius;
            so.FindProperty("nodeGravity").floatValue = tuning.nodeGravity;
            so.FindProperty("elasticFactor").floatValue = tuning.elasticFactor;
            so.FindProperty("riderInfluenceSpacing").floatValue = tuning.riderInfluenceSpacing;
            so.FindProperty("riderEquilibriumDepth").floatValue = tuning.riderEquilibriumDepth;
            so.FindProperty("riderSpringStiffness").floatValue = tuning.riderSpringStiffness;
            so.FindProperty("riderSpringDamping").floatValue = tuning.riderSpringDamping;
            so.FindProperty("impactCompressionFromVelocity").floatValue = tuning.impactCompressionFromVelocity;
            so.FindProperty("referenceMass").floatValue = tuning.referenceMass;
            so.FindProperty("maxCarryPerStep").floatValue = tuning.maxCarryPerStep;
            so.FindProperty("maxTensionEnter").floatValue = tuning.maxTensionEnter;
            so.FindProperty("maxTensionExit").floatValue = tuning.maxTensionExit;
            so.FindProperty("colliderSubsample").intValue = tuning.colliderSubsample;
            so.FindProperty("visualSubdivisions").intValue = tuning.visualSubdivisions;
            so.FindProperty("timingBonusCurve").animationCurveValue = tuning.timingBonusCurve;
            so.FindProperty("chargeMultiplierCurve").animationCurveValue = tuning.chargeMultiplierCurve;
            so.FindProperty("maxReboundSpeed").floatValue = tuning.maxReboundSpeed;
            so.FindProperty("previewRiderMass").floatValue = tuning.previewRiderMass;
            so.FindProperty("previewSteps").intValue = tuning.previewSteps;
            so.ApplyModifiedProperties();

            rope.Rebuild();
            return root;
        }

        // Thin unlit horizontal bars (no collider) at set heights above the rope row,
        // so launch-peak heights can be eyeballed in play mode.
        static void CreateHeightMarkers(Transform parent, float baseY)
        {
            var group = new GameObject("HeightMarkers");
            group.transform.SetParent(parent, false);

            float[] offsets = { 4f, 6f, 8f, 10f, 12f };
            foreach (float dy in offsets)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = $"Mark_+{dy:F0}m";
                bar.transform.SetParent(group.transform, false);
                bar.transform.position = new Vector3(0f, baseY + dy, 0f);
                bar.transform.localScale = new Vector3(50f, 0.03f, 1f);
                Object.DestroyImmediate(bar.GetComponent<BoxCollider>());
                // Gray dim, lighter every other step so the eye can count.
                float shade = 0.3f + 0.08f * (dy / 2f - 1f);
                bar.GetComponent<MeshRenderer>().sharedMaterial =
                    MakeMaterial(new Color(shade, shade, shade));
            }
        }

        // ===== Rope factory =====

        static GameObject CreateRope(Transform parent, string name,
            Vector3 anchorAPos, Vector3 anchorBPos, RopeTuning tuning, bool withWindDebug)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var anchorA = new GameObject("AnchorA");
            anchorA.transform.SetParent(root.transform, false);
            anchorA.transform.position = anchorAPos;

            var anchorB = new GameObject("AnchorB");
            anchorB.transform.SetParent(root.transform, false);
            anchorB.transform.position = anchorBPos;

            CreateAnchorMarker(anchorA.transform);
            CreateAnchorMarker(anchorB.transform);

            BuildRopeBody(root.transform, anchorA.transform, anchorB.transform, tuning, withWindDebug);
            return root;
        }

        static GameObject CreateSwingingRope(Transform parent, string name,
            Vector3 pendulumPivot, Vector3 ropeEndAnchor, RopeTuning tuning)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var pendRoot = new GameObject("PendulumRoot");
            pendRoot.transform.SetParent(root.transform, false);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "PendulumBoard";
            board.transform.SetParent(pendRoot.transform, false);
            board.transform.position = pendulumPivot + new Vector3(0f, -3f, 0f);
            board.transform.localScale = new Vector3(3f, 0.3f, 1f);
            board.layer = GroundLayer;
            Object.DestroyImmediate(board.GetComponent<BoxCollider>());
            board.AddComponent<BoxCollider2D>().size = Vector2.one;

            var pend = board.AddComponent<PendulumPlatform>();
            var pendSo = new SerializedObject(pend);
            pendSo.FindProperty("localPivot").vector2Value = new Vector2(0f, 3f);
            pendSo.FindProperty("ropeLength").floatValue = 3f;
            pendSo.FindProperty("startAngleDeg").floatValue = 25f;
            pendSo.ApplyModifiedProperties();
            board.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.6f, 0.4f, 0.2f));

            var anchorA = new GameObject("AnchorA");
            anchorA.transform.SetParent(root.transform, false);
            anchorA.transform.position = ropeEndAnchor;
            CreateAnchorMarker(anchorA.transform);

            var anchorB = new GameObject("AnchorB");
            anchorB.transform.SetParent(board.transform, false);
            anchorB.transform.localPosition = new Vector3(-0.5f, 0.5f, 0f);
            CreateAnchorMarker(anchorB.transform);

            BuildRopeBody(root.transform, anchorA.transform, anchorB.transform, tuning, withWindDebug: false);
            return root;
        }

        static void BuildRopeBody(Transform parent, Transform anchorA, Transform anchorB,
            RopeTuning tuning, bool withWindDebug)
        {
            var body = new GameObject("Body");
            body.transform.SetParent(parent, false);
            body.layer = GroundLayer;

            var edge = body.AddComponent<EdgeCollider2D>();
            edge.edgeRadius = 0f;

            var lineGo = new GameObject("LineRenderer");
            lineGo.transform.SetParent(body.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.08f;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
            if (mat != null) line.sharedMaterial = mat;

            var rope = body.AddComponent<Rope>();
            var so = new SerializedObject(rope);
            so.FindProperty("anchorA").objectReferenceValue = anchorA;
            so.FindProperty("anchorB").objectReferenceValue = anchorB;
            so.FindProperty("edge").objectReferenceValue = edge;
            so.FindProperty("line").objectReferenceValue = line;
            so.FindProperty("segmentCount").intValue = tuning.segmentCount;
            so.FindProperty("restLengthMul").floatValue = tuning.restLengthMul;
            so.FindProperty("constraintIterations").intValue = tuning.constraintIterations;
            so.FindProperty("damping").floatValue = tuning.damping;
            so.FindProperty("edgeRadius").floatValue = tuning.edgeRadius;
            so.FindProperty("nodeGravity").floatValue = tuning.nodeGravity;
            so.FindProperty("elasticFactor").floatValue = tuning.elasticFactor;
            so.FindProperty("riderInfluenceSpacing").floatValue = tuning.riderInfluenceSpacing;
            so.FindProperty("riderForceMul").floatValue = tuning.riderForceMul;
            so.FindProperty("maxTensionEnter").floatValue = tuning.maxTensionEnter;
            so.FindProperty("maxTensionExit").floatValue = tuning.maxTensionExit;
            so.FindProperty("colliderSubsample").intValue = tuning.colliderSubsample;
            so.FindProperty("visualSubdivisions").intValue = tuning.visualSubdivisions;
            so.FindProperty("reboundSpeedPerMeter").floatValue = tuning.reboundSpeedPerMeter;
            so.FindProperty("maxReboundSpeed").floatValue = tuning.maxReboundSpeed;
            so.FindProperty("previewRiderMass").floatValue = tuning.previewRiderMass;
            so.FindProperty("previewSteps").intValue = tuning.previewSteps;
            so.ApplyModifiedProperties();

            rope.Rebuild();

            if (withWindDebug) body.AddComponent<RopeWindDebug>();
        }

        // ===== Helpers =====

        static GameObject CreateAnchorMarker(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Marker";
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.9f, 0.3f, 0.3f));
            return go;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = GroundLayer;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        static void Finish(GameObject root, string message)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("[RopeTest] " + message);
        }
    }
}
