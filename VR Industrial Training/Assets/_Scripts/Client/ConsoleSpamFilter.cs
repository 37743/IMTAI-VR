using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
public sealed class ConsoleSpamFilter : ILogHandler
{
    private static readonly string[] BannedPhrases = new string[]
    {
        "XR_ERROR_ACTIONSET_NOT_ATTACHED",
        "ErrorFunctionUnsupported",
        "XR_ERROR_FUNCTION_UNSUPPORTED",
        "Error setting active audio output driver",
        "Local Dimming feature is not supported",
        "BoxCollider does not support negative scale or size",
        "Setting linear velocity of a kinematic body is not supported",
        "Setting angular velocity of a kinematic body is not supported",
        "Data longer than the AudioClip"
    };

#if UNITY_EDITOR
    static ConsoleSpamFilter()
    {
        Install();
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Install();
    }

    private readonly ILogHandler defaultLogHandler;

    private ConsoleSpamFilter(ILogHandler defaultLogHandler)
    {
        this.defaultLogHandler = defaultLogHandler;
    }

    private static void Install()
    {
        if (Debug.unityLogger.logHandler is ConsoleSpamFilter)
            return;

        Debug.unityLogger.logHandler = new ConsoleSpamFilter(Debug.unityLogger.logHandler);
    }

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
        string message = BuildMessage(format, args);

        if (ShouldSuppress(message))
            return;

        defaultLogHandler.LogFormat(logType, context, format, args);
    }

    public void LogException(Exception exception, UnityEngine.Object context)
    {
        if (exception != null && ShouldSuppress(exception.Message))
            return;

        defaultLogHandler.LogException(exception, context);
    }

    private static bool ShouldSuppress(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        foreach (string phrase in BannedPhrases)
        {
            if (message.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string BuildMessage(string format, object[] args)
    {
        if (args == null || args.Length == 0)
            return format;

        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
