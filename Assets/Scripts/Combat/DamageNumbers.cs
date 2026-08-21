using UnityEngine;

/// <summary>
/// Spawns floating <see cref="DamagePopup"/> numbers above this host when it takes damage.
/// Put on creatures (or any <see cref="IVitalsReadable"/>).
/// </summary>
[DefaultExecutionOrder(1100)]
public class DamageNumbers : MonoBehaviour
{
    [SerializeField] DamagePopup popupPrefab;
    [Tooltip("Offset from the host. X = right, Y = up (along surface), Z = forward.")]
    [SerializeField] Vector3 localOffset = new Vector3(0f, 2.4f, 0f);

    IVitalsReadable _source;

    void Awake()
    {
        _source = GetComponent<IVitalsReadable>();
    }

    void OnEnable()
    {
        if (_source == null)
            _source = GetComponent<IVitalsReadable>();

        if (_source != null)
            _source.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_source != null)
            _source.Damaged -= OnDamaged;
    }

    void OnDamaged(float amount)
    {
        if (amount <= 0f || popupPrefab == null)
            return;

        Spawn(amount);
    }

    void Spawn(float amount)
    {
        Vector3 up = ResolveUp();
        Vector3 right = Vector3.Cross(up, Vector3.forward);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(up, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, up).normalized;

        Vector3 pos = transform.position
                      + right * localOffset.x
                      + up * localOffset.y
                      + forward * localOffset.z;

        DamagePopup.Create(popupPrefab, pos, amount, up);
    }

    Vector3 ResolveUp()
    {
        if (SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(transform.position);
        return transform.up;
    }
}
