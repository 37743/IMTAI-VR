using UnityEngine;
using System.Collections;
using StateAsm;

public class RunSpectroState : SentisWhisperState
{
    private Unity.InferenceEngine.Tensor<float> spectroOutput;

    public RunSpectroState(IStateMachine<WhisperStateID> stateMachine) : base(stateMachine, WhisperStateID.RunSpectro, WhisperStateID.RunEncoder)
    {
    }

    public override void Enter()
    {
        Debug.Log("-> RunSpectroState::Enter()");
        stage = 0;
        RunSpectro();
    }
 
    public override void Update()
    {
        stateMachine.SetState(nextStateId);
    }
       
    private void RunSpectro()
    {
        using var input = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, whisper.NumSamples), whisper.Data);
        whisper.SpectroEngine.Schedule(input);

        spectroOutput = whisper.SpectroEngine.PeekOutput() as Unity.InferenceEngine.Tensor<float>;

        whisper.SpectroOutput?.Dispose();
        whisper.SpectroOutput = null;

        whisper.SpectroOutput = spectroOutput.ReadbackAndClone();
        spectroOutput?.Dispose();
        spectroOutput = null;
    }

    public override void Exit()
    {
        // Safety net in case anything remained
        spectroOutput?.Dispose(); spectroOutput = null;
        base.Exit();
    }
}
