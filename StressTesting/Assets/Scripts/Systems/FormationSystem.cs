using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Profiling;
using Unity.Burst;

// This system should depend on the crowd system, but as systems can depend only on one thing, then all the formation
// systems will execute before the prepare buckets system, which is bad. That is why this system depends on the buckets
// system, to ensure the buckets system begins execution.
[UpdateAfter(typeof(PrepareBucketsSystem))]
public partial class FormationSystem : SystemBase
{
	private EntityQuery formationsQuery;
	private ComponentLookup<IndexInFormationData> indicesInFormationLookup;

	//public static readonly int GroundLayermask = 1 << LayerMask.NameToLayer("Ground");
	public const int GroundLayermask = 1 << 8;

	// 添加复制组件数据的结构体
	[BurstCompile]
	private struct CopyComponentData<T> : IJobParallelFor where T : struct, IComponentData
	{
		[ReadOnly] public NativeArray<T> Source;
		[WriteOnly] public NativeArray<T> Results;

		public void Execute(int index)
		{
			Results[index] = Source[index];
		}
	}

	// 添加复制实体的结构体
	[BurstCompile]
	private struct CopyEntities : IJobParallelFor
	{
		[ReadOnly] public NativeArray<Entity> Source;
		[WriteOnly] public NativeArray<Entity> Results;

		public void Execute(int index)
		{
			Results[index] = Source[index];
		}
	}

	protected override void OnCreate()
	{
		formationsQuery = GetEntityQuery(
			ComponentType.ReadWrite<FormationData>(),
			ComponentType.ReadOnly<EntityRef>(),
			ComponentType.ReadWrite<FormationNavigationData>(),
			ComponentType.ReadWrite<FormationClosestData>(),
			ComponentType.ReadWrite<CrowdAgentNavigator>(),
			ComponentType.ReadOnly<CrowdAgent>(),
			ComponentType.ReadOnly<FormationHighLevelPath>()
		);

		indicesInFormationLookup = GetComponentLookup<IndexInFormationData>();
	}

	protected override void OnUpdate()
	{
		// Realloc();
		if (formationsQuery.IsEmpty) return;
		
		//NativeArrayExtensions.ResizeNativeArray(ref raycastHits, math.max(raycastHits.Length,minions.Length));
		//NativeArrayExtensions.ResizeNativeArray(ref raycastCommands, math.max(raycastCommands.Length, minions.Length));
		
		// 更新组件查询
		indicesInFormationLookup.Update(this);

		// 获取组件数据
		var formationDataArray = formationsQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
		var formationNavigationDataArray = formationsQuery.ToComponentDataArray<FormationNavigationData>(Allocator.TempJob);
		var formationClosestDataArray = formationsQuery.ToComponentDataArray<FormationClosestData>(Allocator.TempJob);
		var crowdAgentNavigatorArray = formationsQuery.ToComponentDataArray<CrowdAgentNavigator>(Allocator.TempJob);
		var crowdAgentArray = formationsQuery.ToComponentDataArray<CrowdAgent>(Allocator.TempJob);
		var formationHighLevelPathArray = formationsQuery.ToComponentDataArray<FormationHighLevelPath>(Allocator.TempJob);
		var formationEntities = formationsQuery.ToEntityArray(Allocator.TempJob);

		var copyNavigationJob = new CopyNavigationPositionToFormation
		{
			formations = formationDataArray,
			agents = crowdAgentArray,
			navigators = crowdAgentNavigatorArray,
			navigationData = formationNavigationDataArray,
			dt = SystemAPI.Time.DeltaTime
		};
		var copyNavigationJobHandle = copyNavigationJob.Schedule(formationDataArray.Length, SimulationState.SmallBatchSize);

		var copyFormations = new NativeArray<FormationData>(formationDataArray.Length, Allocator.TempJob);
		var copyFormationsJob = new CopyComponentData<FormationData>
		{
			Source = formationDataArray,
			Results = copyFormations
		};
		var copyFormationJobHandle = copyFormationsJob.Schedule(formationDataArray.Length, SimulationState.HugeBatchSize, copyNavigationJobHandle);
		
		var copyFormationEntities = new NativeArray<Entity>(formationEntities.Length, Allocator.TempJob);
		var copyFormationEntitiesJob = new CopyEntities
		{
			Source = formationEntities,
			Results = copyFormationEntities
		};
		var copyFormationEntitiesJobHandle = copyFormationEntitiesJob.Schedule(formationEntities.Length, SimulationState.HugeBatchSize, copyNavigationJobHandle);
		var copyBarrier = JobHandle.CombineDependencies(copyFormationJobHandle, copyFormationEntitiesJobHandle);
		
		var closestSearchJob = new SearchClosestFormations
		{
			formations = copyFormations,
			closestFormations = formationClosestDataArray,
			formationEntities = copyFormationEntities
		};
		var closestSearchJobHandle = closestSearchJob.Schedule(formationDataArray.Length, SimulationState.HugeBatchSize, copyBarrier);
		
		var updateFormationsJob = new UpdateFormations
		{
			closestFormations = formationClosestDataArray,
			formationHighLevelPath = formationHighLevelPathArray,
			formations = formationDataArray,
			formationNavigators = crowdAgentNavigatorArray
		};
		var updateFormationsJobHandle = updateFormationsJob.Schedule(formationDataArray.Length, SimulationState.SmallBatchSize, closestSearchJobHandle);
		
		// 等待所有作业完成
		updateFormationsJobHandle.Complete();

		// 将计算结果写回到实体
		for (int i = 0; i < formationEntities.Length; i++)
		{
			EntityManager.SetComponentData(formationEntities[i], formationDataArray[i]);
			EntityManager.SetComponentData(formationEntities[i], formationNavigationDataArray[i]);
			EntityManager.SetComponentData(formationEntities[i], formationClosestDataArray[i]);
			EntityManager.SetComponentData(formationEntities[i], crowdAgentNavigatorArray[i]);
		}

		// 清理临时分配的数组
		formationDataArray.Dispose();
		formationNavigationDataArray.Dispose();
		formationClosestDataArray.Dispose();
		crowdAgentNavigatorArray.Dispose();
		crowdAgentArray.Dispose();
		formationHighLevelPathArray.Dispose();
		formationEntities.Dispose();
		copyFormations.Dispose();
		copyFormationEntities.Dispose();
	}
	
