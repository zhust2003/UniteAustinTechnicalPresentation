using Unity.Entities;
using UnityEngine;

[AddComponentMenu("ECS/Components/RangedUnit")]
public class RangedUnitDataWrapper : MonoBehaviour
{
    public RangedUnitData Value;
}

public class RangedUnitDataBaker : Baker<RangedUnitDataWrapper>
{
    public override void Bake(RangedUnitDataWrapper authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}

