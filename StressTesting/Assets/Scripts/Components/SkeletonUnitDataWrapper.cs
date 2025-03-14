using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("ECS/Components/SkeletonUnit")]
public class SkeletonUnitDataWrapper : MonoBehaviour
{
    public SkeletonUnitData Value;
}

public class SkeletonUnitDataBaker : Baker<SkeletonUnitDataWrapper>
{
    public override void Bake(SkeletonUnitDataWrapper authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
