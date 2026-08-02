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
    [SerializeField] bool showOnHeal;

    IVitalsReadable _source;

    public Vector3 LocalOffset
    {
        get => localOffset;
        set => localOffset = value;
    }

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

        Spawn(amount, isHeal: false);
    }

    /// <summary>Optional: call from heal code if you want green popups.</summary>
    public void ShowHeal(float amount)
    {
        if (!showOnHeal || amount <= 0f || popupPrefab == null)
            return;

        Spawn(amount, isHeal: true);
    }

    void Spawn(float amount, bool isHeal)
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

        DamagePopup.Create(popupPrefab, pos, amount, up, isHeal);
    }

    Vector3 ResolveUp()
    {
        if (SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(transform.position);
        return transform.up;
    }
}
