using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

[UpdateAfter(typeof(ArrowSystem))]
public partial class ArrowRenderSystem : SystemBase
{
	private Material arrowMaterial;
	private Mesh arrowMesh;

	private ComputeBuffer argsBuffer;
	private ComputeBuffer objectToWorldBuffer;

	private const int ArrowCapacity = 50000;

	private NativeArray<uint> indirectArgs;
	private NativeArray<Matrix4x4> transformationMatrices;

	private EntityQuery arrowQuery;
	private JobHandle previousFrameHandle;
	public int prevLength = 0;

	protected override void OnCreate()
	{
		arrowQuery = GetEntityQuery(ComponentType.ReadOnly<ArrowData>());
		indirectArgs = new NativeArray<uint>(5, Allocator.Persistent);
		transformationMatrices = new NativeArray<Matrix4x4>(ArrowCapacity, Allocator.Persistent);
	}

	protected override void OnDestroy()
	{
		if (argsBuffer != null) argsBuffer.Dispose();
		if (objectToWorldBuffer != null) objectToWorldBuffer.Dispose();

		previousFrameHandle.Complete();
		if (transformationMatrices.IsCreated) transformationMatrices.Dispose();
		if (indirectArgs.IsCreated) indirectArgs.Dispose();
	}

	protected override void OnUpdate()
	{
		if (SimulationSettings.Instance.DisableRendering)
			return;

		if ((arrowMaterial == null || arrowMesh == null) && Application.isPlaying)
		{
			var arrowObject = GameObject.Find("Arrow");
			if (arrowObject != null)
			{
				argsBuffer = new ComputeBuffer(1, indirectArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
				objectToWorldBuffer = new ComputeBuffer(ArrowCapacity, 64);

				arrowMesh = arrowObject.GetComponent<MeshFilter>().sharedMesh;
				arrowMaterial = new Material(arrowObject.GetComponent<Renderer>().sharedMaterial);

				arrowMaterial.SetFloat("textureCoord", 1);
			}
			else Debug.LogError("Arrow object not found");
		}

		if (arrowMaterial == null || arrowMesh == null) return;

		arrowMaterial.SetBuffer("objectToWorldBuffer", objectToWorldBuffer);

		previousFrameHandle.Complete();

		objectToWorldBuffer.SetData(transformationMatrices);
		// Setup the args buffer
		indirectArgs[0] = arrowMesh.GetIndexCount(0);
		indirectArgs[1] = (uint)prevLength;
		argsBuffer.SetData(indirectArgs);

		Graphics.DrawMeshInstancedIndirect(arrowMesh, 0, arrowMaterial, new Bounds(Vector3.zero, 10000000 * Vector3.one), argsBuffer);

		int arrowCount = arrowQuery.CalculateEntityCount();
		prevLength = arrowCount;

		// 创建本地变量以避免捕获this
		var localTransformationMatrices = transformationMatrices;
		int localArrowCapacity = ArrowCapacity;

		// 确保NativeArray在作业中是有效的
		if (!localTransformationMatrices.IsCreated || arrowCount <= 0)
			return;

		var calculateMatricesJobHandle = Entities
			.WithName("CalculateArrowTransformationMatrix")
			.WithBurst()
			.ForEach((int entityInQueryIndex, in ArrowData arrow) =>
			{
				if (entityInQueryIndex >= localArrowCapacity) return;

				float3 f = math.normalize(arrow.velocity);
				float3 r = math.cross(f, new float3(0, 1, 0));
				float3 u = math.cross(f, r);
				float3 p = arrow.position;

				var transform = new Matrix4x4(
					new Vector4(r.x, r.y, r.z, 0),
					new Vector4(u.x, u.y, u.z, 0),
					new Vector4(f.x, f.y, f.z, 0),
					new Vector4(p.x, p.y, p.z, 1f));

				localTransformationMatrices[entityInQueryIndex] = transform;
			})
			.ScheduleParallel(Dependency);

		previousFrameHandle = calculateMatricesJobHandle;
		Dependency = calculateMatricesJobHandle;
	}
}
