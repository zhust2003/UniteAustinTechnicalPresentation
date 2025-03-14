using System;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct MinionTarget : IComponentData
{
	public float3 Target;
	public float speed;
}

[AddComponentMenu("ECS/Components/Target")]
public class TargetComponent : MonoBehaviour
{
    public MinionTarget Value;
}

public class TargetBaker : Baker<TargetComponent>
{
    public override void Bake(TargetComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}