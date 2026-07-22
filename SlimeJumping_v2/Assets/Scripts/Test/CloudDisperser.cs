using UnityEngine;

// 구름 발판 소멸 비주얼 (low-poly + 캐주얼).
// 구름 = 단일 prefab(유저 스타일). 조각 = 별개 prefab(구름을 쪼갠 것, 조립 시 구름과 동일).
// 평소: cloudPrefab 표시, piecesPrefab 은 조립 상태로 숨김(같은 위치).
// Disperse(): 구름 숨김 + 조각 표시(동일 위치 = seam 없음) + 각 조각 "캐주얼 팝"
//   (바깥+위로 빠르게 튐 + 빠른 spin + 초반 살짝 pop 후 scale→0, 짧은 lifetime, 약간 stagger).
// dissolve/알파 페이드 없음(홀로그램 금지). 조각 매칭은 CloudFractureTool 로 생성.
public class CloudDisperser : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject cloudPrefab;    // 단일 low-poly 구름(유저 스타일).
    public GameObject piecesPrefab;   // 매칭 조각(CloudFractureTool 출력, 조립=구름).

    [Header("캐주얼 팝")]
    public float spread = 1.0f;       // 바깥으로 튀는 거리.
    public float rise = 0.9f;         // 위로.
    public float life = 0.42f;        // 조각 수명(짧고 snappy).
    [Range(0f, 0.25f)] public float stagger = 0.06f; // 조각별 시작 지연 폭.
    public float spin = 220f;         // deg/s 빠른 회전.
    [Range(1f, 1.6f)] public float popScale = 1.25f; // 초반 살짝 부풀기.

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

    GameObject cloud;
    GameObject piecesRoot;
    Piece[] pieces;
    bool dispersing;
    float refSize = 1f;   // 조각 흩어짐을 구름 크기에 비례시키는 기준(스케일 독립).

    public bool IsDispersing => dispersing;

    void Awake() { Build(); }

    [ContextMenu("Build")]
    public void Build()
    {
        // 정리.
        var oc = transform.Find("Cloud"); if (oc != null) DestroyImmediate(oc.gameObject);
        var op = transform.Find("Pieces"); if (op != null) DestroyImmediate(op.gameObject);
        cloud = null; piecesRoot = null; pieces = null;

        if (cloudPrefab != null)
        {
            cloud = Instantiate(cloudPrefab, transform);
            cloud.name = "Cloud";
            cloud.transform.localPosition = Vector3.zero;
            cloud.transform.localRotation = Quaternion.identity;
        }

        if (piecesPrefab != null)
        {
            piecesRoot = Instantiate(piecesPrefab, transform);
            piecesRoot.name = "Pieces";
            piecesRoot.transform.localPosition = Vector3.zero;
            piecesRoot.transform.localRotation = Quaternion.identity;

            var childCount = piecesRoot.transform.childCount;
            pieces = new Piece[childCount];
            // 클러스터 중심 = 조각 rest 위치 평균.
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
                    delay = Random.Range(0f, stagger),
                };
            }
        }

        ResetToRest();
    }

    [ContextMenu("Reset To Rest")]
    public void ResetToRest()
    {
        dispersing = false;
        SetActiveSafe(cloud, true);
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
        SetActiveSafe(cloud, false);
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
            float u = Mathf.Clamp01(p.t / life);
            float ease = 1f - (1f - u) * (1f - u);                   // easeOut = 초반 빠르게 튐.

            p.tr.localPosition = p.restPos + (p.dir * spread + Vector3.up * rise) * refSize * ease;
            // 초반 살짝 pop(부풀기) 후 빠르게 scale→0.
            float pop = 1f + (popScale - 1f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.7f));
            float shrink = 1f - Mathf.SmoothStep(0.45f, 1f, u);
            p.tr.localScale = p.restScale * (pop * shrink);
            p.tr.Rotate(p.spinAxis, spin * Time.deltaTime, Space.Self);

            if (u >= 1f) p.tr.gameObject.SetActive(false);
            else anyAlive = true;
        }
        if (!anyAlive) dispersing = false;
    }

    static void SetActiveSafe(GameObject go, bool v) { if (go != null) go.SetActive(v); }
}
