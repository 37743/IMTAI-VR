using System.Collections.Generic;
using UnityEngine;

public class RuntimeHighlighter : MonoBehaviour
{
    public Color outlineColor = Color.yellow;
    [Range(0f, 0.1f)] public float outlineWidth = 0.02f;

    private readonly List<OutlineInstance> activeOutlines = new List<OutlineInstance>();

    public void Highlight(GameObject obj)
    {
        if (obj == null)
            return;

        // Do not add multiple outlines to the same object.
        if (obj.TryGetComponent<OutlineInstance>(out _))
            return;

        OutlineInstance outline = obj.AddComponent<OutlineInstance>();
        outline.Initialize(outlineColor, outlineWidth);
        activeOutlines.Add(outline);
    }

    public void ClearHighlights()
    {
        for (int i = activeOutlines.Count - 1; i >= 0; i--)
        {
            if (activeOutlines[i] != null)
            {
                // Destroying the component should trigger its OnDestroy/ResetMaterial.
                Destroy(activeOutlines[i]);
            }
        }

        activeOutlines.Clear();
    }
}