using Unity.Entities;
using UnityEngine;

public struct AliveMinionData : IComponentData
{
	
}

[AddComponentMenu("ECS/Components/AliveMinion")]
public class AliveMinionComponent : MonoBehaviour
{
    public AliveMinionData Value;
}

public class AliveMinionBaker : Baker<AliveMinionComponent>
{
    public override void Bake(AliveMinionComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, new AliveMinionData());
    }
}
