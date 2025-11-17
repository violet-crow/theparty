using UnityEngine;
using Autohand;
using System.Collections;

public class ShowCreditsOnGrab : MonoBehaviour
{
    public GameObject creditsScreenPrefab;
    public float distanceFromHead = 0.5f;   // 0.4–0.7m usually fills view
    public float fadeDuration = 1.5f;       // how long to fade in
    public bool useUnscaledTime = true;     // true = unaffected by timescale
    public bool oneShot = true;
    public bool destroyObjectAfter = false;
    bool shown;
    public void OnGrabFromAutoHand(Hand hand)
    {
        if (oneShot && shown) return;

        Camera cam = Camera.main;
        if (!cam || creditsScreenPrefab == null)
        {
            Debug.LogWarning("[ShowCreditsOnGrab] Missing camera or prefab.");
            return;
        }

        GameObject screen = Instantiate(creditsScreenPrefab);
        Transform t = screen.transform;
        t.SetParent(cam.transform, false);
        t.localPosition = new Vector3(0f, 0f, distanceFromHead);
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        // Ensure there's a CanvasGroup and start at 0 alpha
        var group = screen.GetComponent<CanvasGroup>();
        if (group == null) group = screen.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // Optional: if you want it to block gaze/laser UI input while visible
        group.blocksRaycasts = true;
        group.interactable = true;

        StartCoroutine(FadeCanvasGroup(group, 0f, 1f, fadeDuration));

        shown = true;
        if (destroyObjectAfter) Destroy(gameObject);
    }

    IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        float t = 0f;
        group.alpha = from;

        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            group.alpha = Mathf.Lerp(from, to, p);
            yield return null;
        }

        group.alpha = to;
    }
}