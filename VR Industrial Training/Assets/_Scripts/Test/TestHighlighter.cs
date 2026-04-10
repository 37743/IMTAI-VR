using UnityEngine;

public class TestHighlighter : MonoBehaviour
{
    public GameObject objectToHighlight;
    public RuntimeHighlighter highlighter;

    void Start()
    {
        highlighter.Highlight(objectToHighlight);

        Invoke("ClearHighlight", 20f);
    }

    void ClearHighlight()
    {
        highlighter.ClearHighlights();
    }
}