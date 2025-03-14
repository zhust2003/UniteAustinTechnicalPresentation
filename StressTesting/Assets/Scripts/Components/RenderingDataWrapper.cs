using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

[AddComponentMenu("ECS/Components/Rendering")]
public class RenderingDataWrapper : MonoBehaviour
{
	public UnitType UnitType;
	public GameObject BakingPrefab;
	public Material Material;
	public LodData LodData;
}

public class RenderingDataBaker : Baker<RenderingDataWrapper>
{
	public override void Bake(RenderingDataWrapper authoring)
	{
		// 创建一个纯值类型的RenderingData
		var renderingData = new RenderingData
		{
			UnitTypeValue = (int)authoring.UnitType,
			MaterialID = authoring.Material != null ? authoring.Material.GetInstanceID() : 0,
			
			// LodData字段
			Lod1MeshID = authoring.LodData.Lod1Mesh != null ? authoring.LodData.Lod1Mesh.GetInstanceID() : 0,
			Lod2MeshID = authoring.LodData.Lod2Mesh != null ? authoring.LodData.Lod2Mesh.GetInstanceID() : 0,
			Lod3MeshID = authoring.LodData.Lod3Mesh != null ? authoring.LodData.Lod3Mesh.GetInstanceID() : 0,
			Lod1Distance = authoring.LodData.Lod1Distance,
			Lod2Distance = authoring.LodData.Lod2Distance,
			Lod3Distance = authoring.LodData.Lod3Distance,
			Scale = authoring.LodData.Scale
		};
		
		var entity = GetEntity(TransformUsageFlags.Dynamic);
		AddSharedComponent(entity, renderingData);
		
		// 单独添加 Entity 引用组件
		if (authoring.BakingPrefab != null)
		{
			AddComponent(entity, new BakingPrefabReference 
			{ 
				BakingPrefabEntity = GetEntity(authoring.BakingPrefab, TransformUsageFlags.None) 
			});
		}
		
        Debug.Log($"已烘焙 UnitType: {authoring.UnitType} 到实体: {entity.Index}");
	}
}

[Serializable]
public struct RenderingData : ISharedComponentData
{
	// 基本类型
	public int UnitTypeValue;
	public int MaterialID;
	
	// LodData字段
	public int Lod1MeshID;
	public int Lod2MeshID;
	public int Lod3MeshID;
	public float Lod1Distance;
	public float Lod2Distance;
	public float Lod3Distance;
	public float Scale;
}

// 创建一个单独的组件来存储 Entity 引用
public struct BakingPrefabReference : IComponentData
{
	public Entity BakingPrefabEntity;
}
