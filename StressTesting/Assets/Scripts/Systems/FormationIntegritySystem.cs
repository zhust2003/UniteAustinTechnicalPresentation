using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(PrepareMinionTargetsSystem))]
public partial class FormationIntegritySystem : SystemBase
{
    private EntityQuery formationsQuery;
    private ComponentLookup<MinionData> minionDataLookup;
    private ComponentLookup<UnitTransformData> minionTransformsLookup;
    private ComponentLookup<MinionTarget> minionTargetsLookup;
    private BufferLookup<EntityRef> entityRefBufferLookup;

    protected override void OnCreate()
    {
        formationsQuery = GetEntityQuery(
            ComponentType.ReadOnly<FormationIntegrityData>(),
            ComponentType.ReadOnly<FormationData>()
        );

        minionDataLookup = GetComponentLookup<MinionData>(true);
        minionTransformsLookup = GetComponentLookup<UnitTransformData>(true);
        minionTargetsLookup = GetComponentLookup<MinionTarget>(true);
        entityRefBufferLookup = GetBufferLookup<EntityRef>(true);
    }

    protected override void OnUpdate()
    {
        if (formationsQuery.IsEmpty) return;

        // 更新组件查询
        minionDataLookup.Update(this);
        minionTransformsLookup.Update(this);
        minionTargetsLookup.Update(this);
        entityRefBufferLookup.Update(this);

        // 获取组件数据
        var formationIntegrityDataArray = formationsQuery.ToComponentDataArray<FormationIntegrityData>(Allocator.TempJob);
        var formationDataArray = formationsQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
        var formationEntities = formationsQuery.ToEntityArray(Allocator.TempJob);

        // 创建并调度作业
        var calculateIntegrityDataJob = new CalculateIntegrityDataJob
        {
            formationIntegrityData = formationIntegrityDataArray,
            formationData = formationDataArray,
            formationEntities = formationEntities,
            minionDataLookup = minionDataLookup,
            minionTransformsLookup = minionTransformsLookup,
            minionTargetsLookup = minionTargetsLookup,
            entityRefBufferLookup = entityRefBufferLookup
        };

        // 正确设置依赖关系
        Dependency = calculateIntegrityDataJob.Schedule(formationDataArray.Length, SimulationState.BigBatchSize, Dependency);
        
        // 完成作业并等待结果
        Dependency.Complete();

        // 将计算结果写回到实体
        for (int i = 0; i < formationEntities.Length; i++)
        {
            EntityManager.SetComponentData(formationEntities[i], formationIntegrityDataArray[i]);
        }

        // 清理临时分配的数组
        formationIntegrityDataArray.Dispose();
        formationDataArray.Dispose();
        formationEntities.Dispose();
    }

    [BurstCompile]
    private struct CalculateIntegrityDataJob : IJobParallelFor
    {
        public NativeArray<FormationIntegrityData> formationIntegrityData;
        public NativeArray<FormationData> formationData;
        public NativeArray<Entity> formationEntities;

        [ReadOnly]
        public ComponentLookup<MinionData> minionDataLookup;

        [ReadOnly]
        public ComponentLookup<UnitTransformData> minionTransformsLookup;

        [ReadOnly]
        public ComponentLookup<MinionTarget> minionTargetsLookup;

        [ReadOnly]
        public BufferLookup<EntityRef> entityRefBufferLookup;
        
        public void Execute(int index)
        {
            var integrityData = new FormationIntegrityData();
            var formationEntity = formationEntities[index];
            var formation = formationData[index];
            
            // 使用 BufferLookup 而不是 EntityManager
            var buffer = entityRefBufferLookup[formationEntity];

            for (var i = 0; i < formation.UnitCount; ++i)
            {
                if (i >= buffer.Length) break;
                
                var unitEntity = buffer[i].entity;

                if (unitEntity == Entity.Null) break; // 如果是空实体，我们已经到达末尾
                
                if (!minionTransformsLookup.HasComponent(unitEntity) || 
                    !minionDataLookup.HasComponent(unitEntity) || 
                    !minionTargetsLookup.HasComponent(unitEntity))
                    continue;
                
                var unitTransform = minionTransformsLookup[unitEntity];
                var unitData = minionDataLookup[unitEntity];
                var target = minionTargetsLookup[unitEntity].Target;

                if (unitData.attackCycle >= 0)
                    ++integrityData.unitsAttacking;
                
                var distance = math.length(target - unitTransform.Position);
                
                if (distance < FormationPathFindSystem.FarDistance)
                {
                    ++integrityData.unitCount;
                    if (distance >= FormationPathFindSystem.CloseDistance)
                        ++integrityData.unitsClose;
                }
                else
                {
                    ++integrityData.unitsFar;
                }
            }

            formationIntegrityData[index] = integrityData;
        }
    }
}