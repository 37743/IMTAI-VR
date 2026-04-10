using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class ProbeMachine : MonoBehaviour
{
    public string serverIP = "26.45.252.190";
    public int port = 8000;

    public GameObject targetMachine;

    [System.Serializable]
    public class ProbeRequest
    {
        public string machine;
        public List<string> components;
    }

    void Start()
    {
        if (targetMachine != null)
        {
            StartCoroutine(SendProbe());
        }
        else
        {
            Debug.LogError("ProbeMachine: targetMachine is not assigned.");
        }
    }

    IEnumerator SendProbe()
    {
        List<string> componentNames = new List<string>();

        Transform[] children = targetMachine.GetComponentsInChildren<Transform>();

        foreach (Transform child in children)
        {
            if (child != targetMachine.transform)
                componentNames.Add(child.gameObject.name);
        }

        ProbeRequest request = new ProbeRequest
        {
            machine = targetMachine.name,
            components = componentNames
        };

        string json = JsonUtility.ToJson(request);

        string url = $"http://{serverIP}:{port}/probe";

        UnityWebRequest requestWeb = new UnityWebRequest(url, "POST");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        requestWeb.uploadHandler = new UploadHandlerRaw(bodyRaw);
        requestWeb.downloadHandler = new DownloadHandlerBuffer();
        requestWeb.SetRequestHeader("Content-Type", "application/json");

        yield return requestWeb.SendWebRequest();

        if (requestWeb.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Probe success: " + requestWeb.downloadHandler.text);
        }
        else
        {
            Debug.LogError("Probe failed: " + requestWeb.error);
        }
    }
}