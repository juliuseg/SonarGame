using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BiomeSettings", menuName = "Own/BiomeSettings")]
public class BiomeSettings : ScriptableObject
{
    public float densityOffset;
    public List<InstancingMesh> instancingMeshes;

}

