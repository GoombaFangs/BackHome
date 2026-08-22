using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-prefab pool for world loot drops (reuse via SetActive, no Destroy per kill).
/// Prefab key comes from <see cref="ItemDefinition.WorldDropPrefab"/>.
/// </summary>
public class LootDropPool : MonoBehaviour
{
    readonly Dictionary<GameObject, Queue<LootPickup>> _pools = new();
    Transform _root;

    void Awake()
    {
        EnsureRoot();
    }

    public LootPickup Spawn(ItemDefinition item, int amount, Vector3 worldPosition)
    {
        if (item == null)
            return null;

        GameObject prefab = item.WorldDropPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"{name}: item '{item.DisplayName}' has no world drop prefab.", item);
            return null;
        }

        LootPickup pickup = GetOrCreate(prefab);
        if (pickup == null)
            return null;

        pickup.Configure(item, amount);
        pickup.ActivateFromPool(this, prefab, worldPosition);
        return pickup;
    }

    public LootPickup Spawn(GameObject prefab, Vector3 worldPosition)
    {
        LootPickup pickup = GetOrCreate(prefab);
        if (pickup == null)
            return null;

        pickup.ActivateFromPool(this, prefab, worldPosition);
        return pickup;
    }

    public void Release(LootPickup pickup, GameObject prefabKey)
    {
        if (pickup == null)
            return;

        pickup.gameObject.SetActive(false);
        pickup.transform.SetParent(_root, false);

        if (prefabKey == null)
            return;

        if (!_pools.TryGetValue(prefabKey, out Queue<LootPickup> queue))
        {
            queue = new Queue<LootPickup>(8);
            _pools[prefabKey] = queue;
        }

        queue.Enqueue(pickup);
    }

    LootPickup GetOrCreate(GameObject prefab)
    {
        if (prefab == null)
            return null;

        EnsureRoot();

        if (!_pools.TryGetValue(prefab, out Queue<LootPickup> queue))
        {
            queue = new Queue<LootPickup>(8);
            _pools[prefab] = queue;
        }

        LootPickup pickup = null;
        while (queue.Count > 0 && pickup == null)
        {
            LootPickup candidate = queue.Dequeue();
            if (candidate != null)
                pickup = candidate;
        }

        if (pickup != null)
            return pickup;

        GameObject instance = Instantiate(prefab, _root);
        instance.name = prefab.name;
        pickup = instance.GetComponent<LootPickup>();
        if (pickup == null)
            pickup = instance.GetComponentInChildren<LootPickup>();

        if (pickup != null)
            return pickup;

        Debug.LogWarning($"{name}: loot prefab '{prefab.name}' is missing LootPickup.", this);
        Destroy(instance);
        return null;
    }

    void EnsureRoot()
    {
        if (_root != null)
            return;

        Transform existing = transform.Find("LootPool");
        if (existing != null)
        {
            _root = existing;
            return;
        }

        var go = new GameObject("LootPool");
        go.transform.SetParent(transform, false);
        _root = go.transform;
    }
}
