using UnityEngine;
using System.Collections;
using StateAsm;

public class RunEncoderState : SentisWhisperState
{
    IEnumerator m_Schedule;
    private int layerCount = 0;

    Unity.InferenceEngine.Tensor<float> encodedAudio;

    public RunEncoderState(IStateMachine<WhisperStateID> stateMachine) : base(stateMachine, WhisperStateID.RunEncoder, WhisperStateID.RunDecoder)
    {
    }

    public override void Enter()
    {
        Debug.Log("-> RunEncoderState::Enter()");
        stage = 0;
        m_Schedule = null;
        layerCount = 0;
    }

    public override void Update()
    {
        switch (stage)
        {
            case 0:
                StartModel();
                break;
            case 1:
                ExecuteLayer();
                break;
            case 2:
                ReadOutput();
                break;
            default:
                stateMachine.SetState(nextStateId);
                break;
        }
    }

    private void StartModel()
    {
        m_Schedule = whisper.EncoderEngine.ScheduleIterable(whisper.SpectroOutput);
        stage = 1;
    }

    private void ExecuteLayer()
    {
        layerCount++;

        if (!m_Schedule.MoveNext())
        {
            stage = 2;
        }
    }

    private void ReadOutput()
    {
        Debug.Log("-> ReadOutput() - Number of layers: " + layerCount);

        encodedAudio = whisper.EncoderEngine.PeekOutput() as Unity.InferenceEngine.Tensor<float>;

        whisper.EncodedAudio?.Dispose();
        whisper.EncodedAudio = null;

        whisper.EncodedAudio = encodedAudio.ReadbackAndClone();
        encodedAudio?.Dispose();
        encodedAudio = null;

        stage = 3;
    }

    public override void Exit()
    {
        encodedAudio?.Dispose(); encodedAudio = null;
        whisper.SpectroOutput?.Dispose(); whisper.SpectroOutput = null;
        base.Exit();
    }
}
