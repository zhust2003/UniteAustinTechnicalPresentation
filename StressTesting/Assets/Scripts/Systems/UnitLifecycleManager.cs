using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Assets.Instancing.Skinning.Scripts.ECS;
using UnityEngine.Profiling;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

[UpdateAfter(typeof(SpellSystem))]
public partial class UnitLifecycleManager : SystemBase
{
	private EntityQuery unitsQuery;
	private EntityQuery dyingUnitsQuery;
	private EntityQuery dyingArrowsQuery;
	
	private SpellSystem spellSystem;

	public NativeQueue<Entity> queueForKillingEntities;
	public NativeQueue<Entity> deathQueue;
	public NativeQueue<Entity> entitiesForFlying;

	public int MaxDyingUnitsPerFrame = 250;

	public NativeQueue<ArrowData> createdArrows;
	private const int CreatedArrowsQueueSize = 100000;

	private const int DeathQueueSize = 80000;

	private Queue<Entity> entitiesThatNeedToBeKilled = new Queue<Entity>(100000);

	protected override void OnCreate()
	{
		unitsQuery = GetEntityQuery(
			ComponentType.ReadWrite<UnitTransformData>(),
			ComponentType.ReadWrite<MinionTarget>(),
			ComponentType.ReadWrite<RigidbodyData>(),
			ComponentType.ReadWrite<TextureAnimatorData>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadWrite<MinionPathData>()
		);
		
		dyingUnitsQuery = GetEntityQuery(
			ComponentType.ReadOnly<DyingUnitData>(),
			ComponentType.ReadWrite<UnitTransformData>()
		);
		
		dyingArrowsQuery = GetEntityQuery(
			ComponentType.ReadOnly<DyingUnitData>(),
			ComponentType.ReadOnly<ArrowData>()
		);
		
		spellSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SpellSystem>();
		
		queueForKillingEntities = new NativeQueue<Entity>(Allocator.Persistent);
		deathQueue = new NativeQueue<Entity>(Allocator.Persistent);
		createdArrows = new NativeQueue<ArrowData>(Allocator.Persistent);
		entitiesForFlying = new NativeQueue<Entity>(Allocator.Persistent);
	}

	protected override void OnDestroy()
	{
		if (queueForKillingEntities.IsCreated) queueForKillingEntities.Dispose();
		if (deathQueue.IsCreated) deathQueue.Dispose();
		if (createdArrows.IsCreated) createdArrows.Dispose();
		if (entitiesForFlying.IsCreated) entitiesForFlying.Dispose();
	}

