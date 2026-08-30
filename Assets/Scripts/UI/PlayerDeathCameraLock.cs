using System.Collections;
using UnityEngine;

/// <summary>
/// After the player dies, the normal follow camera (<see cref="CameraFollow"/>) can keep
/// reacting to the death moment - falling, ragdolling, snapping angles - which reads as the
/// camera "going crazy". Instead:
/// <list type="number">
/// <item><see cref="BeginDeathFall"/> - called the instant death happens - disables
/// <see cref="CameraFollow"/> and plays one small, calm, scripted settle (a slight dip +
/// downward tilt), nothing chaotic.</item>
/// <item><see cref="Lock"/> - called a couple seconds later - stops that settle wherever it
/// is and fades in a hazy, muted overlay so the moment reads as an intentional "dazed" beat.</item>
/// </list>
/// Lives on the Hud (like <see cref="PlayerDeathUI"/>), which owns the single death timer and
/// calls both methods at the right moments - this component runs no delay of its own, so the
/// camera state, the overlay, the death panel and the time freeze can never drift apart.
/// </summary>
public class PlayerDeathCameraLock : MonoBehaviour
{
    const string OverlayResourcesPath = "Player/Shaders/DeathDazedOverlay";

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    [Header("Death Fall")]
    [Tooltip("A small, calm settle - not a dramatic swoop. Seconds to reach the resting pose.")]
    [SerializeField, Min(0.05f)] float fallDuration = 1.6f;
    [Tooltip("World-space drop (toward the ground) while settling.")]
    [SerializeField, Min(0f)] float fallDrop = 0.5f;
    [Tooltip("Slight downward nod while settling, in degrees.")]
    [SerializeField, Min(0f)] float fallTiltDegrees = 3.5f;

    [Header("Dazed Overlay")]
    [SerializeField, Min(0.05f)] float fadeIn = 0.9f;
    [SerializeField] Material overlayMaterial;

    CameraFollow _follow;
    Camera _camera;
    Material _runtime;
    Transform _overlay;
    MeshRenderer _renderer;
    Coroutine _routine;
    Coroutine _fallRoutine;
    float _lastFov = -1f;
    float _lastAspect = -1f;

    void LateUpdate()
    {
        // Keep the overlay covering the screen even while it fades in (FOV can still change
        // right up to the lock, e.g. from motion framing settling out).
        if (_overlay != null && _overlay.gameObject.activeSelf)
            FitToCamera();
    }

    void OnDisable()
    {
        StopFallRoutine();
        StopRoutine();
    }

    void OnDestroy()
    {
        StopFallRoutine();
        StopRoutine();

        if (_overlay != null)
        {
            Destroy(_overlay.gameObject);
            _overlay = null;
            _renderer = null;
        }

        if (_runtime != null)
        {
            Destroy(_runtime);
            _runtime = null;
        }
    }

    /// <summary>
    /// Call the instant death happens. Takes the camera away from <see cref="CameraFollow"/>
    /// and plays one small, calm settle toward the ground. Idempotent.
    /// </summary>
    public void BeginDeathFall()
    {
        if (_fallRoutine != null)
            return;

        DisableFollow();

        Camera cam = ResolveCamera();
        if (cam == null)
            return;

        _fallRoutine = StartCoroutine(DeathFallRoutine(cam));
    }

    /// <summary>
    /// Call a beat after <see cref="BeginDeathFall"/>. Freezes the camera exactly where it is
    /// (stopping the settle if it's still playing) and starts fading in the dazed overlay.
    /// Idempotent.
    /// </summary>
    public void Lock()
    {
        StopFallRoutine();
        DisableFollow();

        if (_routine != null)
            return;

        if (_runtime == null && !EnsureMaterial())
            return;

        EnsureOverlay();
        if (_overlay != null)
            _overlay.gameObject.SetActive(true);
        if (_renderer != null)
            _renderer.enabled = true;

        _routine = StartCoroutine(FadeIn());
    }

    void StopRoutine()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
    }

    void StopFallRoutine()
    {
        if (_fallRoutine != null)
        {
            StopCoroutine(_fallRoutine);
            _fallRoutine = null;
        }
    }

    IEnumerator DeathFallRoutine(Camera cam)
    {
        Vector3 startPos = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;

        Vector3 down = Vector3.down;
        if (SphericalPlanet.Instance != null)
            down = -SphericalPlanet.Instance.GetUpAt(startPos);

        Vector3 endPos = startPos + down * fallDrop;
        Quaternion endRot = startRot * Quaternion.AngleAxis(fallTiltDegrees, Vector3.right);

        float t = 0f;
        while (t < fallDuration)
        {
            t += Time.deltaTime;
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / fallDuration), 3f);
            cam.transform.position = Vector3.Lerp(startPos, endPos, eased);
            cam.transform.rotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        cam.transform.position = endPos;
        cam.transform.rotation = endRot;
        _fallRoutine = null;
    }

    IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            ApplyIntensity(Smooth(Mathf.Clamp01(t / fadeIn)));
            yield return null;
        }

        ApplyIntensity(1f);
        _routine = null;
    }

    void DisableFollow()
    {
        if (_follow == null)
            _follow = FindAnyObjectByType<CameraFollow>();
        if (_follow == null && Camera.main != null)
            _follow = Camera.main.GetComponent<CameraFollow>();

        if (_follow != null)
            _follow.enabled = false;
    }

    void ApplyIntensity(float amount)
    {
        if (_runtime != null)
            _runtime.SetFloat(IntensityId, amount);
    }

    bool EnsureMaterial()
    {
        if (_runtime != null)
            return true;

        Material source = overlayMaterial;
        if (source == null)
            source = Resources.Load<Material>(OverlayResourcesPath);
        if (source == null)
        {
            Debug.LogWarning($"{name}: DeathDazedOverlay material missing at Resources/{OverlayResourcesPath}.", this);
            return false;
        }

        _runtime = new Material(source);
        _runtime.name = source.name + " (Runtime)";
        overlayMaterial = source;
        return true;
    }

    void EnsureOverlay()
    {
        Camera cam = ResolveCamera();
        if (cam == null)
            return;

        if (_overlay == null)
        {
            var go = new GameObject("DeathDazedOverlay");
            _overlay = go.transform;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuad();
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.sharedMaterial = _runtime;
        }

        if (_overlay != null && cam != null && _overlay.parent != cam.transform)
            _overlay.SetParent(cam.transform, false);

        _overlay.localRotation = Quaternion.identity;
        FitToCamera(force: true);
    }

    void FitToCamera(bool force = false)
    {
        Camera cam = ResolveCamera();
        if (cam == null || _overlay == null)
            return;

        float fov = cam.fieldOfView;
        float aspect = cam.aspect;
        if (!force && Mathf.Abs(fov - _lastFov) < 0.01f && Mathf.Abs(aspect - _lastAspect) < 0.001f)
            return;

        _lastFov = fov;
        _lastAspect = aspect;

        float z = cam.nearClipPlane + 0.02f;
        float height = 2f * z * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        _overlay.localPosition = new Vector3(0f, 0f, z);
        _overlay.localScale = new Vector3(height * aspect, height, 1f);
    }

    Camera ResolveCamera()
    {
        if (_camera != null)
            return _camera;

        _camera = Camera.main;
        if (_camera == null)
            _camera = FindAnyObjectByType<Camera>();
        return _camera;
    }

    static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "DeathDazedOverlayQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
