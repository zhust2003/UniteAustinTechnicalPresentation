using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

[System.Serializable]
public struct RigidbodyData : IComponentData
{
	public float3 Velocity;
	public float3 AngularVelocity;
}

[AddComponentMenu("ECS/Components/Rigidbody")]
public class RigidbodyComponent : MonoBehaviour
{
    public RigidbodyData Value;
}

public class RigidbodyBaker : Baker<RigidbodyComponent>
{
    public override void Bake(RigidbodyComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
