using UnityEngine;
using UnityEngine.UI;
using StarterAssets;

/// <summary>
/// Wires mobile move input, hides unused controls, and enables a floating joystick.
/// </summary>
public class MobileInputBinder : MonoBehaviour
{
    [SerializeField] UICanvasControllerInput canvasInput;
    [SerializeField] StarterAssetsInputs playerInputs;

    void Awake()
    {
        if (canvasInput == null)
            canvasInput = GetComponent<UICanvasControllerInput>();

        if (playerInputs == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerInputs = player.GetComponent<StarterAssetsInputs>();
        }

        HideUnusedControls();
        SetupFloatingJoystick();

        if (canvasInput == null || playerInputs == null)
        {
            Debug.LogWarning("MobileInputBinder: missing canvas input or player StarterAssetsInputs.");
            return;
        }

        canvasInput.starterAssetsInputs = playerInputs;
        playerInputs.cursorLocked = false;
        playerInputs.cursorInputForLook = false;
        playerInputs.analogMovement = true;
        playerInputs.jump = false;
        playerInputs.sprint = false;
        playerInputs.look = Vector2.zero;
    }

    void HideUnusedControls()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            string lower = child.name.ToLowerInvariant();
            if (lower.Contains("look") || lower.Contains("jump") || lower.Contains("sprint"))
                child.gameObject.SetActive(false);
        }
    }

    void SetupFloatingJoystick()
    {
        RectTransform moveJoystick = FindMoveJoystick();
        if (moveJoystick == null || canvasInput == null)
            return;

        // Full-screen invisible pad that owns press/drag/release.
        Transform existing = transform.Find("FloatingTouchPad");
        GameObject padObject = existing != null ? existing.gameObject : new GameObject("FloatingTouchPad", typeof(RectTransform));
        if (existing == null)
            padObject.transform.SetParent(transform, false);

        RectTransform padRect = padObject.GetComponent<RectTransform>();
        padRect.anchorMin = Vector2.zero;
        padRect.anchorMax = Vector2.one;
        padRect.offsetMin = Vector2.zero;
        padRect.offsetMax = Vector2.zero;
        padRect.pivot = new Vector2(0.5f, 0.5f);
        padObject.transform.SetAsFirstSibling();

        Image padImage = padObject.GetComponent<Image>();
        if (padImage == null)
            padImage = padObject.AddComponent<Image>();
        padImage.color = new Color(0f, 0f, 0f, 0f);
        padImage.raycastTarget = true;

        DynamicFloatingJoystick floating = padObject.GetComponent<DynamicFloatingJoystick>();
        if (floating == null)
            floating = padObject.AddComponent<DynamicFloatingJoystick>();

        floating.Setup(moveJoystick, canvasInput);
    }

    RectTransform FindMoveJoystick()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            string lower = child.name.ToLowerInvariant();
            if (lower.Contains("look") || lower.Contains("jump") || lower.Contains("sprint"))
                continue;

            if (lower.Contains("joystick") || lower.Contains("move"))
                return child as RectTransform;
        }

        return null;
    }
}
