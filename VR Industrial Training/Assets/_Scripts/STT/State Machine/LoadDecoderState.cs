using UnityEngine;
using StateAsm;

public class LoadDecoderState : SentisWhisperState
{

    public LoadDecoderState(IStateMachine<WhisperStateID> stateMachine) : base(stateMachine, WhisperStateID.LoadDecoder, WhisperStateID.LoadEncoder)
    {
        
    }

    public override void Enter()
    {
        stage = 0;
    }

    public override void Update()
    {
        switch(stage)
        {
            case 0:
                LoadDecoder();
                stage = 1;
                break;
            default:
                stateMachine.SetState( nextStateId );
                break;
        }      
    }

    private void LoadDecoder() 
    {
        Unity.InferenceEngine.Model decoder = Unity.InferenceEngine.ModelLoader.Load(whisper.decoderAsset);

        var graph = new Unity.InferenceEngine.FunctionalGraph();

        var inputs = graph.AddInputs(decoder);

        Unity.InferenceEngine.FunctionalTensor[] outputs = Unity.InferenceEngine.Functional.Forward(decoder, inputs);

        Unity.InferenceEngine.FunctionalTensor argmaxOutput = Unity.InferenceEngine.Functional.ArgMax(outputs[0],2);

        Unity.InferenceEngine.Model decoderWithArgMax = graph.Compile(argmaxOutput);

        whisper.DecoderEngine = new Unity.InferenceEngine.Worker(decoderWithArgMax, backend);
    }

    public override void Exit()
    {
        base.Exit();
    }
}
