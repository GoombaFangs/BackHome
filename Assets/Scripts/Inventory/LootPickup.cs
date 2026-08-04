using UnityEngine;

/// <summary>
/// World-space loot pickup (sprite billboard + trigger). Returned to <see cref="LootDropPool"/> on collect.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class LootPickup : MonoBehaviour
{
    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int amount = 1;
    [SerializeField] float bobHeight = 0.12f;
    [SerializeField] float bobSpeed = 2.5f;
    [SerializeField] float spinDegreesPerSecond = 70f;
    [SerializeField] float groundOffset = 0.55f;

    LootDropPool _pool;
    GameObject _prefabKey;
    SpriteRenderer _sprite;
    Vector3 _basePosition;
    float _phase;
    bool _collected;

    public ItemDefinition Item => item;
    public int Amount => amount;

    void Awake()
    {
        _sprite = GetComponentInChildren<SpriteRenderer>();
        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        if (col.radius < 0.2f)
            col.radius = 0.6f;
    }

    public void Configure(ItemDefinition definition, int stackAmount)
    {
        if (definition != null)
            item = definition;
        amount = Mathf.Max(1, stackAmount);

        if (_sprite != null && item != null && item.Icon != null)
            _sprite.sprite = item.Icon;
    }

    public void ActivateFromPool(LootDropPool pool, GameObject prefabKey, Vector3 worldPosition)
    {
        _pool = pool;
        _prefabKey = prefabKey;
        _collected = false;
        _phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        Vector3 up = ResolveUp(worldPosition);
        _basePosition = worldPosition + up * groundOffset;
        transform.position = _basePosition;
        gameObject.SetActive(true);
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

        float bob = Mathf.Sin((Time.time + _phase) * bobSpeed) * bobHeight;
        Vector3 up = ResolveUp(_basePosition);
        transform.position = _basePosition + up * bob;

        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        if (_sprite != null && spinDegreesPerSecond != 0f)
            _sprite.transform.Rotate(0f, 0f, spinDegreesPerSecond * Time.deltaTime, Space.Self);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_collected || !IsPlayer(other))
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
