using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Experimental.AI;
using UnityEngine;

public struct CrowdAgentNavigator : IComponentData
{
    public float3 requestedDestination;
    public NavMeshLocation requestedDestinationLocation;
    public float distanceToDestination;
    public NavMeshLocation pathStart;
    public NavMeshLocation pathEnd;
    public int pathSize;
    public float speed;
    public float nextCornerSide;
    public float3 steeringTarget;
    public bool newDestinationRequested;
    public bool goToDestination;
    public bool destinationInView;
    public bool destinationReached;
    public bool active;

    public void MoveTo(float3 dest)
    {
        requestedDestination = dest;
        newDestinationRequested = true;
    }

    public void StartMoving()
    {
        goToDestination = true;
        destinationInView = false;
        destinationReached = false;
        distanceToDestination = -1f;
    }
}

[AddComponentMenu("ECS/Navigation/CrowdAgentNavigator")]
public class CrowdAgentNavigatorComponent : MonoBehaviour
{
    public CrowdAgentNavigator Value;
}

public class CrowdAgentNavigatorBaker : Baker<CrowdAgentNavigatorComponent>
{
    public override void Bake(CrowdAgentNavigatorComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
