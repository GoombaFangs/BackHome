using UnityEngine;

/// <summary>
/// Flies from a ranged weapon muzzle toward a creature. Damage is applied only on impact.
/// </summary>
public class RangedBullet : MonoBehaviour
{
    RangedWeapon _weapon;
    Creature _target;
    float _damage;
    Vector3 _knockFrom;
    float _speed;
    float _hitRadius;
    float _lifetime;
    Vector3 _euler;
    bool _armed;
    bool _spent;

    public void Launch(
        RangedWeapon weapon,
        Creature target,
        float damage,
        Vector3 knockFrom,
        float speed,
        float hitRadius,
        float lifetime,
        Vector3 euler)
    {
        _weapon = weapon;
        _target = target;
        _damage = damage;
        _knockFrom = knockFrom;
        _speed = Mathf.Max(0.1f, speed);
        _hitRadius = Mathf.Max(0.05f, hitRadius);
        _lifetime = Mathf.Max(0.05f, lifetime);
        _euler = euler;
        _armed = true;
        _spent = false;
    }

    void Update()
    {
        if (!_armed || _spent)
            return;

        _lifetime -= Time.deltaTime;
        if (_lifetime <= 0f || _weapon == null || _target == null || !_target.IsAlive)
        {
            Expire();
            return;
        }

        Vector3 aim = _weapon.GetBulletAim(_target);
        Vector3 toAim = aim - transform.position;
        float distance = toAim.magnitude;
        Vector3 dir = distance > 0.0001f ? toAim / distance : transform.forward;
        Face(dir);

        if (distance <= _hitRadius)
        {
            Impact(aim, dir);
            return;
        }

        float step = _speed * Time.deltaTime;
        if (step >= distance - _hitRadius)
        {
            transform.position = aim;
            Impact(aim, dir);
            return;
        }

        transform.position += dir * step;
    }

    void Face(Vector3 dir)
    {
        Vector3 up = RangedWeapon.GetBulletUp(transform.position, dir);
        transform.rotation = Quaternion.LookRotation(dir, up) * Quaternion.Euler(_euler);
    }

    void Impact(Vector3 hitPoint, Vector3 dir)
    {
        if (_spent)
            return;

        _spent = true;
        _weapon?.ResolveBulletHit(_target, _damage, _knockFrom, hitPoint, dir);
        Destroy(gameObject);
    }

    void Expire()
    {
        if (_spent)
            return;

        _spent = true;
        Destroy(gameObject);
    }
}
