using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
[System.Serializable]
public struct ArrowData : IComponentData
{
	public float3 position;
	public float3 velocity;

	public bool active;
	public int IsFriendly;
}
 
[AddComponentMenu("ECS/Components/Arrow")]
public class ArrowComponent : MonoBehaviour
{
    public ArrowData Value;
}

public class ArrowBaker : Baker<ArrowComponent>
{
    public override void Bake(ArrowComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
