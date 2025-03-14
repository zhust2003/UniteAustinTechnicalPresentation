using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Experimental.AI;
using UnityEngine;

public struct CrowdAgent : IComponentData
{
    public int type;    // NavMeshAgent type
    public float3 worldPosition;
    public float3 velocity;
    public NavMeshLocation location;
}

[AddComponentMenu("ECS/Navigation/CrowdAgent")]
public class CrowdAgentComponent : MonoBehaviour
{
    public CrowdAgent Value;
}

public class CrowdAgentBaker : Baker<CrowdAgentComponent>
{
    public override void Bake(CrowdAgentComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
