using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(FormationSystem))]
public partial class FormationMaintenanceSystem : SystemBase
{
	private EntityQuery formationsQuery;
	private EntityQuery minionsQuery;
	private BufferLookup<EntityRef> formationsUnitDataLookup;
	private ComponentLookup<IndexInFormationData> indicesInFormationLookup;
	private ComponentLookup<FormationData> formationDataLookup;

	protected override void OnCreate()
	{
		formationsQuery = GetEntityQuery(
			ComponentType.ReadOnly<FormationData>()
		);

		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<IndexInFormationData>()
		);

		formationsUnitDataLookup = GetBufferLookup<EntityRef>();
		indicesInFormationLookup = GetComponentLookup<IndexInFormationData>();
		formationDataLookup = GetComponentLookup<FormationData>(true);
	}

	protected override void OnUpdate()
	{
		if (formationsQuery.IsEmpty) return;

		// 更新组件查询
		formationsUnitDataLookup.Update(this);
		indicesInFormationLookup.Update(this);
		formationDataLookup.Update(this);

		// 获取组件数据
		var formationDataArray = formationsQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
		var formationEntities = formationsQuery.ToEntityArray(Allocator.TempJob);

		var minionTransformsArray = minionsQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var minionIndicesArray = minionsQuery.ToComponentDataArray<IndexInFormationData>(Allocator.TempJob);
		var minionEntitiesArray = minionsQuery.ToEntityArray(Allocator.TempJob);

		// 第一步：清除单位数据
		var clearUnitDataJob = new ClearUnitDataJob
		{
			formations = formationDataArray,
			formationEntities = formationEntities,
			formationsUnitDataLookup = formationsUnitDataLookup
		};
		var clearJobHandle = clearUnitDataJob.Schedule(Dependency);

		// 第二步：填充单位数据
		var fillUnitJob = new FillUnitDataJob
		{
			formationsUnitDataLookup = formationsUnitDataLookup,
			transforms = minionTransformsArray,
			indicesInFormation = minionIndicesArray,
			minionEntities = minionEntitiesArray
		};
		var fillJobHandle = fillUnitJob.Schedule(clearJobHandle);

		// 第三步：重新排列单位索引
		var rearrangeJob = new RearrangeUnitIndexesJob
		{
			formations = formationDataArray,
			formationEntities = formationEntities,
			formationsUnitDataLookup = formationsUnitDataLookup,
			indicesInFormation = indicesInFormationLookup
		};
		var rearrangeJobHandle = rearrangeJob.Schedule(fillJobHandle);
		rearrangeJobHandle.Complete();

		// 清理临时分配的数组
		formationDataArray.Dispose();
		formationEntities.Dispose();
		minionTransformsArray.Dispose();
		minionIndicesArray.Dispose();
		minionEntitiesArray.Dispose();

		Dependency = rearrangeJobHandle;
	}

	[BurstCompile]
	private struct ClearUnitDataJob : IJob
	{
		[ReadOnly]
		public NativeArray<FormationData> formations;
		[ReadOnly]
		public NativeArray<Entity> formationEntities;
		public BufferLookup<EntityRef> formationsUnitDataLookup;

		public void Execute()
		{
			for (int index = 0; index < formationEntities.Length; index++) {
				var formationEntity = formationEntities[index];
				var buffer = formationsUnitDataLookup[formationEntity];
				var len = math.max(formations[index].SpawnedCount, formations[index].UnitCount);

				for (var i = 0; i < len; i++)
				{
					if (i < buffer.Length)
					{
						buffer[i] = new EntityRef();
					}
				}
			}
		}
	}

	[BurstCompile]
	private struct FillUnitDataJob : IJob // 这不能是并行作业，因为BufferLookup不支持并行写入
	{
		public BufferLookup<EntityRef> formationsUnitDataLookup;
		[ReadOnly]
		public NativeArray<UnitTransformData> transforms;
		[ReadOnly]
		public NativeArray<IndexInFormationData> indicesInFormation;
		[ReadOnly]
		public NativeArray<Entity> minionEntities;

		public void Execute()
		{
			for (var index = 0; index < minionEntities.Length; ++index)
			{
				var formationEntity = transforms[index].FormationEntity;
				if (!formationsUnitDataLookup.HasBuffer(formationEntity)) continue;

				var buffer = formationsUnitDataLookup[formationEntity];
				var indexInFormation = indicesInFormation[index].IndexInFormation;
				
				if (indexInFormation < buffer.Length)
				{
					buffer[indexInFormation] = new EntityRef(minionEntities[index]);
				}
			}
		}
	}

	[BurstCompile]
	private struct RearrangeUnitIndexesJob : IJob
	{
		[ReadOnly]
		public NativeArray<FormationData> formations;
		[ReadOnly]
		public NativeArray<Entity> formationEntities;
		public BufferLookup<EntityRef> formationsUnitDataLookup;

		public ComponentLookup<IndexInFormationData> indicesInFormation;

		public void Execute()
		{
			// 创建一个临时字典来存储需要更新的实体和它们的新索引
			var entitiesToUpdate = new NativeHashMap<Entity, int>(1024, Allocator.Temp);
			
			for (int index = 0; index < formationEntities.Length; index++)
			{
				var formationEntity = formationEntities[index];
				if (!formationsUnitDataLookup.HasBuffer(formationEntity)) continue;

				var buffer = formationsUnitDataLookup[formationEntity];
				var len = math.max(formations[index].SpawnedCount, formations[index].UnitCount);
				len = math.min(len, buffer.Length);

				for (var i = 0; i < len; i++)
				{
					if (buffer[i].entity != Entity.Null) continue;
					
					// 寻找合适的索引
					int j;
					for (j = i + 1; j < len; j++)
					{
						if (buffer[j].entity == Entity.Null) continue;
						
						// 找到了索引，进行替换
						buffer[i] = buffer[j];
						buffer[j] = new EntityRef();
						
						// 将需要更新的实体和新索引添加到字典中
						if (indicesInFormation.HasComponent(buffer[i].entity))
						{
							entitiesToUpdate.TryAdd(buffer[i].entity, i);
						}

						break;
					}

					// 到末尾都没有可用的索引
					if (j == len) break;
				}
			}
			
			// 在所有缓冲区更新完成后，更新实体的索引
			var enumerator = entitiesToUpdate.GetEnumerator();
			while (enumerator.MoveNext())
			{
				var entity = enumerator.Current.Key;
				var newIndex = enumerator.Current.Value;
				
				var indexData = indicesInFormation[entity];
				indexData.IndexInFormation = newIndex;
				indicesInFormation[entity] = indexData;
			}
			
			// 释放临时字典
			entitiesToUpdate.Dispose();
		}
	}
}
