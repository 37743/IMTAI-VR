using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;

public class PokeToggle : MonoBehaviour
{
    [Header("Target UI")]
    public Toggle targetToggle;

    [Header("Settings")]
    [Tooltip("Minimum time (in seconds) between allowed presses to prevent double-clicks/jitter.")]
    public float pressCooldown = 0.25f;

    private PokeInteractable _pokeInteractable;
    private InteractableState _previousState = InteractableState.Normal;
    private float _lastPressTime = 0f;

    void Start()
    {
        _pokeInteractable = GetComponent<PokeInteractable>();
        
        if (_pokeInteractable == null)
        {
            Debug.LogError("ManualPokeToggle: No PokeInteractable found.");
        }
    }

    void Update()
    {
        if (_pokeInteractable == null) return;

        InteractableState currentState = _pokeInteractable.State;

        if (currentState == InteractableState.Select && _previousState != InteractableState.Select)
        {
            if (Time.unscaledTime - _lastPressTime >= pressCooldown)
            {
                _lastPressTime = Time.unscaledTime;
                
                if (targetToggle != null)
                {
                    targetToggle.isOn = !targetToggle.isOn;
                }
            }
            else
            {
                Debug.Log("Poke ignored: Cooldown active to prevent double-click.");
            }
        }

        _previousState = currentState;
    }
}