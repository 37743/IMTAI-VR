using System;
using UnityEngine;
using UnityEngine.Events;

public abstract class LatheISDKStepInteractable : MonoBehaviour
{
    [Header("Training ID")]
    public string targetId;

    [Header("Step State")]
    public bool isStepActive;

    [Header("Meta ISDK Components To Enable Only During Highlighted Step")]
    [Tooltip("Drag HandGrabInteractable, GrabInteractable, PokeInteractable, or related ISDK behaviours here.")]
    public Behaviour[] interactionBehavioursToEnable;

    [Header("Completion")]
    public UnityEvent unityOnCompleted;

    public event Action<LatheISDKStepInteractable> Completed;

    protected virtual void Reset()
    {
        targetId = gameObject.name;
    }

    protected virtual void Awake()
    {
        SetStepActive(false);
    }

    public virtual void SetStepActive(bool active)
    {
        isStepActive = active;

        if (interactionBehavioursToEnable != null)
        {
            foreach (Behaviour behaviour in interactionBehavioursToEnable)
            {
                if (behaviour != null)
                    behaviour.enabled = active;
            }
        }
    }

    protected void CompleteInteraction()
    {
        unityOnCompleted?.Invoke();
        Completed?.Invoke(this);
    }
}