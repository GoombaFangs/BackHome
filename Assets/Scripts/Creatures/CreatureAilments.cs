using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime status effects applied to a creature by weapons (DoT, and later others).
/// Added on first proc so creature prefabs don't need a special component.
/// </summary>
public class CreatureAilments : MonoBehaviour
{
    const float VfxHeight = 1.15f;

    sealed class HitBuildup
    {
        public WeaponDefinition Weapon;
        public int Hits;
        public float LastHitAt;
    }

    sealed class ActiveDot
    {
        public WeaponDefinition Weapon;
        public float DamagePerSecond;
        public float TickInterval;
        public float TickTimer;
        public float Remaining;
        public GameObject Vfx;
    }

    readonly List<HitBuildup> _hits = new();
    readonly List<ActiveDot> _dots = new();
    Creature _creature;

    public static void RegisterRepeatedHit(Creature creature, WeaponDefinition weapon, float hitDamage, GameObject debuffVfx, Vector3 debuffEuler)
    {
        if (creature == null || weapon == null || !creature.IsAlive)
            return;
        if (weapon.DotDamagePerSecond <= 0f && hitDamage <= 0f)
            return;

        CreatureAilments host = creature.GetComponent<CreatureAilments>();
        if (host == null)
            host = creature.gameObject.AddComponent<CreatureAilments>();
        host.OnRepeatedHit(weapon, hitDamage, debuffVfx, debuffEuler);
    }

    void Awake()
    {
        _creature = GetComponent<Creature>();
    }

    void OnDisable()
    {
        ClearDots();
        _hits.Clear();
    }

    void Update()
    {
        if (_creature == null || !_creature.IsAlive)
        {
            ClearDots();
            return;
        }

        float dt = Time.deltaTime;
        for (int i = _dots.Count - 1; i >= 0; i--)
        {
            ActiveDot dot = _dots[i];
            dot.Remaining -= dt;
            if (dot.Remaining <= 0f)
            {
                EndDot(i);
                continue;
            }

            dot.TickTimer -= dt;
            if (dot.TickTimer > 0f)
                continue;

            float tick = dot.DamagePerSecond * dot.TickInterval;
            dot.TickTimer += dot.TickInterval;
            if (tick > 0f)
                _creature.TakeDamage(tick);

            if (_creature == null || !_creature.IsAlive)
            {
                ClearDots();
                return;
            }
        }
    }

    void OnRepeatedHit(WeaponDefinition weapon, float hitDamage, GameObject debuffVfx, Vector3 debuffEuler)
    {
        HitBuildup buildup = FindBuildup(weapon);
        if (buildup == null)
        {
            buildup = new HitBuildup { Weapon = weapon };
            _hits.Add(buildup);
        }

        if (Time.time - buildup.LastHitAt > weapon.HitWindow)
            buildup.Hits = 0;

        buildup.Hits++;
        buildup.LastHitAt = Time.time;

        if (buildup.Hits < weapon.HitsToApplyDot)
            return;

        float dps = weapon.DotDamagePerSecond > 0f ? weapon.DotDamagePerSecond : hitDamage * 0.25f;
        ApplyDot(weapon, dps, debuffVfx, debuffEuler);
    }

    void ApplyDot(WeaponDefinition weapon, float dps, GameObject debuffVfx, Vector3 debuffEuler)
    {
        ActiveDot dot = FindDot(weapon);
        if (dot == null)
        {
            dot = new ActiveDot { Weapon = weapon };
            _dots.Add(dot);
            dot.Vfx = SpawnVfx(debuffVfx, debuffEuler);
        }

        dot.DamagePerSecond = dps;
        dot.TickInterval = weapon.DotTickInterval;
        dot.TickTimer = weapon.DotTickInterval;
        dot.Remaining = weapon.DotDuration;
        if (dot.Vfx == null)
            dot.Vfx = SpawnVfx(debuffVfx, debuffEuler);
    }

    HitBuildup FindBuildup(WeaponDefinition weapon)
    {
        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Weapon == weapon)
                return _hits[i];
        }

        return null;
    }

    ActiveDot FindDot(WeaponDefinition weapon)
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            if (_dots[i].Weapon == weapon)
                return _dots[i];
        }

        return null;
    }

    GameObject SpawnVfx(GameObject prefab, Vector3 euler)
    {
        if (prefab == null)
            return null;

        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(transform.position)
            : transform.up;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(euler);
        GameObject fx = Instantiate(prefab, transform.position + up * VfxHeight, rotation, transform);
        fx.name = prefab.name;
        fx.transform.localScale = Vector3.one;

        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (!systems[i].isPlaying)
                systems[i].Play(true);
        }

        return fx;
    }

    void EndDot(int index)
    {
        ActiveDot dot = _dots[index];
        if (dot.Vfx != null)
            Destroy(dot.Vfx);
        _dots.RemoveAt(index);
    }

    void ClearDots()
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            if (_dots[i].Vfx != null)
                Destroy(_dots[i].Vfx);
        }

        _dots.Clear();
    }
}
