using Unity.Entities;
using UnityEngine;

[AddComponentMenu("ECS/Components/UnitTransform")]
public class UnitTransformDataComponent : MonoBehaviour
{
    public UnitTransformData Value;
}

public class UnitTransformDataBaker : Baker<UnitTransformDataComponent>
{
    public override void Bake(UnitTransformDataComponent authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
