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
        public WeaponDotSettings Settings;
        public int Hits;
        public float LastHitAt;
    }

    sealed class ActiveDot
    {
        public WeaponDotSettings Settings;
        public float DamagePerSecond;
        public float TickInterval;
        public float TickTimer;
        public float Remaining;
        public GameObject Vfx;
    }

    readonly List<HitBuildup> _hits = new();
    readonly List<ActiveDot> _dots = new();
    Creature _creature;

    public static void RegisterRepeatedHit(Creature creature, WeaponDotSettings settings, float hitDamage)
    {
        if (creature == null || settings == null || !creature.IsAlive)
            return;
        if (settings.DamagePerSecond <= 0f && hitDamage <= 0f)
            return;

        CreatureAilments host = creature.GetComponent<CreatureAilments>();
        if (host == null)
            host = creature.gameObject.AddComponent<CreatureAilments>();
        host.OnRepeatedHit(settings, hitDamage);
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
        if (_creature == null)
            _creature = GetComponent<Creature>();
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

    void OnRepeatedHit(WeaponDotSettings settings, float hitDamage)
    {
        HitBuildup buildup = FindBuildup(settings);
        if (buildup == null)
        {
            buildup = new HitBuildup { Settings = settings };
            _hits.Add(buildup);
        }

        if (Time.time - buildup.LastHitAt > settings.HitWindow)
            buildup.Hits = 0;

        buildup.Hits++;
        buildup.LastHitAt = Time.time;

        if (buildup.Hits < settings.HitsToApply)
            return;

        float dps = settings.DamagePerSecond > 0f ? settings.DamagePerSecond : hitDamage * 0.25f;
        ApplyDot(settings, dps);
    }

    void ApplyDot(WeaponDotSettings settings, float dps)
    {
        ActiveDot dot = FindDot(settings);
        if (dot == null)
        {
            dot = new ActiveDot { Settings = settings };
            _dots.Add(dot);
            dot.Vfx = SpawnVfx(settings.DebuffVFX, settings.DebuffEuler);
        }

        dot.DamagePerSecond = dps;
        dot.TickInterval = settings.TickInterval;
        dot.TickTimer = settings.TickInterval;
        dot.Remaining = settings.Duration;
        if (dot.Vfx == null)
            dot.Vfx = SpawnVfx(settings.DebuffVFX, settings.DebuffEuler);
    }

    HitBuildup FindBuildup(WeaponDotSettings settings)
    {
        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Settings == settings)
                return _hits[i];
        }

        return null;
    }

    ActiveDot FindDot(WeaponDotSettings settings)
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            if (_dots[i].Settings == settings)
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
