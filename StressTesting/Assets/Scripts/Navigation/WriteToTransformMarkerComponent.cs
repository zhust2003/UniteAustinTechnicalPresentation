using Unity.Entities;
using UnityEngine;

// struct that lets the system know it should apply the results into the transform component
public struct WriteToTransformMarker : IComponentData
{
}

[AddComponentMenu("ECS/Navigation/WriteToTransformMarker")]
public class WriteToTransformMarkerComponent : MonoBehaviour
{
    public WriteToTransformMarker Value;
}

public class WriteToTransformMarkerBaker : Baker<WriteToTransformMarkerComponent>
{
    public override void Bake(WriteToTransformMarkerComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
