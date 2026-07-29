using UnityEngine;
using SlimeJumping.Platforms;

// 구름 발판 소멸 비주얼. 비주얼 = 씬 메시(sceneCloud), prefab instantiate 없음.
// Disperse(): sceneCloud 숨기고 CloudFractureTool 조각을 같은 위치에 표시(seam 없음) → 팝+spin+scale→0.
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
    float refSize = 1f;   // 조각 흩어짐을 구름 크기에 비례시키는 기준(스케일 독립).

    public bool IsDispersing => dispersing;

    void Awake() { Build(); }

    [ContextMenu("Build")]
    public void Build()
    {
        if (cfg == null) cfg = CloudPlatformConfig.Resolve(configOverride);
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
        SetActiveSafe(sceneCloud, true);
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
        foreach (var p in pieces)
        {
            p.t = -p.delay;
            p.tr.localPosition = p.restPos;
            p.tr.localScale = p.restScale;
            p.tr.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        if (!dispersing) return;
        bool anyAlive = false;
        foreach (var p in pieces)
        {
            if (p.t < 0f) { p.t += Time.deltaTime; anyAlive = true; continue; }
            p.t += Time.deltaTime;
            float u = Mathf.Clamp01(p.t / cfg.life);
            float ease = 1f - (1f - u) * (1f - u);                   // easeOut = 초반 빠르게 튐.

            p.tr.localPosition = p.restPos + (p.dir * cfg.spread + Vector3.up * cfg.rise) * refSize * ease;
            // 초반 살짝 pop(부풀기) 후 빠르게 scale→0.
            float pop = 1f + (cfg.popScale - 1f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.7f));
            float shrink = 1f - Mathf.SmoothStep(0.45f, 1f, u);
            p.tr.localScale = p.restScale * (pop * shrink);
            p.tr.Rotate(p.spinAxis, cfg.spin * Time.deltaTime, Space.Self);

            if (u >= 1f) p.tr.gameObject.SetActive(false);
            else anyAlive = true;
        }
        if (!anyAlive) dispersing = false;
    }

    static void SetActiveSafe(GameObject go, bool v) { if (go != null) go.SetActive(v); }
}
