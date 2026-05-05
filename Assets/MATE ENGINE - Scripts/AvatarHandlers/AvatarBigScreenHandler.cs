using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class AvatarBigScreenHandler : MonoBehaviour
{
    [Header("Keybinds")]
    public List<KeyCode> ToggleKeys = new List<KeyCode> { KeyCode.B };

    [Header("Animator & Bone Selection")]
    public Animator avatarAnimator;
    public HumanBodyBones attachBone = HumanBodyBones.Head;

    [Header("Camera")]
    public Camera MainCamera;
    [Tooltip("Override for Zoom: Camera FOV (Perspective) or Size (Orthographic). 0 = auto.")]
    public float TargetZoom = 0f;
    public float ZoomMoveSpeed = 10f;
    [Tooltip("Y-Offset to bone position (meters, before scaling)")]
    public float YOffset = 0.08f;

    [Header("Fade Animation")]
    public float FadeYOffset = 0.5f;
    public float FadeInDuration = 0.5f;
    public float FadeOutDuration = 0.5f;

    [Header("Canvas Blocking")]
    public GameObject moveCanvas;

    private IntPtr unityWindow = IntPtr.Zero;
    private bool isBigScreenActive = false;
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    private float originalFOV;
    private float originalOrthoSize;
    private RectInt originalWindowRect;
    private bool originalRectSet = false;
    private Transform bone;
    private AvatarAnimatorController avatarAnimatorController;
    private bool moveCanvasWasActive = false;
    private Coroutine fadeCoroutine;
    private bool isFading = false;
    private bool isInDesktopTransition = false;

    public static List<AvatarBigScreenHandler> ActiveHandlers = new List<AvatarBigScreenHandler>();
    private static readonly int IsBigScreen = Animator.StringToHash("isBigScreen");

    public bool IsBigScreenActive => isBigScreenActive;

    void OnEnable()
    {
        if (!ActiveHandlers.Contains(this))
            ActiveHandlers.Add(this);
    }
    void OnDisable()
    {
        ActiveHandlers.Remove(this);
    }

    public void ToggleBigScreenFromUI()
    {
        if (!isBigScreenActive)
            ActivateBigScreen();
        else
            DeactivateBigScreen();
    }

    void Start()
    {
        if (WindowManager.Instance != null)
        {
            unityWindow = WindowManager.Instance.UnityWindow;
        }
        if (MainCamera == null) MainCamera = Camera.main;
        if (avatarAnimator == null) avatarAnimator = GetComponent<Animator>();
        if (MainCamera != null)
        {
            originalCamPos = MainCamera.transform.position;
            originalCamRot = MainCamera.transform.rotation;
            originalFOV = MainCamera.fieldOfView;
            originalOrthoSize = MainCamera.orthographicSize;
        }
        if (unityWindow != IntPtr.Zero && WindowManager.Instance.GetWindowRect(unityWindow, out RectInt r))
        {
            originalWindowRect = r;
            originalRectSet = true;
        }
        avatarAnimatorController = GetComponent<AvatarAnimatorController>();
    }

    public void SetAnimator(Animator a) => avatarAnimator = a;

    void Update()
    {
        foreach (var key in ToggleKeys)
        {
            if (Input.GetKeyDown(key))
            {
                if (!isBigScreenActive && !isFading)
                    ActivateBigScreen();
                else if (isBigScreenActive && !isFading)
                    DeactivateBigScreen();
                break;
            }
        }
        if (isBigScreenActive && MainCamera != null && bone != null && avatarAnimator != null && !isFading && !isInDesktopTransition)
            UpdateBigScreenCamera();
    }

    void UpdateBigScreenCamera()
    {
        var scale = avatarAnimator.transform.lossyScale.y;
        var headPos = bone.position;
        var neck = avatarAnimator.GetBoneTransform(HumanBodyBones.Neck);
        float headHeight = Mathf.Max(0.12f, neck ? Mathf.Abs(headPos.y - neck.position.y) : 0.25f) * scale;
        float buffer = 1.4f;

        Vector3 camPos = originalCamPos;
        camPos.y = headPos.y + YOffset * scale;
        MainCamera.transform.position = camPos;
        MainCamera.transform.rotation = Quaternion.identity;

        if (TargetZoom > 0f)
        {
            if (MainCamera.orthographic) MainCamera.orthographicSize = TargetZoom * scale;
            else MainCamera.fieldOfView = TargetZoom;
        }
        else
        {
            if (MainCamera.orthographic)
                MainCamera.orthographicSize = headHeight * buffer;
            else
            {
                float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                MainCamera.fieldOfView = Mathf.Clamp(
                    2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg, 10f, 60f);
            }
        }
    }

    void ActivateBigScreen()
    {
        if (isBigScreenActive) return;
        SaveCameraState();

        if (moveCanvas != null)
            moveCanvasWasActive = moveCanvas.activeSelf;

        isBigScreenActive = true;
        if (avatarAnimator != null) avatarAnimator.SetBool(IsBigScreen, true);
        if (avatarAnimatorController != null) avatarAnimatorController.BlockDraggingOverride = true;
        if (moveCanvas != null && moveCanvas.activeSelf) moveCanvas.SetActive(false);

        bone = avatarAnimator ? avatarAnimator.GetBoneTransform(attachBone) : null;
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(BigScreenEnterSequence());
    }

    void DeactivateBigScreen()
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(BigScreenExitSequence());
    }

    void SaveCameraState()
    {
        if (MainCamera != null)
        {
            originalCamPos = MainCamera.transform.position;
            originalCamRot = MainCamera.transform.rotation;
            originalFOV = MainCamera.fieldOfView;
            originalOrthoSize = MainCamera.orthographicSize;
        }
    }

    IEnumerator FadeCameraY(bool fadeIn)
    {
        isFading = true;
        if (avatarAnimator == null || bone == null || MainCamera == null)
        { isFading = false; yield break; }

        var scale = avatarAnimator.transform.lossyScale.y;
        var headPos = bone.position;
        float baseY = headPos.y + YOffset * scale;
        float fadeY = baseY + FadeYOffset;

        Vector3 camPos = MainCamera.transform.position;
        float fromY = fadeIn ? fadeY : baseY;
        float toY = fadeIn ? baseY : fadeY;
        float duration = fadeIn ? FadeInDuration : FadeOutDuration;
        float time = 0f;

        var neck = avatarAnimator.GetBoneTransform(HumanBodyBones.Neck);
        float headHeight = Mathf.Max(0.12f, neck ? Mathf.Abs(headPos.y - neck.position.y) : 0.25f) * scale;
        float buffer = 1.4f;

        while (time < duration)
        {
            float curve = Mathf.SmoothStep(0, 1, time / duration);
            camPos.y = Mathf.Lerp(fromY, toY, curve);
            MainCamera.transform.position = camPos;
            MainCamera.transform.rotation = Quaternion.identity;
            if (TargetZoom > 0f)
            {
                if (MainCamera.orthographic) MainCamera.orthographicSize = TargetZoom * scale;
                else MainCamera.fieldOfView = TargetZoom;
            }
            else
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = headHeight * buffer;
                else
                {
                    float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                    MainCamera.fieldOfView = Mathf.Clamp(
                        2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg, 10f, 60f);
                }
            }
            time += Time.deltaTime;
            yield return null;
        }

        camPos.y = toY;
        MainCamera.transform.position = camPos;
        MainCamera.transform.rotation = Quaternion.identity;
        if (TargetZoom > 0f)
        {
            if (MainCamera.orthographic) MainCamera.orthographicSize = TargetZoom * scale;
            else MainCamera.fieldOfView = TargetZoom;
        }
        else
        {
            if (MainCamera.orthographic)
                MainCamera.orthographicSize = headHeight * buffer;
            else
            {
                float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                MainCamera.fieldOfView = Mathf.Clamp(
                    2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg, 10f, 60f);
            }
        }
        isFading = false;

        if (!fadeIn)
        {
            isBigScreenActive = false;
            if (avatarAnimator != null) avatarAnimator.SetBool(IsBigScreen, false);
            if (avatarAnimatorController != null) avatarAnimatorController.BlockDraggingOverride = false;
            if (moveCanvas != null && moveCanvasWasActive) moveCanvas.SetActive(true);
            if (unityWindow != IntPtr.Zero && originalRectSet)
            {
                WindowManager.Instance.SetWindowPosition(originalWindowRect.position);
                WindowManager.Instance.SetWindowSize(new Vector2Int(originalWindowRect.width, originalWindowRect.height));
            }
            if (MainCamera != null)
            {
                MainCamera.transform.position = originalCamPos;
                MainCamera.transform.rotation = originalCamRot;
                MainCamera.fieldOfView = originalFOV;
                MainCamera.orthographicSize = originalOrthoSize;
            }
        }
    }
    
    private RectInt FindBestMonitorRect(RectInt windowRect)
    {
        if (WindowManager.Instance == null) return new RectInt(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
        
        List<RectInt> monitorRects = WindowManager.Instance.GetAllMonitors().Values.ToList();
        int idx = 0;
        float maxArea = 0;
        for (int i = 0; i < monitorRects.Count; i++)
        {
            float overlap = OverlapArea(windowRect, monitorRects[i]);
            if (overlap > maxArea) { idx = i; maxArea = overlap; }
        }
        return new(new((monitorRects[idx].x + monitorRects[idx].width) / 2 - windowRect.width / 2, (monitorRects[idx].y + monitorRects[idx].height) / 2 - windowRect.height / 2), monitorRects[idx].size);
    }

    float OverlapArea(RectInt a, RectInt b)
    {
        float x1 = Mathf.Max(a.x, b.x), x2 = Mathf.Min(a.x + a.width, b.x + b.width);
        float y1 = Mathf.Max(a.y, b.y), y2 = Mathf.Min(a.y + a.height, b.y + b.height);
        float w = x2 - x1, h = y2 - y1;
        return (w > 0 && h > 0) ? w * h : 0;
    }

    IEnumerator GlideAvatarDesktop(float duration, bool toFadeY)
    {
        isInDesktopTransition = true;
        if (avatarAnimator == null || bone == null || MainCamera == null)
        { isInDesktopTransition = false; yield break; }

        var scale = avatarAnimator.transform.lossyScale.y;
        var headPos = bone.position;
        float baseY = headPos.y + YOffset * scale;
        float fadeY = baseY + FadeYOffset;

        Vector3 camPos = MainCamera.transform.position;
        float fromY = toFadeY ? baseY : fadeY;
        float toY = toFadeY ? fadeY : baseY;
        float time = 0f;

        while (time < duration)
        {
            camPos.y = Mathf.Lerp(fromY, toY, Mathf.SmoothStep(0, 1, time / duration));
            MainCamera.transform.position = camPos;
            time += Time.deltaTime;
            yield return null;
        }
        camPos.y = toY;
        MainCamera.transform.position = camPos;

        if (toFadeY && unityWindow != IntPtr.Zero)
        {
            if (WindowManager.Instance.GetWindowRect(unityWindow, out RectInt windowRect))
            {
                RectInt targetScreen = FindBestMonitorRect(windowRect);
                WindowManager.Instance.SetWindowPosition(targetScreen.x, targetScreen.height - windowRect.height);
                originalWindowRect = windowRect;
                originalRectSet = true;
            }
        }
        if (!toFadeY && MainCamera != null)
        {
            MainCamera.transform.position = originalCamPos;
            MainCamera.transform.rotation = originalCamRot;
            MainCamera.fieldOfView = originalFOV;
            MainCamera.orthographicSize = originalOrthoSize;
        }
        isInDesktopTransition = false;
    }

    IEnumerator BigScreenEnterSequence()
    {
        yield return StartCoroutine(GlideAvatarDesktop(0.4f, true));
        yield return StartCoroutine(FadeCameraY(true));
    }
    IEnumerator BigScreenExitSequence()
    {
        yield return StartCoroutine(FadeCameraY(false));
        yield return StartCoroutine(GlideAvatarDesktop(0.4f, false));
    }
}