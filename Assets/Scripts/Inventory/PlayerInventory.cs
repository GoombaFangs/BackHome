using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime player inventory. Survives scene loads via DontDestroyOnLoad
/// (player is recreated on ship/planet transitions and death respawn).
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public struct Slot
    {
        public ItemDefinition item;
        public int count;
    }

    static PlayerInventory _instance;

    readonly List<Slot> _slots = new();

    public static PlayerInventory Instance => _instance;

    public IReadOnlyList<Slot> Slots => _slots;

    public event Action Changed;

    public static PlayerInventory EnsureExists()
    {
        if (_instance != null)
            return _instance;

        var go = new GameObject("PlayerInventory");
        return go.AddComponent<PlayerInventory>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    public void Add(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0)
            return;

        int cap = item.MaxStack;

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].item != item)
                continue;

            Slot slot = _slots[i];
            int next = Mathf.Min(cap, slot.count + amount);
            if (next == slot.count)
                return;

            slot.count = next;
            _slots[i] = slot;
            Changed?.Invoke();
            return;
        }

        _slots.Add(new Slot { item = item, count = Mathf.Min(cap, amount) });
        Changed?.Invoke();
    }
}
