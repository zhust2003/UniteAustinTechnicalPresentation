using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("ECS/Components/MeleeUnit")]
public class MeleeUnitDataWrapper : MonoBehaviour
{
    public MeleeUnitData Value;
}

public class MeleeUnitDataBaker : Baker<MeleeUnitDataWrapper>
{
    public override void Bake(MeleeUnitDataWrapper authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
