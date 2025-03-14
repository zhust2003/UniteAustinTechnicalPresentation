using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

public struct FormationClosestData : IComponentData
{
	public Entity closestFormation;
	public float3 closestFormationPosition;
}

[AddComponentMenu("ECS/Components/FormationClosest")]
public class FormationClosestComponent : MonoBehaviour
{
	public FormationClosestData Value;
}

public class FormationClosestBaker : Baker<FormationClosestComponent>
{
	public override void Bake(FormationClosestComponent authoring)
	{
		var entity = GetEntity(TransformUsageFlags.Dynamic);
		AddComponent(entity, authoring.Value);
	}
}