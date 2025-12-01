using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class IntoxicationManager : MonoBehaviour
{
public static IntoxicationManager Instance { get; private set; }
[Header("URP Volume (Global)")]
[Tooltip("Global Volume with a profile. Color Adjustments and Motion Blur will be ensured/created.")]
public Volume globalVolume;

[Header("Stage 1 - Color Adjustments")]
[Tooltip("Target saturation delta (added to base).")]
public float stage1SaturationDelta = 25f;
[Tooltip("Target contrast delta (added to base).")]
public float stage1ContrastDelta = 15f;
[Tooltip("Seconds to blend color changes.")]
public float colorBlendSeconds = 0.4f;

[Header("Stage 2 - Motion Blur (VR caution)")]
[Tooltip("Enable Motion Blur effect at Stage 2.")]
public bool enableMotionBlur = true;
[Range(0f, 1f)] public float motionBlurIntensity = 0.4f;
[Range(0f, 1f)] public float motionBlurClamp = 0.2f;
public MotionBlurQuality motionBlurQuality = MotionBlurQuality.Medium;

[Header("Stage 3 - Blackout UI")]
[Tooltip("CanvasGroup of blackout UI (panel+text+restart button).")]
public CanvasGroup blackoutCanvasGroup;
[Tooltip("Restart button inside blackout UI.")]
public Button restartButton;
[Tooltip("Seconds to fade to black before showing UI.")]
public float blackoutFadeSeconds = 0.8f;

[Header("Restart behavior")]
[Tooltip("Reload scene on restart instead of soft reset.")]
public bool reloadSceneOnRestart = false;
[Tooltip("If reloading, specify a scene name (optional). Leave empty to reload active scene.")]
public string restartSceneName = "";

[Header("Optional control during blackout")]
[Tooltip("Disable listed components on blackout (e.g., movement controllers).")]
public Behaviour[] disableOnBlackout;

// Internal state
int drinkCount = 0;
ColorAdjustments colorAdj;
MotionBlur motionBlur;
float baseSaturation = 0f, baseContrast = 0f;
Coroutine colorRoutine;
Coroutine blackoutRoutine;

void Awake()
{
    if (Instance != null && Instance != this) { Destroy(gameObject); return; }
    Instance = this;
}

void Start()
{
    if (globalVolume == null || globalVolume.profile == null)
    {
        Debug.LogError("IntoxicationManager: Assign a Global Volume with a valid Profile.");
        return;
    }

    // Ensure/lookup overrides
    if (!globalVolume.profile.TryGet(out colorAdj))
        colorAdj = globalVolume.profile.Add<ColorAdjustments>();
    if (!globalVolume.profile.TryGet(out motionBlur))
        motionBlur = globalVolume.profile.Add<MotionBlur>();

    // Record baseline values
    baseSaturation = colorAdj.saturation.value;
    baseContrast = colorAdj.contrast.value;

    // Make sure overrides are active/overridden
    colorAdj.active = true;
    colorAdj.saturation.overrideState = true;
    colorAdj.contrast.overrideState = true;

    motionBlur.active = false; // off initially
    motionBlur.intensity.overrideState = true;
    motionBlur.clamp.overrideState = true;

    // Setup blackout UI
    if (blackoutCanvasGroup != null)
    {
        blackoutCanvasGroup.alpha = 0f;
        blackoutCanvasGroup.interactable = false;
        blackoutCanvasGroup.blocksRaycasts = false;
    }
    if (restartButton != null)
    {
        restartButton.onClick.RemoveAllListeners();
        restartButton.onClick.AddListener(RestartExperience);
    }
}

// Call this from DrinkDetector.OnDrink (UnityEvent) or directly
public void HandleDrink()
{
    if (globalVolume == null || globalVolume.profile == null)
        return;

    drinkCount++;

    if (drinkCount == 1)
    {
        ApplyStage1();
    }
    else if (drinkCount == 2)
    {
        ApplyStage2();
    }
    else if (drinkCount >= 3)
    {
        ApplyStage3();
    }
}

void ApplyStage1()
{
    float targetSat = baseSaturation + stage1SaturationDelta;
    float targetCon = baseContrast + stage1ContrastDelta;
    StartColorBlend(targetSat, targetCon, colorBlendSeconds);
}

void ApplyStage2()
{
    if (!enableMotionBlur || motionBlur == null) return;

    motionBlur.active = true;
    motionBlur.intensity.value = motionBlurIntensity;
    motionBlur.clamp.value = motionBlurClamp;
    motionBlur.quality.value = motionBlurQuality;
}

void ApplyStage3()
{
    if (blackoutCanvasGroup == null)
    {
        Debug.LogWarning("IntoxicationManager: No blackout UI assigned; performing soft reset.");
        RestartExperience();
        return;
    }

    // Disable any listed components (input/movement) during blackout
    if (disableOnBlackout != null)
    {
        for (int i = 0; i < disableOnBlackout.Length; i++)
        {
            if (disableOnBlackout[i] != null) disableOnBlackout[i].enabled = false;
        }
    }

    if (blackoutRoutine != null) StopCoroutine(blackoutRoutine);
    blackoutRoutine = StartCoroutine(BlackoutSequence());
}

void StartColorBlend(float targetSaturation, float targetContrast, float duration)
{
    if (colorRoutine != null) StopCoroutine(colorRoutine);
    colorRoutine = StartCoroutine(ColorBlendRoutine(targetSaturation, targetContrast, duration));
}

IEnumerator ColorBlendRoutine(float targetSaturation, float targetContrast, float duration)
{
    float startSat = colorAdj.saturation.value;
    float startCon = colorAdj.contrast.value;
    float t = 0f;
    duration = Mathf.Max(0.01f, duration);

    while (t < 1f)
    {
        t += Time.deltaTime / duration;
        float tt = Mathf.Clamp01(t);
        colorAdj.saturation.value = Mathf.Lerp(startSat, targetSaturation, tt);
        colorAdj.contrast.value = Mathf.Lerp(startCon, targetContrast, tt);
        yield return null;
    }

    colorAdj.saturation.value = targetSaturation;
    colorAdj.contrast.value = targetContrast;
    colorRoutine = null;
}

IEnumerator BlackoutSequence()
{
    // Fade to black (UI overlay assumed to be a full-screen panel)
    float t = 0f;
    float dur = Mathf.Max(0.01f, blackoutFadeSeconds);
    while (t < 1f)
    {
        t += Time.deltaTime / dur;
        blackoutCanvasGroup.alpha = Mathf.Clamp01(t);
        yield return null;
    }
    blackoutCanvasGroup.alpha = 1f;

    // Enable UI interaction
    blackoutCanvasGroup.interactable = true;
    blackoutCanvasGroup.blocksRaycasts = true;

    blackoutRoutine = null;
}

public void RestartExperience()
{
    // Reload or soft reset
    if (reloadSceneOnRestart)
    {
        string scene = string.IsNullOrEmpty(restartSceneName)
            ? SceneManager.GetActiveScene().name
            : restartSceneName;
        SceneManager.LoadScene(scene);
        return;
    }

    // Soft reset effects
    drinkCount = 0;

    if (colorRoutine != null) { StopCoroutine(colorRoutine); colorRoutine = null; }
    if (blackoutRoutine != null) { StopCoroutine(blackoutRoutine); blackoutRoutine = null; }

    if (colorAdj != null)
    {
        colorAdj.saturation.value = baseSaturation;
        colorAdj.contrast.value = baseContrast;
    }

    if (motionBlur != null)
    {
        motionBlur.active = false;
    }

    if (blackoutCanvasGroup != null)
    {
        blackoutCanvasGroup.alpha = 0f;
        blackoutCanvasGroup.interactable = false;
        blackoutCanvasGroup.blocksRaycasts = false;
    }

    // Re-enable components
    if (disableOnBlackout != null)
    {
        for (int i = 0; i < disableOnBlackout.Length; i++)
        {
            if (disableOnBlackout[i] != null) disableOnBlackout[i].enabled = true;
        }
    }
}
}