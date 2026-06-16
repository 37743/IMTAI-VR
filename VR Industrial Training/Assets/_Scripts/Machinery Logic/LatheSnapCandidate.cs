using System.Collections.Generic;
using System;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

public class LatheSnapCandidate : MonoBehaviour
{
    public Transform snapRoot;
    public Transform snapAnchor;
    public bool readMetaInteractableState = true;
    public Behaviour[] interactableBehaviours;
    public bool isGrabbed;
    public bool isAttachedToMachine;
    public LatheSnapTarget attachedToSnapTarget;

    [Header("Events")]
    public UnityEvent unityOnAttachedToMachine;
    public UnityEvent unityOnRemovedFromMachine;

    public event Action<LatheSnapCandidate, LatheSnapTarget> AttachedToMachine;
    public event Action<LatheSnapCandidate, LatheSnapTarget> RemovedFromMachine;

    public Transform Root => snapRoot != null ? snapRoot : transform;
    public Transform Anchor => snapAnchor != null ? snapAnchor : Root;

    private readonly List<IInteractableView> interactableViews = new List<IInteractableView>();

    private void Reset()
    {
        snapRoot = transform;
    }

    private void Awake()
    {
        CacheInteractableViews();
    }

    private void OnEnable()
    {
        CacheInteractableViews();

        foreach (IInteractableView interactableView in interactableViews)
        {
            interactableView.WhenSelectingInteractorViewAdded += HandleSelectingInteractorAdded;
            interactableView.WhenSelectingInteractorViewRemoved += HandleSelectingInteractorRemoved;
        }
    }

    private void OnDisable()
    {
        foreach (IInteractableView interactableView in interactableViews)
        {
            interactableView.WhenSelectingInteractorViewAdded -= HandleSelectingInteractorAdded;
            interactableView.WhenSelectingInteractorViewRemoved -= HandleSelectingInteractorRemoved;
        }
    }

    private void Update()
    {
        if (!readMetaInteractableState)
            return;

        bool selected = false;

        foreach (IInteractableView interactableView in interactableViews)
        {
            if (interactableView.State == InteractableState.Select)
            {
                selected = true;
                break;
            }
        }

        isGrabbed = selected;
    }

    public void NotifyGrabStarted()
    {
        isGrabbed = true;
    }

    public void NotifyGrabEnded()
    {
        isGrabbed = false;
    }

    public void MarkAttachedToMachine(LatheSnapTarget snapTarget)
    {
        isAttachedToMachine = true;
        attachedToSnapTarget = snapTarget;
        unityOnAttachedToMachine?.Invoke();
        AttachedToMachine?.Invoke(this, snapTarget);
    }

    public void MarkRemovedFromMachine()
    {
        LatheSnapTarget previousTarget = attachedToSnapTarget;

        isAttachedToMachine = false;
        attachedToSnapTarget = null;
        unityOnRemovedFromMachine?.Invoke();
        RemovedFromMachine?.Invoke(this, previousTarget);
    }

    private void CacheInteractableViews()
    {
        interactableViews.Clear();

        if (interactableBehaviours != null)
        {
            foreach (Behaviour behaviour in interactableBehaviours)
                AddInteractableView(behaviour);
        }

        Behaviour[] childBehaviours = GetComponentsInChildren<Behaviour>(true);

        foreach (Behaviour behaviour in childBehaviours)
            AddInteractableView(behaviour);
    }

    private void AddInteractableView(Behaviour behaviour)
    {
        if (behaviour is IInteractableView interactableView && !interactableViews.Contains(interactableView))
            interactableViews.Add(interactableView);
    }

    private void HandleSelectingInteractorAdded(IInteractorView interactorView)
    {
        isGrabbed = true;
    }

    private void HandleSelectingInteractorRemoved(IInteractorView interactorView)
    {
        foreach (IInteractableView interactableView in interactableViews)
        {
            if (interactableView.State == InteractableState.Select)
                return;
        }

        isGrabbed = false;
    }
}