	protected override void OnUpdate()
	{
		if (unitsQuery.CalculateEntityCount() == 0) return;
		
		Profiler.BeginSample("Explosion wait");
		spellSystem.CombinedExplosionHandle.Complete(); // TODO try to remove this 
		Profiler.EndSample();
		
		Dependency.Complete();
		
		Profiler.BeginSample("Spawn ");

		// 确保所有队列都已创建
		if (!deathQueue.IsCreated) deathQueue = new NativeQueue<Entity>(Allocator.Persistent);
		if (!createdArrows.IsCreated) createdArrows = new NativeQueue<ArrowData>(Allocator.Persistent);
		if (!queueForKillingEntities.IsCreated) queueForKillingEntities = new NativeQueue<Entity>(Allocator.Persistent);
		if (!entitiesForFlying.IsCreated) entitiesForFlying = new NativeQueue<Entity>(Allocator.Persistent);

		// 处理创建的箭矢
		int arrowsProcessed = 0;
		int maxArrowsPerFrame = 1000; // 限制每帧处理的箭矢数量
		while (createdArrows.Count > 0 && arrowsProcessed < maxArrowsPerFrame)
		{
			var data = createdArrows.Dequeue();
			Spawner.Instance.SpawnArrow(data);
			arrowsProcessed++;
		}

		// 创建一个本地变量来存储deathQueue，避免在作业中捕获this
		var localDeathQueue = deathQueue.AsParallelWriter();
		
		// 清理死亡单位
		var cleanupJobHandle = Entities
			.WithName("CleanupJob")
			.WithAll<MinionData>()
			.ForEach((Entity entity, in MinionData minionData) =>
			{
				if (minionData.Health <= 0)
				{
					localDeathQueue.Enqueue(entity);
				}
			})
			.WithoutBurst()
			.Schedule(Dependency);

		// 移动死亡单位到地下
		float currentTime = (float)SystemAPI.Time.ElapsedTime;
		var moveUnitsJobHandle = Entities
			.WithName("MoveUnitsBelowGround")
			.WithAll<DyingUnitData>()
			.ForEach((Entity entity, ref UnitTransformData transform, in DyingUnitData dyingData) =>
			{
				if (currentTime > dyingData.TimeAtWhichToExpire - 2f)
				{
					float t = (dyingData.TimeAtWhichToExpire - currentTime) / 2f;
					transform.Position.y = math.lerp(dyingData.StartingYCoord, dyingData.StartingYCoord - 1f, 1 - t);
				}
			})
			.WithoutBurst()
			.Schedule(cleanupJobHandle);

		Profiler.EndSample();

		Profiler.BeginSample("LifeCycleManager - Main Thread");

		float time = (float)SystemAPI.Time.ElapsedTime;
		
		// 创建一个本地变量来存储queueForKillingEntities，避免在作业中捕获this
		var localQueueForKillingEntities = queueForKillingEntities.AsParallelWriter();
		
		// 处理死亡单位
		var dyingUnitsJobHandle = Entities
			.WithName("ProcessDyingUnits")
			.WithAll<DyingUnitData, UnitTransformData>()
			.ForEach((Entity entity, in DyingUnitData dyingData) =>
			{
				if (time > dyingData.TimeAtWhichToExpire)
				{
					localQueueForKillingEntities.Enqueue(entity);
				}
			})
			.WithoutBurst()
			.Schedule(moveUnitsJobHandle);

		// 处理死亡箭矢
		var dyingArrowsJobHandle = Entities
			.WithName("ProcessDyingArrows")
			.WithAll<DyingUnitData, ArrowData>()
			.ForEach((Entity entity, in DyingUnitData dyingData) =>
			{
				if (time > dyingData.TimeAtWhichToExpire)
				{
					localQueueForKillingEntities.Enqueue(entity);
				}
			})
			.WithoutBurst()
			.Schedule(dyingUnitsJobHandle);
		
		// 确保所有作业完成
		dyingArrowsJobHandle.Complete();

		Profiler.EndSample();
		Profiler.BeginSample("Queue processing");

		float timeForUnitExpiring = time + 5f;
		float timeForArrowExpiring = time + 1f;

		Profiler.BeginSample("Death queue");
		int processed = 0;
		int maxProcessedPerFrame = 1000; // 限制每帧处理的实体数量
		while (deathQueue.Count > 0 && processed < maxProcessedPerFrame)
		{
			var entityToKill = deathQueue.Dequeue();
			if (EntityManager.Exists(entityToKill) && EntityManager.HasComponent<MinionData>(entityToKill))
			{
				EntityManager.RemoveComponent<MinionData>(entityToKill);
				entitiesThatNeedToBeKilled.Enqueue(entityToKill);
			}

			if (EntityManager.Exists(entityToKill) && EntityManager.HasComponent<ArrowData>(entityToKill))
			{
				EntityManager.AddComponentData(entityToKill, new DyingUnitData(timeForArrowExpiring, 0));
			}
			processed++;
		}
		Profiler.EndSample();

		Profiler.BeginSample("Explosion wait");
		spellSystem.CombinedExplosionHandle.Complete();
		Profiler.EndSample();

		Profiler.BeginSample("Killing minionEntities");
		// TODO try batched replacing 
		processed = 0;
		while (entitiesThatNeedToBeKilled.Count > 0 && processed < MaxDyingUnitsPerFrame)
		{
			processed++;
			var entityToKill = entitiesThatNeedToBeKilled.Dequeue();
			if (EntityManager.Exists(entityToKill) && EntityManager.HasComponent<MinionTarget>(entityToKill))
			{
				EntityManager.RemoveComponent<MinionTarget>(entityToKill);
				if (EntityManager.HasComponent<AliveMinionData>(entityToKill)) EntityManager.RemoveComponent<AliveMinionData>(entityToKill);

				var textureAnimatorData = EntityManager.GetComponentData<TextureAnimatorData>(entityToKill);
				textureAnimatorData.NewAnimationId = AnimationName.Death;
				var transform = EntityManager.GetComponentData<UnitTransformData>(entityToKill);
				EntityManager.AddComponentData(entityToKill, new DyingUnitData(timeForUnitExpiring, transform.Position.y));

				EntityManager.SetComponentData(entityToKill, textureAnimatorData);

				var formations = GetComponentLookup<FormationData>();
				if (formations.HasComponent(transform.FormationEntity))
				{
					var formation = formations[transform.FormationEntity];
					formation.UnitCount--;
					formation.Width = (int)math.ceil((math.sqrt(formation.UnitCount / 2f) * 2f));
					if (formation.UnitCount == 0)
						formation.FormationState = FormationData.State.AllDead;
					formations[transform.FormationEntity] = formation;
				}
			}
		}
		Profiler.EndSample();

		processed = 0;
		Profiler.BeginSample("Flying queue");
		while (entitiesForFlying.Count > 0 && processed < MaxDyingUnitsPerFrame)
		{
			processed++;
			var entity = entitiesForFlying.Dequeue();
			if (EntityManager.Exists(entity) && !EntityManager.HasComponent<FlyingData>(entity))
			{
				if (EntityManager.HasComponent(entity, typeof(AliveMinionData))) EntityManager.RemoveComponent<AliveMinionData>(entity);
				EntityManager.AddComponentData(entity, new FlyingData());
			}
		}
		Profiler.EndSample();

		Profiler.BeginSample("Destroying entities");
		processed = 0;
		while (queueForKillingEntities.Count > 0 && processed < maxProcessedPerFrame)
		{
			var entity = queueForKillingEntities.Dequeue();
			if (EntityManager.Exists(entity))
			{
				EntityManager.DestroyEntity(entity);
			}
			processed++;
		}
		Profiler.EndSample();

		Profiler.EndSample();
	}
}
