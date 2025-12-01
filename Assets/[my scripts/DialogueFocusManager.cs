using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class DialogueFocusManager : MonoBehaviour
{
public static DialogueFocusManager Instance { get; private set; }
[Header("Head source")]
public Transform head; // Assign CenterEyeAnchor; if null, uses Camera.main

[Header("Focus scoring")]
[Range(0f, 1f)] public float dotWeight = 0.8f;
public float distanceWeight = 0.2f;
public bool lockFocusWhileTalking = true;

[Header("Occlusion")]
public bool requireLineOfSight = true;
public LayerMask occlusionMask; // Include environment; exclude NPC/UI layers

[Header("Input")]
public bool useLeftXY = true;   // X/Y on left Touch
public bool useRightAB = false; // A/B on right Touch

readonly List<NPCDialogue> npcs = new List<NPCDialogue>(64);
NPCDialogue current;
NPCDialogue talkingNPC;

void Awake()
{
    if (Instance != null && Instance != this) { Destroy(gameObject); return; }
    Instance = this;
    BootstrapScan();
}

void OnEnable()
{
    if (Instance == this) BootstrapScan();
}

void BootstrapScan()
{
    #if UNITY_2023_1_OR_NEWER
var found = FindObjectsByType<NPCDialogue>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
var found = FindObjectsOfType<NPCDialogue>();
#endif
for (int i = 0; i < found.Length; i++)
{
if (found[i] != null && !npcs.Contains(found[i]))
npcs.Add(found[i]);
}
}
public void Register(NPCDialogue npc)
{
    if (npc == null) return;
    if (!npcs.Contains(npc)) npcs.Add(npc);
}

public void Unregister(NPCDialogue npc)
{
    if (npc == null) return;
    int idx = npcs.IndexOf(npc);
    if (idx >= 0) npcs.RemoveAt(idx);
    if (current == npc) current = null;
    if (talkingNPC == npc) talkingNPC = null;
}

void Update()
{
    if (!head) head = Camera.main ? Camera.main.transform : null;
    if (!head) return;

    // Lock focus and suppress prompts while any NPC is talking
    if (lockFocusWhileTalking)
    {
        talkingNPC = null;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (n != null && n.IsTalking) { talkingNPC = n; break; }
        }
        if (talkingNPC != null)
        {
            current = talkingNPC;
            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                if (n != null) n.SetPromptActive(false);
            }
            return;
        }
    }

    NPCDialogue best = null;
    float bestScore = float.NegativeInfinity;

    // Pick the best candidate by look dot and distance
    for (int i = 0; i < npcs.Count; i++)
    {
        var npc = npcs[i];
        if (npc == null) continue;

        float dot, dist;
        if (!npc.ComputeEligibility(head, out dot, out dist))
        {
            npc.SetPromptActive(false);
            continue;
        }

        if (requireLineOfSight)
        {
            Vector3 dir = npc.transform.position - head.position;
            float len = dir.magnitude;
            if (len > 0f)
            {
                dir /= len;
                if (Physics.Raycast(head.position, dir, out RaycastHit hit, len, occlusionMask, QueryTriggerInteraction.Ignore))
                {
                    // Ignore the target NPC’s own colliders
                    if (!hit.transform.IsChildOf(npc.transform))
                    {
                        npc.SetPromptActive(false);
                        continue;
                    }
                }
            }
        }

        float normDist = Mathf.Clamp01(dist / npc.showPromptDistance);
        float score = dotWeight * dot + distanceWeight * (1f - normDist);

        if (score > bestScore)
        {
            bestScore = score;
            best = npc;
        }
    }

    current = best;

    // Only focused NPC shows prompt/highlight
    for (int i = 0; i < npcs.Count; i++)
    {
        var npc = npcs[i];
        if (npc != null) npc.SetPromptActive(npc == current);
    }

    // Single input check per frame
    if (current != null && GetTalkPressed())
    {
        current.TriggerDialogue();
    }
}

bool GetTalkPressed()
{
    bool pressed = false;
    if (useLeftXY)
    {
        pressed |= OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)
                || OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch);
    }
    if (useRightAB)
    {
        pressed |= OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)
                || OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
    }
    return pressed;
}

public NPCDialogue CurrentFocus => current;

public void ClearPrompts()
{
    for (int i = 0; i < npcs.Count; i++)
    {
        var npc = npcs[i];
        if (npc != null) npc.SetPromptActive(false);
    }
    current = null;
}
}