	[BurstCompile]
	private struct UpdateFormations : IJobParallelFor
	{
		public NativeArray<FormationData> formations;
		[ReadOnly] public NativeArray<FormationClosestData> closestFormations;
		[ReadOnly] public NativeArray<FormationHighLevelPath> formationHighLevelPath;

		public NativeArray<CrowdAgentNavigator> formationNavigators;

		public void Execute(int index)
		{
			var navigator = formationNavigators[index];

			var formation = formations[index];

#if DEBUG_CROWDSYSTEM && !ENABLE_HLVM_COMPILER
			Debug.Assert(navigator.active || formation.FormationState == FormationData.State.AllDead);
#endif

			if (formation.FormationState == FormationData.State.AllDead)
			{
				if (navigator.active)
				{
					navigator.active = false;
					formationNavigators[index] = navigator;
				}
				return;
			}

			float3 targetPosition = formationNavigators[index].requestedDestination;
			bool foundTargetPosition = false;
			if (closestFormations[index].closestFormation != Entity.Null)
			{
				var closestPosition = closestFormations[index].closestFormationPosition;

				// Aggro distance of 75
				if (formation.EnableAgro && math.distance(closestPosition, formation.Position) < 75)
				{
					targetPosition = closestPosition;
					foundTargetPosition = true;
				}
			}

			if (!foundTargetPosition && formation.EnableHighLevelPath)
			{
				int nextPathIndex = formation.HighLevelPathIndex;

				do
				{
					targetPosition = formationHighLevelPath[index].GetTarget(nextPathIndex);
					nextPathIndex++;
				}
				while (math.distance(targetPosition.xz, formation.Position.xz) < 0.1f && nextPathIndex <= 3);
				formation.HighLevelPathIndex = nextPathIndex - 1;
			}

			if (math.distance(formationNavigators[index].requestedDestination.xz, targetPosition.xz) > 0.1f)
			{
				navigator.MoveTo(targetPosition);
			}

			formationNavigators[index] = navigator;
			formations[index] = formation;
		}
	}

	[BurstCompile]
	private struct SearchClosestFormations : IJobParallelFor
	{
		[ReadOnly] public NativeArray<FormationData> formations;
		[ReadOnly] public NativeArray<Entity> formationEntities;
		public NativeArray<FormationClosestData> closestFormations;

		public void Execute(int index)
		{
			if (formations[index].FormationState == FormationData.State.AllDead) return;
			var data = closestFormations[index];
			float d = float.PositiveInfinity;
			int closestIndex = -1;

			for (int i = 0; i < formations.Length; i++)
			{
				if (!(i == index || formations[i].FormationState == FormationData.State.AllDead || formations[i].IsFriendly == formations[index].IsFriendly))
				{
					float3 relative = formations[index].Position - formations[i].Position;
					float newD = math.dot(relative, relative);

					if (newD < d)
					{
						d = newD;
						closestIndex = i;
					}
				}
			}

			if(closestIndex != -1) data.closestFormation = formationEntities[closestIndex]; else data.closestFormation = Entity.Null;
			if (closestIndex != -1) data.closestFormationPosition = formations[closestIndex].Position;

			closestFormations[index] = data;
		}
	}

	[BurstCompile]
	private struct CopyNavigationPositionToFormation : IJobParallelFor
	{
		public NativeArray<FormationData> formations;
		[ReadOnly]
		public NativeArray<CrowdAgent> agents;
		[ReadOnly]
		public NativeArray<CrowdAgentNavigator> navigators;
		public NativeArray<FormationNavigationData> navigationData;
		[ReadOnly]
		public float dt;

		public void Execute(int index)
		{
			var formation = formations[index];
			var prevPosition = formation.Position;

			formation.Position = agents[index].worldPosition;

			var forward = formation.Position - prevPosition;
			forward.y = 0;

			// If we are moving we should assign a new forward vector.
			if (!MathUtil.Approximately(math.dot(forward, forward), 0))
			{
				forward = math.normalize(forward);
				formation.Forward = Vector3.RotateTowards(formation.Forward, forward, 0.314f * dt, 1);
			}

			var navData = navigationData[index];

			var targetRelative = navData.TargetPosition - navigators[index].steeringTarget;
			if (!MathUtil.Approximately(math.dot(targetRelative, targetRelative), 0))
			{
				// We got a next corner
				navData.initialCornerDistance = math.length(formation.Position - navigators[index].steeringTarget);
				navData.prevFormationSide = formation.formationSide;
				navData.TargetPosition = navigators[index].steeringTarget;

				navigationData[index] = navData;
			}

			if (navData.initialCornerDistance != 0) formation.formationSide = math.lerp(navData.prevFormationSide, navigators[index].nextCornerSide,
																math.clamp(1 - math.length(formation.Position - navData.TargetPosition) / navData.initialCornerDistance, 0, 1));


			formations[index] = formation;
		}
	}
}