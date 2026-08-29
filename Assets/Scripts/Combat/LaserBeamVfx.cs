using UnityEngine;

/// <summary>
/// Short-lived <see cref="LineRenderer"/> beam drawn between two world points — used for on-hit
/// effects that visually "jump" between creatures (for example Frosty's chain effect). Spawn the
/// prefab, call <see cref="Init"/> with the two endpoints, and it shrinks and destroys itself.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class LaserBeamVfx : MonoBehaviour
{
    [SerializeField, Min(0.01f)] float lifetime = 0.18f;
    [SerializeField, Min(0f)] float width = 0.35f;
    [SerializeField] AnimationCurve widthOverLife = new(
        new Keyframe(0f, 0f),
        new Keyframe(0.15f, 1f),
        new Keyframe(1f, 0f));

    LineRenderer _line;
    float _elapsed;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
    }

    public void Init(Vector3 from, Vector3 to)
    {
        if (_line == null)
            _line = GetComponent<LineRenderer>();

        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.SetPosition(0, from);
        _line.SetPosition(1, to);
        _elapsed = 0f;
        ApplyWidth(0f);
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = lifetime > 0f ? Mathf.Clamp01(_elapsed / lifetime) : 1f;
        ApplyWidth(t);
    }

    void ApplyWidth(float t)
    {
        if (_line == null)
            return;

        float w = Mathf.Max(0f, width * widthOverLife.Evaluate(t));
        _line.startWidth = w;
        _line.endWidth = w;
    }
}
