using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using StateAsm;
using UnityEngine;
using Unity.InferenceEngine;
using System.Text.RegularExpressions;

public class RunDecoderState : SentisWhisperState
{
    const int END_OF_TEXT = 50257;
    const int START_OF_TRANSCRIPT = 50258;
    const int ENGLISH = 50259;
    const int TRANSCRIBE = 50359;
    const int NO_TIME_STAMPS = 50363;
    const int START_TIME = 50364;

    const int maxTokens = 100;
    private int currentToken = 3;
    private int[] outputTokens = new int[maxTokens];
    private string outputString = "";

    Tensor<int> tokensPredictions;
    Tensor<int> cpuTokensPredictions;

    public RunDecoderState(IStateMachine<WhisperStateID> stateMachine)
        : base(stateMachine, WhisperStateID.RunDecoder, WhisperStateID.Ready) { }

    public override void Enter()
    {
        Debug.Log("-> RunDecoderState::Enter()");
        stage = 0;

        outputTokens[0] = START_OF_TRANSCRIPT;
        outputTokens[1] = ENGLISH;
        outputTokens[2] = TRANSCRIBE;
        outputTokens[3] = NO_TIME_STAMPS;

        currentToken = 3;
        outputString = "";
    }

    public override void Update()
    {
        switch (stage)
        {
            case 0:
                if (currentToken < outputTokens.Length - 1)
                    ExecuteDecoder();
                break;

            default:
                stateMachine.SetState(nextStateId);
                break;
        }
    }

    private void ExecuteDecoder()
    {
        using var tokensSoFar =
            new Tensor<int>(new TensorShape(1, outputTokens.Length), outputTokens);

        var inputs = new Dictionary<string, Tensor>
        {
            { "input_0", tokensSoFar },
            { "input_1", whisper.EncodedAudio }
        };

        whisper.DecoderEngine.Schedule(inputs.Values.ToArray());

        tokensPredictions?.Dispose();
        cpuTokensPredictions?.Dispose();

        tokensPredictions = whisper.DecoderEngine.PeekOutput() as Tensor<int>;
        cpuTokensPredictions = tokensPredictions.ReadbackAndClone();

        tokensPredictions.Dispose();

        int ID = cpuTokensPredictions[currentToken];
        outputTokens[++currentToken] = ID;

        if (ID == END_OF_TEXT)
        {
            stage = 1;

            string finalTranscript = CleanTranscript(outputString);

            if (whisper.SpeechText != null)
                whisper.SpeechText.text = "You: " + finalTranscript;

            if (!string.IsNullOrEmpty(finalTranscript) && whisper.machineGuide != null)
            {
                Debug.Log($"Sending to AI: {finalTranscript}");
                whisper.machineGuide.AskQuestion(finalTranscript);
            }
            else if (whisper.machineGuide == null)
            {
                Debug.LogWarning("RunDecoderState: LatheMachineGuide reference is missing on RunWhisper!");
            }
        }
        else if (ID >= whisper.Tokens.Length)
        {
            outputString += $"(time={(ID - START_TIME) * 0.02f})";
        }
        else
        {
            outputString += GetUnicodeText(whisper.Tokens[ID]);
        }

        cpuTokensPredictions.Dispose();
    }

    string GetUnicodeText(string text)
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(ShiftCharacterDown(text));
        return Encoding.UTF8.GetString(bytes);
    }

    string ShiftCharacterDown(string text)
    {
        string outText = "";
        foreach (char letter in text)
        {
            outText += ((int)letter <= 256) ? letter :
                (char)whisper.WhiteSpaceCharacters[(int)(letter - 256)];
        }
        return outText;
    }

    private static string CleanTranscript(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var rxBlankAudio = new Regex(@"\s*\[(?:blank_audio|blank audio)\]\s*", RegexOptions.IgnoreCase);
        var rxYouOnly = new Regex(@"^\s*you\s*[.!?\""]?\s*$", RegexOptions.IgnoreCase);

        string s = input;
        s = rxBlankAudio.Replace(s, " ");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();

        if (rxYouOnly.IsMatch(s)) return string.Empty;

        return s;
    }

    public override void Exit()
    {
        tokensPredictions?.Dispose();
        cpuTokensPredictions?.Dispose();
        whisper.EncodedAudio?.Dispose();

        GC.Collect();
        base.Exit();
    }
}