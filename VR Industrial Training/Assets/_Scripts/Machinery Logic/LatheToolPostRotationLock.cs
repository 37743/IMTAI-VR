using System.Collections.Generic;
using UnityEngine;

public class LatheToolPostRotationLock : MonoBehaviour
{
    private struct BehaviourState
    {
        public Behaviour Behaviour;
        public bool WasEnabled;

        public BehaviourState(Behaviour behaviour)
        {
            Behaviour = behaviour;
            WasEnabled = behaviour != null && behaviour.enabled;
        }
    }

    [Header("Toolpost")]
    public Transform toolPost;
    public bool locked = true;

    [Header("Interaction Lock")]
    [Tooltip("Drag the toolpost Grabbable, GrabInteractable, HandGrabInteractable, or rotation transformer here.")]
    public Behaviour[] behavioursToDisableWhenLocked;

    [Tooltip("If the list above is empty, find likely grab/rotation behaviours on the toolpost object.")]
    public bool autoFindBehavioursOnToolPost = true;

    [Tooltip("Leave off if the locking lever is a child of the toolpost, otherwise it may be disabled too.")]
    public bool includeChildrenWhenAutoFinding;

    [Header("Rotation Hold")]
    public bool holdLockedLocalRotation = true;

    private readonly List<BehaviourState> _behaviourStates = new List<BehaviourState>();
    private Quaternion _lockedLocalRotation;
    private bool _cacheBuilt;

    private void Reset()
    {
        toolPost = transform;
    }

    private void Awake()
    {
        ResolveReferences();
        CacheBehaviourStates();

        if (toolPost != null)
            _lockedLocalRotation = toolPost.localRotation;

        ApplyLockedState();
    }

    private void LateUpdate()
    {
        if (!locked || !holdLockedLocalRotation || toolPost == null)
            return;

        toolPost.localRotation = _lockedLocalRotation;
    }

    public void SetLocked(bool value)
    {
        if (locked == value && _cacheBuilt)
            return;

        locked = value;
        ResolveReferences();
        CacheBehaviourStates();

        if (locked && toolPost != null)
            _lockedLocalRotation = toolPost.localRotation;

        ApplyLockedState();
    }

    public void Lock()
    {
        SetLocked(true);
    }

    public void Unlock()
    {
        SetLocked(false);
    }

    [ContextMenu("Refresh Behaviour Cache")]
    public void RefreshBehaviourCache()
    {
        _cacheBuilt = false;
        CacheBehaviourStates();
        ApplyLockedState();
    }

    private void ResolveReferences()
    {
        if (toolPost == null)
            toolPost = transform;
    }

    private void CacheBehaviourStates()
    {
        if (_cacheBuilt)
            return;

        _behaviourStates.Clear();

        if (behavioursToDisableWhenLocked != null && behavioursToDisableWhenLocked.Length > 0)
        {
            for (int i = 0; i < behavioursToDisableWhenLocked.Length; i++)
                AddBehaviourState(behavioursToDisableWhenLocked[i]);
        }
        else if (autoFindBehavioursOnToolPost && toolPost != null)
        {
            Behaviour[] behaviours = includeChildrenWhenAutoFinding
                ? toolPost.GetComponentsInChildren<Behaviour>(true)
                : toolPost.GetComponents<Behaviour>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (IsLikelyToolPostRotationBehaviour(behaviours[i]))
                    AddBehaviourState(behaviours[i]);
            }
        }

        _cacheBuilt = true;
    }

    private void AddBehaviourState(Behaviour behaviour)
    {
        if (behaviour == null || behaviour == this)
            return;

        for (int i = 0; i < _behaviourStates.Count; i++)
        {
            if (_behaviourStates[i].Behaviour == behaviour)
                return;
        }

        _behaviourStates.Add(new BehaviourState(behaviour));
    }

    private bool IsLikelyToolPostRotationBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || behaviour == this)
            return false;

        if (behaviour is ResistedOneGrabRotateTransformer)
            return true;

        string typeName = behaviour.GetType().Name;
        string fullName = behaviour.GetType().FullName ?? string.Empty;

        bool isMetaInteraction = fullName.StartsWith("Oculus.Interaction");
        bool isGrabBehaviour =
            typeName.Contains("Grabbable") ||
            typeName.Contains("GrabInteractable") ||
            typeName.Contains("HandGrabInteractable") ||
            typeName.Contains("GrabTransformer") ||
            typeName.Contains("OneGrabRotateTransformer");

        return isMetaInteraction && isGrabBehaviour;
    }

    private void ApplyLockedState()
    {
        for (int i = 0; i < _behaviourStates.Count; i++)
        {
            BehaviourState state = _behaviourStates[i];

            if (state.Behaviour == null)
                continue;

            state.Behaviour.enabled = locked ? false : state.WasEnabled;
        }
    }
}
