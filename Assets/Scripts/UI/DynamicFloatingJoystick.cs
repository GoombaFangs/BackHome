using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StarterAssets;

/// <summary>
/// Floating move joystick: appears under the finger/mouse on press, hides on release.
/// Place on a full-screen UI object under the mobile canvas (created by MobileInputBinder).
/// </summary>
public class DynamicFloatingJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] RectTransform joystickRoot;
    [SerializeField] RectTransform handleRect;
    [SerializeField] UICanvasControllerInput canvasInput;
    [SerializeField] float joystickRange = 80f;
    [SerializeField] bool invertY;

    RectTransform _canvasRect;
    Canvas _canvas;
    bool _dragging;
    UIVirtualJoystick _stockJoystick;

    public void Setup(RectTransform moveJoystick, UICanvasControllerInput input)
    {
        joystickRoot = moveJoystick;
        canvasInput = input;
        ResolveHandle();
        PrepareJoystickForFloating();
        HideJoystick();
    }

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        _canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;

        if (canvasInput == null)
            canvasInput = GetComponentInParent<UICanvasControllerInput>();

        ResolveHandle();
        PrepareJoystickForFloating();
        HideJoystick();
    }

    void ResolveHandle()
    {
        if (joystickRoot == null)
            return;

        _stockJoystick = joystickRoot.GetComponent<UIVirtualJoystick>();
        if (handleRect == null && _stockJoystick != null)
            handleRect = _stockJoystick.handleRect;

        if (handleRect == null && joystickRoot.childCount > 0)
            handleRect = joystickRoot.GetChild(0) as RectTransform;
    }

    void PrepareJoystickForFloating()
    {
        if (joystickRoot == null)
            return;

        if (_stockJoystick != null)
            _stockJoystick.enabled = false;

        foreach (Image img in joystickRoot.GetComponentsInChildren<Image>(true))
            img.raycastTarget = false;

        joystickRoot.anchorMin = new Vector2(0.5f, 0.5f);
        joystickRoot.anchorMax = new Vector2(0.5f, 0.5f);
        joystickRoot.pivot = new Vector2(0.5f, 0.5f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (joystickRoot == null || canvasInput == null)
            return;

        _dragging = true;
        PlaceJoystick(eventData);
        joystickRoot.gameObject.SetActive(true);
        UpdateJoystick(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        UpdateJoystick(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        _dragging = false;
        canvasInput.VirtualMoveInput(Vector2.zero);

        if (handleRect != null)
            handleRect.anchoredPosition = Vector2.zero;

        HideJoystick();
    }

    void PlaceJoystick(PointerEventData eventData)
    {
        if (_canvasRect == null)
            _canvasRect = GetComponentInParent<Canvas>().transform as RectTransform;

        Camera cam = GetEventCamera();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                eventData.position,
                cam,
                out Vector2 localPoint))
        {
            joystickRoot.anchoredPosition = localPoint;
        }
    }

    void UpdateJoystick(PointerEventData eventData)
    {
        Camera cam = GetEventCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                joystickRoot,
                eventData.position,
                cam,
                out Vector2 localPoint))
            return;

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, joystickRange);
        if (handleRect != null)
            handleRect.anchoredPosition = clamped;

        Vector2 output = clamped / joystickRange;
        if (invertY)
            output.y = -output.y;

        canvasInput.VirtualMoveInput(output);
    }

    Camera GetEventCamera()
    {
        if (_canvas == null)
            _canvas = GetComponentInParent<Canvas>();

        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            return _canvas.worldCamera;

        return null;
    }

    void HideJoystick()
    {
        if (joystickRoot != null)
            joystickRoot.gameObject.SetActive(false);

        if (handleRect != null)
            handleRect.anchoredPosition = Vector2.zero;
    }
}
