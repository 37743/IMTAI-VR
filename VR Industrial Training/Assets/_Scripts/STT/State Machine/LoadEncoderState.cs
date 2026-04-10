using UnityEngine;
using StateAsm;

public class LoadEncoderState : SentisWhisperState
{

    public LoadEncoderState(IStateMachine<WhisperStateID> stateMachine) : base(stateMachine, WhisperStateID.LoadEncoder, WhisperStateID.LoadSpectro)
    {

    }

    public override void Enter()
    {
        //Debug.Log("-> LoadEncoderState::Enter()");
        stage = 0;
    }

    public override void Update()
    {
        switch (stage)
        {
            case 0:
                LoadEncoder();
                stage = 1; 
                break;
            default:
                stateMachine.SetState(nextStateId);
                break;
        }
    }

    private void LoadEncoder()
    {
        Unity.InferenceEngine.Model encoder = Unity.InferenceEngine.ModelLoader.Load( whisper.encoderAsset);
        whisper.EncoderEngine = new Unity.InferenceEngine.Worker(encoder, backend); //V2.0
    }

    public override void Exit()
    {
        base.Exit();
    }
}