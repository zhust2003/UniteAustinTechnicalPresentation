using Unity.Entities;
using UnityEngine;

[System.Serializable]
public struct FlyingData : IComponentData
{
}

[AddComponentMenu("ECS/Components/Flying")]
public class FlyingComponent : MonoBehaviour
{
    public FlyingData Value;
}

public class FlyingBaker : Baker<FlyingComponent>
{
    public override void Bake(FlyingComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
