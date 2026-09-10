using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Gives Bitgem water the Example-Scene-01 look on Nyxara: sky-blue reflections
/// and camera depth/opaque textures for shoreline foam. Does not change the space skybox.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public sealed class CasualWaterEnvironment : MonoBehaviour
{
    static readonly Color ExampleSky = new Color(0.4666667f, 0.7372549f, 0.77647066f, 0f);

    const string ProbeName = "SeaReflectionProbe";
    const float ProbeSize = 400f;

    ReflectionProbe _probe;

    void OnEnable()
    {
        EnsureProbe();
        BindSun();
        EnableCameraSceneTextures();
    }

    void OnDisable()
    {
        if (_probe == null)
            return;

        GameObject go = _probe.gameObject;
        _probe = null;
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    void EnsureProbe()
    {
        Transform existing = transform.Find(ProbeName);
        ReflectionProbe probe = existing != null ? existing.GetComponent<ReflectionProbe>() : null;
        if (probe == null)
        {
            var go = new GameObject(ProbeName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.hideFlags = HideFlags.DontSave;
            probe = go.AddComponent<ReflectionProbe>();
        }

        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 128;
        probe.size = Vector3.one * (ProbeSize / Mathf.Max(0.01f, transform.lossyScale.x));
        probe.center = Vector3.zero;
        probe.nearClipPlane = 0.3f;
        probe.farClipPlane = 1000f;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = ExampleSky;
        probe.cullingMask = 0;
        probe.intensity = 1.8f;
        probe.hdr = true;
        probe.importance = 10;
        probe.boxProjection = false;
        _probe = probe;
        probe.RenderProbe();
    }

    static void BindSun()
    {
        if (RenderSettings.sun != null)
            return;

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
            {
                RenderSettings.sun = lights[i];
                return;
            }
        }
    }

    static void EnableCameraSceneTextures()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data == null)
            return;

        data.requiresDepthOption = CameraOverrideOption.On;
        data.requiresColorOption = CameraOverrideOption.On;
    }
}
