using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Play-mode only. Hides props under this Environment object, then turns each one
/// on when the player or the camera comes within range. Edit mode never touches
/// active state, so the full set stays visible while you place and arrange props.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
[AddComponentMenu("BackHome/Planet Environment Streamer")]
public class PlanetEnvironmentStreamer : MonoBehaviour
{
    [Tooltip("A prop turns on when the player or the camera is at least this close (meters).")]
    [SerializeField, Min(1f)] float showRadius = 42f;
    [Tooltip("A prop turns back off only after both the player and the camera are farther than this. Keep it larger than Show Radius so props don't flicker on the edge.")]
    [SerializeField, Min(1f)] float hideRadius = 52f;

    struct Entry
    {
        public GameObject Instance;
        public bool Visible;
    }

    Entry[] _entries = System.Array.Empty<Entry>();
    Transform _player;
    Transform _camera;
    int _cameraTimer;
    float _showSqr;
    float _hideSqr;

    void OnValidate()
    {
        showRadius = Mathf.Max(1f, showRadius);
        hideRadius = Mathf.Max(showRadius + 1f, hideRadius);
    }

    void Awake()
    {
        // Edit mode must keep every prop visible for arranging.
        if (!Application.isPlaying)
            return;

        _showSqr = showRadius * showRadius;
        _hideSqr = hideRadius * hideRadius;

        var found = new List<GameObject>(256);
        SphericalPlanet planet = GetComponentInParent<SphericalPlanet>();
        Collect(transform, planet, found);

        _entries = new Entry[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            _entries[i].Instance = found[i];
            _entries[i].Visible = false;
            found[i].SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || _entries.Length == 0)
            return;

        ResolveTargets();
        _showSqr = showRadius * showRadius;
        _hideSqr = hideRadius * hideRadius;
        bool hasPlayer = _player != null;
        bool hasCamera = _camera != null;
        if (!hasPlayer && !hasCamera)
            return;

        Vector3 playerPos = hasPlayer ? _player.position : default;
        Vector3 cameraPos = hasCamera ? _camera.position : default;

        for (int i = 0; i < _entries.Length; i++)
        {
            GameObject instance = _entries[i].Instance;
            if (instance == null)
                continue;

            Vector3 position = instance.transform.position;
            float best = float.PositiveInfinity;
            if (hasPlayer)
            {
                float d = (position - playerPos).sqrMagnitude;
                if (d < best)
                    best = d;
            }

            if (hasCamera)
            {
                float d = (position - cameraPos).sqrMagnitude;
                if (d < best)
                    best = d;
            }

            bool visible = _entries[i].Visible;
            bool shouldShow = visible ? best <= _hideSqr : best <= _showSqr;
            if (shouldShow == visible)
                continue;

            _entries[i].Visible = shouldShow;
            instance.SetActive(shouldShow);
        }
    }

    void ResolveTargets()
    {
        if (_player == null)
        {
            PlanetWalker walker = FindAnyObjectByType<PlanetWalker>();
            if (walker != null)
                _player = walker.transform;
        }

        _cameraTimer--;
        if (_camera == null || !_camera.gameObject.activeInHierarchy || _cameraTimer <= 0)
        {
            _cameraTimer = 20;
            Camera cam = Camera.main;
            _camera = cam != null ? cam.transform : null;
        }
    }

    static void Collect(Transform container, SphericalPlanet planet, List<GameObject> into)
    {
        float centerLimit = planet != null ? planet.Radius * 0.35f : 0f;
        float centerLimitSqr = centerLimit * centerLimit;
        Vector3 center = planet != null ? planet.Center : container.position;

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;

            if (IsContainer(child))
            {
                Collect(child, planet, into);
                continue;
            }

            if (child.GetComponentInChildren<Renderer>(true) == null
                && child.GetComponentInChildren<Collider>(true) == null)
                continue;

            // Props live on the surface. Objects near the planet center (a whole-planet model
            // sitting on the origin) stay active so they are not culled by a pivot at the core.
            if (planet != null && (child.position - center).sqrMagnitude < centerLimitSqr)
                continue;

            into.Add(child.gameObject);
        }
    }

    static bool IsContainer(Transform t)
    {
        if (t.GetComponent<PlanetSurfaceAlign>() != null)
            return false;
        if (t.GetComponent<Renderer>() != null
            || t.GetComponent<MeshFilter>() != null
            || t.GetComponent<Collider>() != null)
            return false;
        if (t.childCount == 0)
            return false;

        for (int i = 0; i < t.childCount; i++)
        {
            if (t.GetChild(i).GetComponent<PlanetSurfaceAlign>() != null)
                return true;
        }

        // Category folders (Grass / Rocks / Trees) sit at the environment origin.
        // A placed prefab sits on the surface, so it is one prop even if its mesh is on a child.
        return t.localPosition.sqrMagnitude < 0.0001f
               && t.GetComponent<Animator>() == null
               && t.GetComponentInChildren<Renderer>(true) != null;
    }
}
