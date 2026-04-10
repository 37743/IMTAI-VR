using UnityEngine;
using StateAsm;

public class WhisperReadyState : SentisWhisperState
{
    public WhisperReadyState(IStateMachine<WhisperStateID> stateMachine) : base(stateMachine, WhisperStateID.Ready, WhisperStateID.Ready)
    {      
    }

    public override void Enter()
    {
        Debug.Log("-> WhisperReadyState::Enter()");
        whisper.IsReady = true;
    }
}
