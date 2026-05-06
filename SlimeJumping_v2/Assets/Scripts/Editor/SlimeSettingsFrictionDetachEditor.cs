// Custom inspector that adds a friction-detach analyzer at the bottom of the
// SlimeSettings inspector. Pure visualization — does not write back to the asset.
//
// Two views:
//   1. Critical-speed table: for several curvature radii, the velocity at which
//      detach starts firing on flat ground (|g·n| = gravity).
//   2. v × R heatmap: each pixel is one (v, R) sample. Red = detach region
//      (centrifugal admissible AND above minSpeed cutoff). Green = wrap region.
//
// Re-renders the heatmap only when the relevant settings change, so dragging
// other unrelated fields doesn't churn texture allocations.

using UnityEditor;
using UnityEngine;
using SlimeJumping.Settings;

[CustomEditor(typeof(SlimeSettings))]
public class SlimeSettingsFrictionDetachEditor : Editor
{
    private const int HeatmapWidth  = 320;
    private const int HeatmapHeight = 200;

    // v ∈ [0, vMax] m/s. R ∈ [rMin, rMax] m.
    private const float VMaxAxis = 25f;
    private const float RMinAxis = 0.1f;
    private const float RMaxAxis = 5f;

    private Texture2D heatmap;
    private float cachedAdhesion = float.NaN;
    private float cachedGravity = float.NaN;
    private float cachedMinSpeed = float.NaN;
    private FrictionDetachFormula cachedFormula = (FrictionDetachFormula)(-1);
    private float cachedVThreshold = float.NaN;
    private float cachedMinAngleDeg = float.NaN;
    private float cachedOmegaThreshold = float.NaN;
    private const float HeatmapDt = 0.02f;   // Δθ 게이트 평가용 가정 (FixedUpdate)

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var s = (SlimeSettings)target;
        if (!s.useFrictionBasedDetachment)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("useFrictionBasedDetachment 가 OFF — 새 detach 매커니즘 비활성. 켜면 아래 분석 표시.", MessageType.Info);
            return;
        }

        // Match runtime GravityVector (SlimeController.cs): magnitude only.
        // settings.gravity is stored as a negative scalar (-20 by default); without
        // Abs the heatmap's availableFlat collapses below availableWall and the
        // "wall-only detach" (orange) bucket becomes mathematically unreachable.
        float gravityAccel = Mathf.Abs(s.Gravity);
        float adhesion = s.cornerAdhesionAccel;
        float minSpeed = s.cornerDetachMinSpeed;
        float availableFlat = gravityAccel + adhesion;     // 평지 가정 |g·n| = gravity
        float availableWall = adhesion;                    // 수직벽 가정 |g·n| = 0

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Friction-Detach Analyzer", EditorStyles.boldLabel);

        // --- Inputs summary ---
        EditorGUILayout.LabelField($"adhesion = {adhesion:F1} m/s²");
        EditorGUILayout.LabelField($"|g| = {gravityAccel:F1} m/s²");
        EditorGUILayout.LabelField($"available (평지) = {availableFlat:F1} m/s²    (벽) = {availableWall:F1} m/s²");
        EditorGUILayout.LabelField($"minSpeed cutoff = {minSpeed:F2} m/s");
        EditorGUILayout.LabelField($"formula = {s.frictionDetachFormula}");

        EditorGUILayout.Space(6);

        // --- Critical-speed table (flat ground) ---
        // Each row reports the speed at which the slime first satisfies the active
        // formula on a uniform-curvature path of radius R (assumes ω = v/R).
        EditorGUILayout.LabelField("임계 속도 표 (평지, R = 곡면 반경, ω = v/R 가정)", EditorStyles.miniBoldLabel);
        float[] radii = { 0.3f, 0.5f, 1.0f, 2.0f, 3.0f, 5.0f };
        foreach (var R in radii)
        {
            float vFlat = ResolveCriticalSpeed(s, R, availableFlat);
            float vWall = ResolveCriticalSpeed(s, R, availableWall);
            string note = (vFlat > 0f && vFlat < minSpeed) ? "  (< minSpeed → 항상 wrap)" : "";
            string flatStr = float.IsPositiveInfinity(vFlat) ? "  ∞" : $"{vFlat,5:F2}";
            string wallStr = float.IsPositiveInfinity(vWall) ? "  ∞" : $"{vWall,5:F2}";
            EditorGUILayout.LabelField(
                $"R = {R,4:F2}m  →  평지 v ≥ {flatStr}  /  벽 v ≥ {wallStr} m/s{note}");
        }

        EditorGUILayout.Space(8);

        // --- v × R heatmap ---
        EditorGUILayout.LabelField("Detach 영역 맵 (평지 가정)", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"X = v ∈ [0, {VMaxAxis}] m/s   Y = R ∈ [{RMinAxis}, {RMaxAxis}] m   빨강 = detach, 초록 = wrap");

        EnsureHeatmap(s, adhesion, gravityAccel, minSpeed);

        // Layout: top axis label → heatmap → bottom axis label, each as its own
        // GetRect so the inspector flow accounts for all three. Earlier code drew
        // axis labels with absolute Rects above/below the heatmap rect (outside
        // the layout's allocated space), which collided with adjacent widgets.
        Rect topAxis = GUILayoutUtility.GetRect(HeatmapWidth, 14, GUILayout.ExpandWidth(false));
        topAxis.x += 4f;
        GUI.Label(topAxis, $"R = {RMaxAxis}m (위)   ↕   R = {RMinAxis}m (아래)        v ∈ [0, {VMaxAxis}] m/s",
            EditorStyles.miniLabel);

        Rect r = GUILayoutUtility.GetRect(HeatmapWidth, HeatmapHeight, GUILayout.ExpandWidth(false));
        r.x += 4f;
        EditorGUI.DrawRect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0.1f, 0.1f, 0.1f, 1f));
        GUI.DrawTexture(r, heatmap, ScaleMode.StretchToFill, false);

        Rect bottomAxis = GUILayoutUtility.GetRect(HeatmapWidth, 14, GUILayout.ExpandWidth(false));
        bottomAxis.x += 4f;
        GUI.Label(bottomAxis, $"0 m/s                                v →                                {VMaxAxis} m/s",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(8);

        // Legend showing the meaning of each color (esp. the surface-angle-dependent
        // "transition" band which is the visual distinction between formulas that
        // depend on `available` and formulas that ignore it).
        EditorGUILayout.LabelField("범례 (한 픽셀에 평지·벽 두 표면각 동시 평가)", EditorStyles.miniBoldLabel);
        DrawLegendRow(new Color(0.20f, 0.65f, 0.25f, 1f), "초록 — 평지·벽 모두 wrap (안 떨어짐)");
        DrawLegendRow(new Color(0.85f, 0.20f, 0.20f, 1f), "빨강 — 평지·벽 모두 detach");
        DrawLegendRow(new Color(0.95f, 0.55f, 0.10f, 1f), "주황 — 표면각에 따라 다름 (평지 wrap, 벽 detach). 표면각 의존 식의 흔적");
        DrawLegendRow(new Color(0.30f, 0.30f, 0.40f, 1f), "회색 — v < cornerDetachMinSpeed (검사 자체 skip)");

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "주황 영역의 유무가 두 모드의 차이를 보여줍니다:\n" +
            "• 주황 있음 (VelocityTimesOmega / ConstantTimesOmega / +MinAngle):\n" +
            "    available = gravityHelp + adhesion 이 식에 들어가서 표면각에 따라 임계가 변함.\n" +
            "    벽·천장에선 gravityHelp ≈ 0 이라 더 쉽게 detach.\n" +
            "• 주황 없음 (AngularRateThreshold):\n" +
            "    ω 단독 임계라 표면각 무관. 평지·벽 동일 임계.",
            MessageType.None);

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "튜닝 가이드:\n" +
            "• 빨강 영역이 너무 크다 (저속·완만한 곡면도 detach) → cornerAdhesionAccel ↑ 또는 omegaThreshold ↑\n" +
            "• 빠른 슬라임만 detach 시키고 싶다 → cornerDetachMinSpeed ↑ (왼쪽 영역 잘라냄)\n" +
            "• 속도 무관 각도 기반 → frictionDetachFormula = AngularRateThreshold (수평 빨강 띠. cornerOmegaThreshold 만 영향).\n" +
            "• 평지 sliding 중에도 detach 됨 → cornerDetachHorizonProbeSkin ↑ (별개 게이트)\n" +
            "• 절벽 끝에서 더 일찍 detach → cornerDetachHorizonTime ↑",
            MessageType.None);

        EditorGUILayout.Space(8);
        DrawHorizonReadout(s);
    }

    // Horizon gate is geometry-dependent (raycast against scene colliders) so it
    // can't be rendered on the v×R parameter chart. Instead, sample the lookahead
    // curve at a few speeds and show concrete distances — the numbers the user
    // needs to reason about whether lookahead reaches a cliff edge or probe depth
    // covers a small bump on their level.
    private static void DrawHorizonReadout(SlimeSettings s)
    {
        EditorGUILayout.LabelField("Horizon 검사 (geometry 의존 — heatmap 에 안 나옴)", EditorStyles.miniBoldLabel);

        float probeSkin = s.cornerDetachHorizonProbeSkin;
        float slimeRadius = s.slimeRadius;
        float probeDepth = slimeRadius + probeSkin;
        var curve = s.cornerDetachHorizonLookaheadCurve;
        bool curveOk = curve != null && curve.length >= 1;

        EditorGUILayout.LabelField($"probeSkin = {probeSkin:F2}m    slimeRadius = {slimeRadius:F2}m    → probeDepth = {probeDepth:F2}m");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("속도별 lookahead = LookaheadCurve.Evaluate(|v|):", EditorStyles.miniLabel);
        if (!curveOk)
        {
            EditorGUILayout.HelpBox("LookaheadCurve 가 비어있음 — 키프레임을 최소 1개 추가하세요.", MessageType.Warning);
        }
        else
        {
            float[] sampleSpeeds = { 1f, 2f, 5f, 10f, 15f, 20f };
            foreach (var v in sampleSpeeds)
            {
                float lookahead = curve.Evaluate(v);
                EditorGUILayout.LabelField($"  v = {v,5:F1} m/s   →   lookahead = {lookahead,5:F2} m");
            }
            float curveStart = curve[0].time;
            float curveEnd = curve[curve.length - 1].time;
            EditorGUILayout.LabelField($"  curve domain: |v| ∈ [{curveStart:F1}, {curveEnd:F1}] m/s  (밖은 PostInfinity 모드 따라 클램프/extrapolate)",
                EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            "동작: centrifugal 통과 후, 진행 방향으로 lookahead 만큼 옮긴 가상 위치에서 -surfaceNormal 방향 probeDepth 깊이로 raycast. " +
            "지면이 안 잡히면 detach (= 절벽 끝). 잡히면 wrap 유지.\n\n" +
            "튜닝:\n" +
            "• 곡선 위 키프레임 Y 값 ↑ → lookahead 거리 ↑ → 그 속도대에서 절벽 더 일찍 감지 (false-positive 도 늘어남)\n" +
            "• 곡선 위 키프레임 Y 값 ↓ → 원형 플랫폼 옆면 같은 곡면에서 sagitta 가 probeDepth 안에 들어와 wrap 유지\n" +
            "• 작은 굴곡·계단·다각형 facet 에 잘못 detach → probeSkin ↑ (probeDepth 깊어져 작은 갭 무시)\n" +
            "• 저속 절벽 감지 보장 → curve 의 v=0 부근 Y 값을 충분히 (예: 0.15m 이상) 두기",
            MessageType.None);
    }

    private void EnsureHeatmap(SlimeSettings s, float adhesion, float gravityAccel, float minSpeed)
    {
        if (heatmap != null
            && Mathf.Approximately(adhesion, cachedAdhesion)
            && Mathf.Approximately(gravityAccel, cachedGravity)
            && Mathf.Approximately(minSpeed, cachedMinSpeed)
            && s.frictionDetachFormula == cachedFormula
            && Mathf.Approximately(s.cornerVelocityThreshold, cachedVThreshold)
            && Mathf.Approximately(s.cornerMinAngleDeg, cachedMinAngleDeg)
            && Mathf.Approximately(s.cornerOmegaThreshold, cachedOmegaThreshold))
        {
            return;
        }

        if (heatmap == null)
        {
            heatmap = new Texture2D(HeatmapWidth, HeatmapHeight, TextureFormat.RGBA32, false);
            heatmap.filterMode = FilterMode.Point;
            heatmap.hideFlags = HideFlags.HideAndDontSave;
        }

        Color detachColor     = new Color(0.85f, 0.20f, 0.20f, 1f);   // 둘 다 detach
        Color wrapColor       = new Color(0.20f, 0.65f, 0.25f, 1f);   // 둘 다 wrap
        Color transitionColor = new Color(0.95f, 0.55f, 0.10f, 1f);   // 평지 wrap, 벽 detach (표면각 의존)
        Color cutoffColor     = new Color(0.30f, 0.30f, 0.40f, 1f);   // below minSpeed

        float availableFlat = gravityAccel + adhesion;
        float availableWall = adhesion;

        // Per-pixel classification: evaluate at both surface orientations so the
        // visual difference between "available-dependent" and "available-independent"
        // formulas shows up as a transition band rather than identical maps.
        // 0 = wrap on both, 1 = detach on wall only, 2 = detach on both, 3 = cutoff
        byte[] cls = new byte[HeatmapWidth * HeatmapHeight];
        for (int y = 0; y < HeatmapHeight; y++)
        {
            float R = Mathf.Lerp(RMinAxis, RMaxAxis, (float)y / (HeatmapHeight - 1));
            for (int x = 0; x < HeatmapWidth; x++)
            {
                float v = Mathf.Lerp(0f, VMaxAxis, (float)x / (HeatmapWidth - 1));
                int idx = y * HeatmapWidth + x;
                if (v < minSpeed) { cls[idx] = 3; continue; }
                bool flat = EvaluateDetach(s, v, R, availableFlat);
                bool wall = EvaluateDetach(s, v, R, availableWall);
                if (flat && wall) cls[idx] = 2;
                else if (wall)    cls[idx] = 1;
                else              cls[idx] = 0;
            }
        }

        Color[] pixels = new Color[HeatmapWidth * HeatmapHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            switch (cls[i])
            {
                case 0: pixels[i] = wrapColor; break;
                case 1: pixels[i] = transitionColor; break;
                case 2: pixels[i] = detachColor; break;
                default: pixels[i] = cutoffColor; break;
            }
        }

        heatmap.SetPixels(pixels);
        heatmap.Apply(false, false);

        cachedAdhesion = adhesion;
        cachedGravity = gravityAccel;
        cachedMinSpeed = minSpeed;
        cachedFormula = s.frictionDetachFormula;
        cachedVThreshold = s.cornerVelocityThreshold;
        cachedMinAngleDeg = s.cornerMinAngleDeg;
        cachedOmegaThreshold = s.cornerOmegaThreshold;
    }

    // Predicate mirroring SlimeController.Attached.cs's centrifugalAdmissible
    // switch, evaluated assuming uniform-curvature sliding (ω = v/R) so the
    // user sees the same v×R region the runtime would gate on.
    private static bool EvaluateDetach(SlimeSettings s, float v, float R, float available)
    {
        if (v <= 0f || R <= 0f) return false;
        float omega = v / R;
        float dThetaRad = omega * HeatmapDt;

        switch (s.frictionDetachFormula)
        {
            case FrictionDetachFormula.ConstantTimesOmega:
                return s.cornerVelocityThreshold * omega > available;
            case FrictionDetachFormula.VelocityTimesOmegaWithMinAngle:
                return (dThetaRad > s.cornerMinAngleDeg * Mathf.Deg2Rad)
                    && (v * omega > available);
            case FrictionDetachFormula.AngularRateThreshold:
                return omega > s.cornerOmegaThreshold;
            case FrictionDetachFormula.VelocityTimesOmega:
            default:
                return v * omega > available;
        }
    }

    // Numerical critical-speed lookup for the table. Uses a fine v sweep so it
    // works for every formula without per-mode algebra. Returns +∞ when no v in
    // the heatmap range triggers detach.
    private static float ResolveCriticalSpeed(SlimeSettings s, float R, float available)
    {
        const int Steps = 1024;
        for (int i = 1; i < Steps; i++)
        {
            float v = (VMaxAxis * i) / Steps;
            if (EvaluateDetach(s, v, R, available)) return v;
        }
        return float.PositiveInfinity;
    }

    private static void DrawLegendRow(Color swatch, string label)
    {
        Rect row = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
        Rect sw = new Rect(row.x + 4, row.y + 2, 14, 12);
        EditorGUI.DrawRect(sw, swatch);
        EditorGUI.DrawRect(new Rect(sw.x - 1, sw.y - 1, sw.width + 2, sw.height + 2), Color.clear);
        GUI.Label(new Rect(sw.xMax + 6, row.y, row.width - 30, 16), label, EditorStyles.miniLabel);
    }

    private void OnDisable()
    {
        if (heatmap != null)
        {
            DestroyImmediate(heatmap);
            heatmap = null;
        }
    }
}
