using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Background
{
    // Editor authoring tool: hits the "Randomize" button in the Inspector to
    // randomly enable/disable the GameObjects N levels below this transform,
    // then saves the result with the scene. No runtime behavior by default —
    // turn on `randomizeOnStart` if you also want play-mode shuffling.
    //
    // Default depth = 3 matches CloudField → CloudBackground → CloudsN → Cloud_NN.
    public class CloudVisibilityRandomizer : MonoBehaviour
    {
        [Tooltip("Hierarchy depth from this transform to the leaves to toggle. " +
                 "3 = great-grandchildren (CloudField → CloudBackground → CloudsN → Cloud_NN).")]
        [SerializeField] int depth = 3;

        [Tooltip("Probability each leaf is active per pass. 1 = all on, 0 = all off.")]
        [Range(0f, 1f)]
        [SerializeField] float activeChance = 0.6f;

        [Header("Optional Runtime Behavior")]
        [Tooltip("If true, randomize once when entering Play mode.")]
        [SerializeField] bool randomizeOnStart = false;

        [Tooltip("Seconds between re-randomizations during Play. 0 = never.")]
        [SerializeField] float tickInterval = 0f;

        readonly List<GameObject> _leaves = new();
        float _nextTickAt;

        void Start()
        {
            if (!randomizeOnStart && tickInterval <= 0f) return;

            CollectLeaves();
            if (randomizeOnStart) Randomize();
            _nextTickAt = Time.time + tickInterval;
        }

        void Update()
        {
            if (tickInterval <= 0f) return;
            if (Time.time < _nextTickAt) return;
            Randomize();
            _nextTickAt = Time.time + tickInterval;
        }

        public void CollectLeaves()
        {
            _leaves.Clear();
            CollectAtDepth(transform, 0);
        }

        void CollectAtDepth(Transform t, int currentDepth)
        {
            if (currentDepth == depth)
            {
                _leaves.Add(t.gameObject);
                return;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                CollectAtDepth(t.GetChild(i), currentDepth + 1);
            }
        }

        public IReadOnlyList<GameObject> Leaves => _leaves;

        public void Randomize()
        {
            for (int i = 0; i < _leaves.Count; i++)
            {
                _leaves[i].SetActive(Random.value < activeChance);
            }
        }

        public void SetAllActive(bool active)
        {
            for (int i = 0; i < _leaves.Count; i++)
            {
                _leaves[i].SetActive(active);
            }
        }
    }
}
