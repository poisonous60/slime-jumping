using UnityEngine;
using SlimeJumping.Platforms;

// 구름 발판 소멸/재생성 비주얼. 비주얼 = 씬 메시(sceneCloud), prefab instantiate 없음.
// Disperse(): sceneCloud 숨기고 CloudFractureTool 조각을 같은 위치에 표시(seam 없음) → 팝+spin+scale→0.
// Reform(): sceneCloud 를 중심에서 scale 로 부풀린다. 조각은 관여 안 함 — 같이 모으면
//           같은 구름을 두 번 그리고, 조각 레이아웃은 안 줄어서 축소된 본체보다 넓게 퍼진다.
// dissolve/알파 페이드 금지(홀로그램).
public class CloudDisperser : MonoBehaviour
{
    [Header("Config")]
    public CloudPlatformConfig configOverride;   // 비우면 Assets/Resources/CloudPlatformConfig.asset 공유 튜닝.

    [Header("Visual")]
    public GameObject sceneCloud;     // 씬 메시 비주얼(자식). intact 상태.
    public GameObject piecesPrefab;   // 매칭 조각(CloudFractureTool 출력, 조립=구름).

    CloudPlatformConfig cfg;

    class Piece
    {
        public Transform tr;
        public Vector3 restPos;
        public Vector3 restScale;
        public Vector3 dir;
        public Vector3 spinAxis;
        public float delay;
        public float t;
    }

    GameObject piecesRoot;
    Piece[] pieces;
    bool dispersing;
    bool reforming;
    float reformT;
    Vector3 sceneCloudRestScale = Vector3.one;
    float refSize = 1f;   // 조각 흩어짐을 구름 크기에 비례시키는 기준(스케일 독립).

    public bool IsDispersing => dispersing;
    public bool IsReforming => reforming;

    void Awake() { Build(); }

