using UnityEngine;
using System.Collections.Generic;

public class OutlineInstance : MonoBehaviour
{
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Material outlineMat;

    public void Initialize(Color color, float width)
    {
        // Find the VR-compatible shader
        Shader shader = Shader.Find("Custom/OutlineShaderVR");
        if (shader == null)
        {
            Debug.LogError("OutlineShaderVR not found! Make sure the shader file is in your project.");
            return;
        }

        outlineMat = new Material(shader);
        outlineMat.SetColor("_OutlineColor", color);
        outlineMat.SetFloat("_OutlineWidth", width);

        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        foreach (var rend in renderers)
        {
            // Store original shared materials array
            originalMaterials[rend] = rend.sharedMaterials;

            // Append the outline material to the end of the array
            Material[] currentMats = rend.sharedMaterials;
            Material[] newMats = new Material[currentMats.Length + 1];
            
            for (int i = 0; i < currentMats.Length; i++)
            {
                newMats[i] = currentMats[i];
            }
            
            newMats[newMats.Length - 1] = outlineMat;
            rend.materials = newMats;
        }
    }

    public void ResetMaterial()
    {
        foreach (var entry in originalMaterials)
        {
            if (entry.Key != null)
            {
                // Restore original material array
                entry.Key.materials = entry.Value;
            }
        }

        if (outlineMat != null)
        {
            Destroy(outlineMat);
        }
    }

    private void OnDestroy()
    {
        ResetMaterial();
    }
}