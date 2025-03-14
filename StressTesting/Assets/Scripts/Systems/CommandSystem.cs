using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(UnityEngine.PlayerLoop.EarlyUpdate))]
public partial class CommandSystem : SystemBase
{
	public static NativeQueue<AttackCommand> AttackCommands;
	public static NativeQueue<AttackCommand>.ParallelWriter AttackCommandsConcurrent;

	public static JobHandle AttackCommandsFence;
	public static JobHandle AttackCommandsConcurrentFence;

	private const int AttackCommandBufferSize = 10000;
	private ComponentLookup<MinionData> minions;

	protected override void OnCreate()
	{
		minions = GetComponentLookup<MinionData>();
	}

	protected override void OnDestroy()
	{
		if (AttackCommands.IsCreated) AttackCommands.Dispose();
	}

	protected override void OnUpdate()
	{
		if (!AttackCommands.IsCreated)
		{
			AttackCommands = new NativeQueue<AttackCommand>(Allocator.Persistent);
			AttackCommandsConcurrent = AttackCommands.AsParallelWriter();
		}

		AttackCommandsConcurrentFence.Complete();

		// 更新组件查询
		minions.Update(this);

		// 处理攻击命令
		var localMinions = minions;
		var localAttackCommands = AttackCommands;

		var attackCommandsJob = new AttackCommandsJob
		{
			minions = localMinions,
			attackCommands = localAttackCommands
		};

		AttackCommandsFence = attackCommandsJob.Schedule(Dependency);
		Dependency = AttackCommandsFence;
	}

	[BurstCompile]
	public struct AttackCommandsJob : IJob
	{
		public ComponentLookup<MinionData> minions;
		public NativeQueue<AttackCommand> attackCommands;

		public void Execute()
		{
			while (attackCommands.Count > 0)
			{
				var command = attackCommands.Dequeue();

				if (minions.HasComponent(command.DefenderEntity))
				{
					var minion = minions[command.DefenderEntity];
					minion.Health -= command.Damage;
					minions[command.DefenderEntity] = minion;
				}
			}
		}
	}
}