    [ContextMenu("Build")]
    public void Build()
    {
        if (cfg == null) cfg = CloudPlatformConfig.Resolve(configOverride);
        // reform 이 중간에 끊겨도 원래 크기로 돌아올 수 있게, 애니메이션 시작 전에 캐시.
        if (sceneCloud != null && !reforming) sceneCloudRestScale = sceneCloud.transform.localScale;
        var op = transform.Find("Pieces"); if (op != null) DestroyImmediate(op.gameObject);
        piecesRoot = null; pieces = null;

        if (piecesPrefab != null)
        {
            piecesRoot = Instantiate(piecesPrefab, transform);
            piecesRoot.name = "Pieces";
            if (sceneCloud != null)
            {
                // 조각은 sceneCloud mesh-local 좌표 → world 변환을 sceneCloud 와 맞춰야 조립이 겹침
                // (sceneCloud 가 wrapper 로 더 깊이 nested 여도 동작).
                var st = sceneCloud.transform;
                var pt = piecesRoot.transform;
                pt.SetPositionAndRotation(st.position, st.rotation);
                Vector3 ps = pt.parent.lossyScale, ss = st.lossyScale;
                pt.localScale = new Vector3(ss.x / ps.x, ss.y / ps.y, ss.z / ps.z);
            }
            else
            {
                piecesRoot.transform.localPosition = Vector3.zero;
                piecesRoot.transform.localRotation = Quaternion.identity;
            }

            var childCount = piecesRoot.transform.childCount;
            pieces = new Piece[childCount];
            Vector3 center = Vector3.zero;
            for (int i = 0; i < childCount; i++) center += piecesRoot.transform.GetChild(i).localPosition;
            if (childCount > 0) center /= childCount;

            // refSize = 조각 rest 위치의 평균 반경(구름 크기 대용) → 흩어짐 비례.
            float sum = 0f;
            for (int i = 0; i < childCount; i++) sum += (piecesRoot.transform.GetChild(i).localPosition - center).magnitude;
            refSize = childCount > 0 ? Mathf.Max(0.01f, sum / childCount) : 1f;

            for (int i = 0; i < childCount; i++)
            {
                var ct = piecesRoot.transform.GetChild(i);
                Vector3 outDir = ct.localPosition - center;
                if (outDir.sqrMagnitude < 1e-4f) outDir = new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f));
                outDir.Normalize();
                pieces[i] = new Piece
                {
                    tr = ct,
                    restPos = ct.localPosition,
                    restScale = ct.localScale,
                    dir = outDir,
                    spinAxis = Random.onUnitSphere,
                    delay = Random.Range(0f, cfg.stagger),
                };
            }
        }

        ResetToRest();
    }

    [ContextMenu("Reset To Rest")]
    public void ResetToRest()
    {
        dispersing = false;
        reforming = false;
        SetActiveSafe(sceneCloud, true);
        if (sceneCloud != null) sceneCloud.transform.localScale = sceneCloudRestScale;
        if (pieces != null)
            foreach (var p in pieces)
            {
                p.t = -1f;
                p.tr.localPosition = p.restPos;
                p.tr.localScale = p.restScale;
                p.tr.gameObject.SetActive(false);
            }
        else SetActiveSafe(piecesRoot, false);
    }

    [ContextMenu("Disperse Now")]
    public void Disperse()
    {
        if (pieces == null) Build();
        // 스왑: 구름 숨김, 조각 표시(동일 위치 = seamless).
        SetActiveSafe(sceneCloud, false);
        if (pieces == null || pieces.Length == 0) return;
        dispersing = true;
        reforming = false;
        foreach (var p in pieces)
        {
            p.t = -p.delay;
            p.tr.localPosition = p.restPos;
            p.tr.localScale = p.restScale;
            p.tr.gameObject.SetActive(true);
        }
    }

    // 본체 하나만 부풀린다. 조각을 같이 모으면 같은 구름 지오메트리를 두 번 그리게 되고,
    // 조각은 restPos 레이아웃이 안 줄어들어 축소된 본체보다 클러스터가 넓다 → 깨진 텍스처로 보였다.
    // 피벗이 bounds 중심이라 scale 만으로 중심에서 균일하게 자란다(알파 금지 계약도 지킴).
    [ContextMenu("Reform Now")]
    public void Reform()
    {
        if (pieces == null) Build();
        reformT = 0f;
        dispersing = false;
        reforming = true;
        if (sceneCloud != null)
        {
            sceneCloud.transform.localScale = Vector3.zero;   // 첫 프레임 팝 방지.
            SetActiveSafe(sceneCloud, true);
        }
    }

    void Update()
    {
        if (reforming) UpdateReform();
        if (!dispersing) return;

        bool anyAlive = false;
        foreach (var p in pieces)
        {
            if (p.t < 0f) { p.t += Time.deltaTime; anyAlive = true; continue; }
            p.t += Time.deltaTime;
            float u = Mathf.Clamp01(p.t / Mathf.Max(0.01f, cfg.life));
            ApplyDisperse(p, u);
            p.tr.Rotate(p.spinAxis, cfg.spin * Time.deltaTime, Space.Self);

            if (u >= 1f) p.tr.gameObject.SetActive(false);
            else anyAlive = true;
        }
        if (!anyAlive) dispersing = false;
    }

    void UpdateReform()
    {
        float dur = Mathf.Max(0.01f, cfg.regenTime);
        reformT += Time.deltaTime;
        // 양끝이 완만한 SmoothStep 단조 증가 — 오버슈트를 주면 pop 이 되어 얌전함이 깨진다.
        if (sceneCloud != null)
            sceneCloud.transform.localScale =
                sceneCloudRestScale * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(reformT / dur));

        if (reformT >= dur) ResetToRest();
    }

    void ApplyDisperse(Piece p, float u)
    {
        float ease = 1f - (1f - u) * (1f - u);                   // easeOut = 초반 빠르게 튐.
        p.tr.localPosition = p.restPos + (p.dir * cfg.spread + Vector3.up * cfg.rise) * refSize * ease;
        // 초반 살짝 pop(부풀기) 후 빠르게 scale→0.
        float pop = 1f + (cfg.popScale - 1f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.7f));
        float shrink = 1f - Mathf.SmoothStep(0.45f, 1f, u);
        p.tr.localScale = p.restScale * (pop * shrink);
    }

    static void SetActiveSafe(GameObject go, bool v) { if (go != null) go.SetActive(v); }
}
