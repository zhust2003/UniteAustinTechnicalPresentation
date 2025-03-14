using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("ECS/Components/TankUnit")]
public class TankUnitDataWrapper : MonoBehaviour
{
    public TankUnitData Value;
}

public class TankUnitDataBaker : Baker<TankUnitDataWrapper>
{
    public override void Bake(TankUnitDataWrapper authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.Value);
    }
}
