using UnityEngine;

public class LatheCuttingToolMaterial : MonoBehaviour
{
    [Header("Tool Material")]
    public string materialName = "Default tool steel";

    [Tooltip("Higher values remove more material from the workpiece. 1 is the default tool material.")]
    [Min(0f)]
    public float deformationMultiplier = 1f;
}
