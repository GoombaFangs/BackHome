using UnityEngine;

/// <summary>
/// Damages the player when they are inside this creature's attack range.
/// Spawns / drives a shared <see cref="AttackRangeIndicator"/> (Ranger) so visual radius matches combat.
/// Instant hit on enter, then ticks every 1 / AttackSpeed seconds while the player stays inside.
/// </summary>
[RequireComponent(typeof(Creature))]
public class CreatureRangeCombat : MonoBehaviour
{
    [SerializeField] AttackRangeIndicator rangeIndicator;
    [Tooltip("Ranger prefab (AttackRangeIndicator). Instantiated if none is already a child.")]
    [SerializeField] GameObject rangerPrefab;
    [SerializeField] Color rangeColor = new Color(1f, 0.35f, 0.22f, 0.55f);

    Creature _creature;
    CreatureAnimator _anim;
    CreatureChase _chase;
    PlayerVitals _player;
    float _tickCooldown;
    bool _playerInside;
    bool _hitPlayerThisFrame;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _anim = GetComponent<CreatureAnimator>();
        if (_anim == null)
            _anim = GetComponentInChildren<CreatureAnimator>();
        _chase = GetComponent<CreatureChase>();
        EnsureRanger();
    }

    void Update()
    {
        _hitPlayerThisFrame = false;

        if (_creature == null || !_creature.IsAlive)
        {
            _playerInside = false;
            _anim?.SetAttacking(false);
            if (rangeIndicator != null)
                rangeIndicator.gameObject.SetActive(false);
            return;
        }

        if (rangeIndicator != null && !rangeIndicator.gameObject.activeSelf)
            rangeIndicator.gameObject.SetActive(true);

        // No damage / attack anim while idle or returning home.
        if (_chase != null && !_chase.IsAggroed)
        {
            _playerInside = false;
            _anim?.SetAttacking(false);
            return;
        }

        float attackSpeed = _creature.AttackSpeed;
        float damage = _creature.AttackDamage;
        float radius = ResolveRadius();
        if (attackSpeed <= 0f || damage <= 0f || radius <= 0f)
        {
            _anim?.SetAttacking(false);
            return;
        }

        if (rangeIndicator != null)
            rangeIndicator.SetRadius(radius);

        if (!TryResolvePlayer(out PlayerVitals player) || !player.IsAlive)
        {
            _playerInside = false;
            _anim?.SetAttacking(false);
            return;
        }

        bool inside = IsTargetInRange(player.transform.position, radius);
        if (inside)
        {
            if (!_playerInside)
            {
                player.TakeDamage(damage);
                _hitPlayerThisFrame = true;
            }

            _playerInside = true;
        }
        else
        {
            _playerInside = false;
        }

        _anim?.SetAttacking(_playerInside);
        if (_playerInside)
            _anim?.SetAttackRate(attackSpeed);

        float interval = 1f / attackSpeed;
        _tickCooldown -= Time.deltaTime;
        if (_tickCooldown > 0f)
            return;

        _tickCooldown = interval;

        if (_playerInside && !_hitPlayerThisFrame && player.IsAlive)
            player.TakeDamage(damage);
    }

    float ResolveRadius()
    {
        if (_creature.AttackRange > 0f)
            return _creature.AttackRange;
        if (rangeIndicator != null)
            return rangeIndicator.GetCombatRadius();
        return 0f;
    }

    void EnsureRanger()
    {
        if (rangeIndicator == null)
            rangeIndicator = GetComponentInChildren<AttackRangeIndicator>(true);

        if (rangeIndicator != null)
        {
            rangeIndicator.SetColor(rangeColor);
            return;
        }

        if (rangerPrefab == null)
            return;

        GameObject instance = Instantiate(rangerPrefab, transform, false);
        instance.name = "Ranger";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        rangeIndicator = instance.GetComponent<AttackRangeIndicator>();
        if (rangeIndicator == null)
            rangeIndicator = instance.GetComponentInChildren<AttackRangeIndicator>(true);

        if (rangeIndicator != null)
            rangeIndicator.SetColor(rangeColor);
    }

    bool TryResolvePlayer(out PlayerVitals player)
    {
        if (_player != null)
        {
            player = _player;
            return true;
        }

        _player = FindAnyObjectByType<PlayerVitals>();
        player = _player;
        return player != null;
    }

    bool IsTargetInRange(Vector3 targetPosition, float radius)
    {
        Vector3 origin = transform.position;
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(origin)
            : transform.up;

        Vector3 planar = Vector3.ProjectOnPlane(targetPosition - origin, up);
        return planar.sqrMagnitude <= radius * radius;
    }
}
