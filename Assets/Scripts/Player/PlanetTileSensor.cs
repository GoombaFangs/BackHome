using UnityEngine;

/// <summary>
/// Tracks which planet tile the player is standing on (gameplay query helper).
/// </summary>
public class PlanetTileSensor : MonoBehaviour
{
    [SerializeField] float pollInterval = 0.15f;

    PlanetTileMap _map;
    float _nextPoll;
    PlanetTileMap.TileSample _current;
    bool _hasSample;

    public bool HasSample => _hasSample;
    public string CurrentTileId => _hasSample ? _current.tileId : string.Empty;
    public string CurrentZoneId => _hasSample ? _current.zoneId : string.Empty;
    public bool CurrentWalkable => !_hasSample || _current.walkable;

    void Update()
    {
        if (Time.time < _nextPoll)
            return;

        _nextPoll = Time.time + Mathf.Max(0.05f, pollInterval);
        Refresh();
    }

    public void Refresh()
    {
        if (_map == null)
            _map = FindFirstObjectByType<PlanetTileMap>();

        if (_map == null)
        {
            _hasSample = false;
            return;
        }

        _hasSample = _map.TryGetTile(transform.position, out _current);
    }
}
