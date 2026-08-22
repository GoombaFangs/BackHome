using UnityEngine;

/// <summary>
/// World-space loot pickup. Pops up along planet-up, lands, then idles.
/// Item identity is applied at spawn via <see cref="Configure"/> so one prefab can serve many items.
/// Sprites billboard to camera; 3D meshes spin in place around planet-up.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class LootPickup : MonoBehaviour
{
    enum Phase
    {
        Dropping,
        Idle
    }

    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int amount = 1;
    [SerializeField, Min(0.05f)] float groundOffset = 0.35f;
    [Header("Drop")]
    [SerializeField, Min(0.05f)] float jumpHeight = 1.15f;
    [SerializeField, Min(0.05f)] float dropDuration = 0.55f;
    [SerializeField, Range(0f, 0.4f)] float bounceHeightRatio = 0.18f;
    [Header("Idle")]
    [SerializeField] float idleSpinDegreesPerSecond = 90f;
    [SerializeField] string dropState = "Drop";
    [SerializeField] string idleState = "Idle";

    LootDropPool _pool;
    GameObject _prefabKey;
    SpriteRenderer _sprite;
    Transform _visual;
    Animator _animator;
    Quaternion _visualRestRotation;
    Vector3 _groundPosition;
    Vector3 _up;
    Phase _phase;
    float _dropElapsed;
    float _idleSpin;
    bool _collected;
    bool _idleStarted;

    void Awake()
    {
        _sprite = GetComponentInChildren<SpriteRenderer>(true);
        _visual = _sprite != null
            ? _sprite.transform
            : (transform.childCount > 0 ? transform.GetChild(0) : transform);
        _visualRestRotation = _visual.localRotation;
        _animator = GetComponent<Animator>();

        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        if (col.radius < 0.2f)
            col.radius = 0.6f;
    }

    public void Configure(ItemDefinition definition, int dropAmount)
    {
        item = definition;
        amount = Mathf.Max(1, dropAmount);
    }

    public void ActivateFromPool(LootDropPool pool, GameObject prefabKey, Vector3 worldPosition)
    {
        _pool = pool;
        _prefabKey = prefabKey;
        _collected = false;
        _idleStarted = false;
        _dropElapsed = 0f;
        _idleSpin = 0f;
        _phase = Phase.Dropping;

        _up = ResolveUp(worldPosition);
        _groundPosition = worldPosition + _up * groundOffset;
        transform.SetPositionAndRotation(
            _groundPosition,
            Quaternion.FromToRotation(Vector3.up, _up));

        if (_visual != null)
            _visual.localRotation = _visualRestRotation;

        ApplyItemVisual();
        gameObject.SetActive(true);

        if (_animator != null && _sprite != null && !string.IsNullOrWhiteSpace(dropState))
        {
            _animator.Rebind();
            _animator.Play(dropState, 0, 0f);
            _animator.Update(0f);
        }
    }

    public void ReturnToPool()
    {
        if (_pool != null)
            _pool.Release(this, _prefabKey);
        else
            gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_collected)
            return;

        if (_phase == Phase.Dropping)
            TickDrop();
        else
            transform.position = _groundPosition;

        UpdateVisual();
    }

    void TickDrop()
    {
        _dropElapsed += Time.deltaTime;
        float t = dropDuration > 0.0001f ? Mathf.Clamp01(_dropElapsed / dropDuration) : 1f;
        float height = EvaluateDropHeight(t);
        transform.position = _groundPosition + _up * height;

        if (t < 1f)
            return;

        transform.position = _groundPosition;
        _phase = Phase.Idle;
        BeginIdle();
    }

    float EvaluateDropHeight(float t)
    {
        float main = 4f * t * (1f - t);
        float bounce = 0f;
        if (t > 0.72f)
        {
            float u = (t - 0.72f) / 0.28f;
            bounce = bounceHeightRatio * 4f * u * (1f - u);
        }

        return jumpHeight * (main + bounce);
    }

    void BeginIdle()
    {
        if (_idleStarted)
            return;

        _idleStarted = true;
        if (_animator != null && _sprite != null && !string.IsNullOrWhiteSpace(idleState))
            _animator.Play(idleState, 0, 0f);
    }

    void ApplyItemVisual()
    {
        if (_sprite != null && item != null && item.Icon != null)
            _sprite.sprite = item.Icon;
    }

    void UpdateVisual()
    {
        if (_visual == null)
            return;

        if (_sprite != null)
        {
            BillboardSprite();
            return;
        }

        if (_phase != Phase.Idle || Mathf.Abs(idleSpinDegreesPerSecond) < 0.01f)
            return;

        _idleSpin += idleSpinDegreesPerSecond * Time.deltaTime;
        _visual.localRotation = _visualRestRotation * Quaternion.AngleAxis(_idleSpin, Vector3.up);
    }

    void BillboardSprite()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Quaternion spin = _visual.localRotation;
        _visual.rotation = cam.transform.rotation * spin;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_collected || !IsPlayer(other))
            return;

        if (_phase == Phase.Dropping && _dropElapsed < dropDuration * 0.65f)
            return;

        _collected = true;

        if (item != null)
        {
            PlayerInventory inventory = PlayerInventory.EnsureExists();
            inventory.Add(item, amount);
        }

        ReturnToPool();
    }

    static Vector3 ResolveUp(Vector3 worldPosition)
    {
        if (SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(worldPosition);
        return Vector3.up;
    }

    static bool IsPlayer(Collider other)
    {
        if (other == null)
            return false;

        if (other.CompareTag("Player"))
            return true;

        Transform root = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform.root;

        return root != null && root.CompareTag("Player");
    }
}
