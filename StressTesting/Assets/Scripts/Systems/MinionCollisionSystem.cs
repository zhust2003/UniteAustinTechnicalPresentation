using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(FormationIntegritySystem))]
public partial class MinionCollisionSystem : SystemBase
{
	private EntityQuery minionsQuery;
	private MinionSystem minionSystem;

	private NativeList<UnitTransformData> m_Transforms;
	private NativeList<Entity> m_Entities;
	private NativeList<MinionBitmask> m_Bitmasks;
	
	protected override void OnCreate()
	{
		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadWrite<RigidbodyData>(),
			ComponentType.ReadOnly<MinionBitmask>(),
			ComponentType.ReadWrite<MinionAttackData>()
		);
		
		m_Transforms = new NativeList<UnitTransformData>(Allocator.Persistent);
		m_Entities = new NativeList<Entity>(Allocator.Persistent);
		m_Bitmasks = new NativeList<MinionBitmask>(Allocator.Persistent);
	}

	protected override void OnDestroy()
	{
		m_Transforms.Dispose();
		m_Entities.Dispose();
		m_Bitmasks.Dispose();
	}

	protected override void OnUpdate()
	{
		if (minionSystem == null)
		{
			minionSystem = World.GetOrCreateSystemManaged<MinionSystem>();
		}
		
		if (minionsQuery.IsEmpty) return;
		
		int minionCount = minionsQuery.CalculateEntityCount();
		
		// 获取组件数据
		var entitiesArray = minionsQuery.ToEntityArray(Allocator.TempJob);
		var transformsArray = minionsQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var velocitiesArray = minionsQuery.ToComponentDataArray<RigidbodyData>(Allocator.TempJob);
		var bitmaskArray = minionsQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
		var attackDataArray = minionsQuery.ToComponentDataArray<MinionAttackData>(Allocator.TempJob);

		m_Transforms.ResizeUninitialized(minionCount);
		m_Entities.ResizeUninitialized(minionCount);
		m_Bitmasks.ResizeUninitialized(minionCount);
		
		var prepareCollision = new PrepareMinionCollisionJob
		{
			entities = entitiesArray,
			entitiesArray = m_Entities.AsDeferredJobArray(),
			
			transforms = transformsArray,
			transformsArray = m_Transforms.AsDeferredJobArray(),
			
			minionBitmask = bitmaskArray,
			minionBitmaskArray = m_Bitmasks.AsDeferredJobArray()
		};
		var prepareJobHandle = prepareCollision.Schedule(minionCount, 128, Dependency);
		
		// 获取碰撞桶
		var collisionForceJob = new MinionCollisionJob
		{
			transforms = m_Transforms.AsDeferredJobArray(),
			buckets = minionSystem.CollisionBuckets,
			minionVelocities = velocitiesArray,
			dt = SystemAPI.Time.DeltaTime,
			minionBitmask = m_Bitmasks.AsDeferredJobArray(),
			minionAttackData = attackDataArray,
			entities = m_Entities.AsDeferredJobArray()
		};

		var collisionJobHandle = collisionForceJob.Schedule(minionCount, SimulationState.BigBatchSize, prepareJobHandle);
		
		// 等待作业完成
		collisionJobHandle.Complete();
		
		// 将更新后的数据写回到实体
		minionsQuery.CopyFromComponentDataArray(velocitiesArray);
		minionsQuery.CopyFromComponentDataArray(attackDataArray);
		
		// 清理临时分配的数组
		entitiesArray.Dispose();
		transformsArray.Dispose();
		velocitiesArray.Dispose();
		bitmaskArray.Dispose();
		attackDataArray.Dispose();
	}
}
