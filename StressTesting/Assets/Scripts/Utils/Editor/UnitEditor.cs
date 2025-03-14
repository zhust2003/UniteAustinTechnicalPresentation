using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Entities;
using System.Reflection;
using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Experimental.AI;
using Unity.Jobs;

public class UnitEditor :  EditorWindow
{

	[MenuItem("Tools/Unit Editor")]
	public static void OpenWindow()
	{
		UnitEditor window = (UnitEditor)EditorWindow.GetWindow(typeof(UnitEditor));
		window.Show();
	}

	[MenuItem("Tools/Spawn Units")]
	public static void SpawnUnits()
	{
		if (EditorHelperSystem.I == null) return;


		EditorHelperSystem.I.spawnerSystem.InitialSpawning();
	}
	
	public static IEnumerable<FieldInfo> GetAllFields(Type t)
	{
		return t.GetFields ();
	}

	public static Dictionary<string, string> GetAllFields(Type t, object o)
	{
		Dictionary<string, string> dic = new Dictionary<string, string>();
		if (o == null)
			return dic;
		dic.Add (t.Name, " ");
		//dic.Add ("Type", t.ToString());

		foreach(FieldInfo info in GetAllFields(t))
		{
			string name = t.ToString() + "." + info.Name;
			if (info.FieldType == typeof(NavMeshLocation))
			{
				var value = (NavMeshLocation)info.GetValue(o);
				dic.Add(name, (value.polygon.IsNull() ? "invalid" : "valid") + " " + value.position);
			}
			else dic.Add(name, info.GetValue(o).ToString());
		}
		return dic;
	}

	public void OnEnable()
	{
		SceneView.onSceneGUIDelegate -= OnSceneGUI;
		SceneView.onSceneGUIDelegate += OnSceneGUI;

		EditorApplication.update -= Repaint;
		EditorApplication.update += Repaint;
	}

	public void OnDisable()
	{
		SceneView.onSceneGUIDelegate -= OnSceneGUI;
		EditorApplication.update -= Repaint;
	}

	public Vector2 scrollview = Vector2.zero;

	public bool DrawFormations = true;
	public bool DrawFormationsNavigation = false;

	public bool DrawUnitPathFinding = false;

	public bool SelectFormations = false;
	public bool SelectUnits = false;

	public int formationId;
	public int unitId;

	public void OnGUI()
	{
		GUILayout.BeginVertical("Box");
		DrawFormations = EditorGUILayout.ToggleLeft("Draw formations", DrawFormations);
		DrawFormationsNavigation = EditorGUILayout.ToggleLeft("Draw formations navigation", DrawFormationsNavigation);
		GUILayout.EndVertical();

		GUILayout.BeginVertical("Box");
		DrawUnitPathFinding = EditorGUILayout.ToggleLeft("Draw lost unit path finding", DrawUnitPathFinding);
		GUILayout.EndVertical();

		GUILayout.BeginVertical("Box");
		SelectFormations = EditorGUILayout.ToggleLeft("Select formations", SelectFormations);
		if (SelectFormations) formationId = EditorGUILayout.IntField("Selected Formation", formationId);
		GUILayout.EndVertical();

		GUILayout.BeginVertical("Box");
		SelectUnits = EditorGUILayout.ToggleLeft("Select Units", SelectUnits);
		if (SelectUnits) unitId = EditorGUILayout.IntField("Selected Unit", unitId);
		GUILayout.EndVertical();
		
		if (!Application.isPlaying || EditorHelperSystem.I.formations.Length == 0)
		{
			return;
		}

		GUILayout.BeginVertical("Box");

		try
		{
			GUILayout.Label("Minion system minions: " + EditorHelperSystem.I.minions.Length);
			GUILayout.Label("Attack queue: " + CommandSystem.AttackCommands.Count);
		}
		catch (Exception) { }

		GUILayout.EndVertical();

		if (EditorHelperSystem.I.integritySystem != null) EditorHelperSystem.I.CompleteDependency();

		formationId = Mathf.Clamp(formationId, 0, EditorHelperSystem.I.formations.Length - 1);
		unitId = Mathf.Clamp(unitId, 0, EditorHelperSystem.I.minions.Length - 1);

		scrollview = EditorGUILayout.BeginScrollView (scrollview);
		
		if (SelectFormations)
		{
			WriteSelectedFormation();
		}
		if (SelectUnits)
		{
			WriteSelectedUnit();
		}

		CommonDrawings();
		EditorGUILayout.EndScrollView();

	}

	public static void DrawFormation(FormationData formation)
	{
		if (EditorApplication.isPaused) return;

		float3 pos1, pos2, pos3, pos4;

		if (formation.Width == 0)
		{
			pos1 = formation.Position + new float3(1, 0, 1);
			pos2 = formation.Position + new float3(-1, 0, 1);
			pos3 = formation.Position + new float3(1, 0, -1);
			pos4 = formation.Position + new float3(-1, 0, -1);
		}
		else
		{
			pos1 = formation.Position + formation.GetOffsetFromCenter(0);
			pos2 = formation.Position + formation.GetOffsetFromCenter(formation.Width - 1);
			pos3 = formation.Position + formation.GetOffsetFromCenter(formation.UnitCount - formation.Width);
			pos4 = formation.Position + formation.GetOffsetFromCenter(formation.UnitCount - 1);
		}

		Color c = formation.IsFriendly ? Color.blue : Color.red;

		// Draw the position of the formation
		Debug.DrawLine(pos1, pos2, c, Time.deltaTime * 2);
		Debug.DrawLine(pos2, pos3, c, Time.deltaTime * 2);
		Debug.DrawLine(pos3, pos4, c, Time.deltaTime * 2);
		Debug.DrawLine(pos4, pos1, c, Time.deltaTime * 2);
		Debug.DrawLine(pos3, pos1, c, Time.deltaTime * 2);
		Debug.DrawLine(pos4, pos2, c, Time.deltaTime * 2);

	}

	public static void DrawNavigation(CrowdAgentNavigator navigator, CrowdAgent agent)
	{
		if (EditorApplication.isPaused) return;
		Debug.DrawLine(agent.worldPosition, navigator.requestedDestination, Color.green, Time.deltaTime * 2);
	}

	public static void DrawUnitPaths(MinionPathData minionPathInfo, NativeArray<float3> path)
	{
		if (EditorApplication.isPaused) return;
		if ((minionPathInfo.bitmasks & 4) == 0)
		{
			// No path finding
			return;
		}

		for (int i = minionPathInfo.currentCornerIndex; i < minionPathInfo.pathSize; i++)
		{
			if (i < 1) continue;
			Debug.DrawLine(path[i - 1], path[i], Color.green, Time.deltaTime * 2);
		}
	}

	public void WriteSelectedFormation()
	{
		if (EditorHelperSystem.I == null || EditorHelperSystem.I.formations.Length == 0)
			return;

		int i = formationId;
		
		GUILayout.BeginVertical("Box");

		foreach (KeyValuePair<string, string> pair in GetAllFields(EditorHelperSystem.I.formations.data[i].GetType(), EditorHelperSystem.I.formations.data[i]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach(KeyValuePair<string, string> pair in GetAllFields(EditorHelperSystem.I.formations.agents[i].GetType(), EditorHelperSystem.I.formations.agents[i]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach(KeyValuePair<string, string> pair in GetAllFields(EditorHelperSystem.I.formations.navigators[i].GetType(), EditorHelperSystem.I.formations.navigators[i]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach(KeyValuePair<string, string> pair in GetAllFields(EditorHelperSystem.I.formations.closestFormations[i].GetType(), EditorHelperSystem.I.formations.closestFormations[i]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(EditorHelperSystem.I.formations.highLevelPaths[i].GetType(), EditorHelperSystem.I.formations.highLevelPaths[i]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(EditorHelperSystem.I.formations.integrityData[i].GetType(), EditorHelperSystem.I.formations.integrityData[i]))
		{
			DrawField(pair.Key, pair.Value);
		}

		GUILayout.EndVertical();
	}

	public void WriteSelectedUnit()
	{
		var e = EditorHelperSystem.I;
		if (e == null || e.minions.Length == 0) return;

		GUILayout.BeginVertical("Box");

		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(MinionData), e.minions.data[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(NavMeshLocation), e.minions.locationComponents[unitId].NavMeshLocation))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(UnitTransformData), e.minions.transforms[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(MinionAttackData), e.minions.attackData[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(MinionTarget), e.minions.targets[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(MinionPathData), e.minions.pathsInfo[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (KeyValuePair<string, string> pair in GetAllFields(typeof(MinionBitmask), e.minions.bitmask[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (var pair in GetAllFields(typeof(TextureAnimatorData), e.minions.animationData[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}
		foreach (var pair in GetAllFields(typeof(RigidbodyData), e.minions.rigidbodies[unitId]))
		{
			DrawField(pair.Key, pair.Value);
		}

		GUILayout.EndVertical();
	}

	public void DrawField(string name, string value)
	{
		GUI.color = value == " " ? Color.green : Color.white;
		GUILayout.BeginHorizontal("Box");
		GUI.color = Color.white;

		GUILayout.Label(name);
		GUILayout.FlexibleSpace();
		GUILayout.Label(value);

		GUILayout.EndHorizontal();
	}

	public void CommonDrawings()
	{
		//var pos1 = FindClosestUnit.transform.Position;
		//var pos2 = FindClosestUnit.formation.Position +  FindClosestUnit.formation.GetOffsetFromCenter (FindClosestUnit.transform.IndexInFormation);
		//Debug.DrawLine (pos1, pos2, Color.yellow, Time.deltaTime * 2);

		for (int i = 0; i < EditorHelperSystem.I.formations.Length; i++)
		{
			if (DrawFormations) DrawFormation(EditorHelperSystem.I.formations.data[i]);
			if (DrawFormationsNavigation) DrawNavigation(EditorHelperSystem.I.formations.navigators[i], EditorHelperSystem.I.formations.agents[i]);
		}
		
		if (DrawUnitPathFinding)
		{
			for (int i = 0; i < EditorHelperSystem.I.minions.Length; i++)
			{
				DrawUnitPaths(EditorHelperSystem.I.minions.pathsInfo[i], EditorHelperSystem.I.minions.paths);
			}
		}
		
		if (SelectFormations && !EditorApplication.isPaused)
		{
			var formation = EditorHelperSystem.I.formations.data[formationId];
			for (int j = 0; j < formation.UnitCount; j++)
			{
				Debug.DrawLine(formation.Position, formation.Position + formation.GetOffsetFromCenter(j), Color.yellow, Time.deltaTime * 2);
			}
		}

		if (SelectUnits)
		{
			var unit = EditorHelperSystem.I.minions.transforms[unitId];
			Color c = (EditorHelperSystem.I.minions.bitmask[unitId].IsFriendly) ? Color.blue : Color.red;

			Debug.DrawLine(unit.Position + new float3(1, 0, 1), unit.Position + new float3(1, 0, -1), c, Time.deltaTime * 2);
			Debug.DrawLine(unit.Position + new float3(1, 0, 1), unit.Position + new float3(-1, 0, 1), c, Time.deltaTime * 2);
			Debug.DrawLine(unit.Position + new float3(-1, 0, -1), unit.Position + new float3(1, 0, -1), c, Time.deltaTime * 2);
			Debug.DrawLine(unit.Position + new float3(-1, 0, -1), unit.Position + new float3(-1, 0, 1), c, Time.deltaTime * 2);
		}
	}

	public void OnSceneGUI(SceneView v)
	{
		var e = EditorHelperSystem.I;

		if (e == null) return;
		if (e.formations.Length == 0 || e.minions.Length == 0 || (!SelectFormations && !SelectUnits)) return;
		
		if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
		{
			Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
			RaycastHit hit;

			if (Physics.Raycast(ray, out hit, Mathf.Infinity, FormationSystem.GroundLayermask))
			{
				Repaint();

				// Select formations
				if (SelectFormations)
				{
					int closestIndex = 0;
					for (int i = 1; i < e.formations.Length; i++)
					{
						if (math.distance(e.formations.data[closestIndex].Position, hit.point) >
							math.distance(e.formations.data[i].Position, hit.point))
						{
							closestIndex = i;
						}
					}

					formationId = closestIndex;
				}

				// Select units
				if (SelectUnits)
				{
					int closestIndex = 0;
					for (int i = 1; i < e.minions.Length; i++)
					{
						if (math.distance(e.minions.transforms[closestIndex].Position, hit.point) >
							math.distance(e.minions.transforms[i].Position, hit.point))
						{
							closestIndex = i;
						}
					}

					unitId = closestIndex;
				}
			}
		}
	}
}

[UpdateAfter(typeof(CommandSystem))]
public partial class EditorHelperSystem : SystemBase
{
	// 查询
	private EntityQuery minionQuery;
	private EntityQuery formationQuery;

	// 组件数据
	public class Minions
	{
		public NativeArray<UnitTransformData> transforms;
		public NativeArray<MinionTarget> targets;
		public NativeArray<RigidbodyData> rigidbodies;
		public NativeArray<TextureAnimatorData> animationData;
		public NativeArray<MinionData> data;
		public NativeArray<MinionPathData> pathsInfo;
		public NativeArray<float3> paths;
		public NativeArray<NavMeshLocationComponent> locationComponents;
		public NativeArray<MinionAttackData> attackData;
		public NativeArray<MinionBitmask> bitmask;
		public NativeArray<Entity> entities;

		public int Length => transforms.Length;

		public void Dispose()
		{
			if (transforms.IsCreated) transforms.Dispose();
			if (targets.IsCreated) targets.Dispose();
			if (rigidbodies.IsCreated) rigidbodies.Dispose();
			if (animationData.IsCreated) animationData.Dispose();
			if (data.IsCreated) data.Dispose();
			if (pathsInfo.IsCreated) pathsInfo.Dispose();
			if (paths.IsCreated) paths.Dispose();
			if (locationComponents.IsCreated) locationComponents.Dispose();
			if (attackData.IsCreated) attackData.Dispose();
			if (bitmask.IsCreated) bitmask.Dispose();
			if (entities.IsCreated) entities.Dispose();
		}
	}

	public class Formations
	{
		public NativeArray<Entity> entities;
		public NativeArray<FormationClosestData> closestFormations;
		public NativeArray<FormationData> data;
		public NativeArray<CrowdAgent> agents;
		public NativeArray<CrowdAgentNavigator> navigators;
		public NativeArray<FormationHighLevelPath> highLevelPaths;
		public NativeArray<FormationIntegrityData> integrityData;

		public int Length => entities.Length;

		public void Dispose()
		{
			if (entities.IsCreated) entities.Dispose();
			if (closestFormations.IsCreated) closestFormations.Dispose();
			if (data.IsCreated) data.Dispose();
			if (agents.IsCreated) agents.Dispose();
			if (navigators.IsCreated) navigators.Dispose();
			if (highLevelPaths.IsCreated) highLevelPaths.Dispose();
			if (integrityData.IsCreated) integrityData.Dispose();
		}
	}

	// 系统引用
	public FormationIntegritySystem integritySystem;
	public SpawnerSystem spawnerSystem;
	
	public static EditorHelperSystem I;

	public Minions minions = new Minions();
	public Formations formations = new Formations();

	public Queue<Action> work;

	protected override void OnCreate()
	{
		// 初始化查询
		minionQuery = GetEntityQuery(
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<MinionTarget>(),
			ComponentType.ReadOnly<RigidbodyData>(),
			ComponentType.ReadOnly<TextureAnimatorData>(),
			ComponentType.ReadOnly<MinionData>(),
			ComponentType.ReadOnly<MinionPathData>(),
			ComponentType.ReadOnly<NavMeshLocationComponent>(),
			ComponentType.ReadOnly<MinionAttackData>(),
			ComponentType.ReadOnly<MinionBitmask>()
		);

		formationQuery = GetEntityQuery(
			ComponentType.ReadOnly<FormationClosestData>(),
			ComponentType.ReadOnly<FormationData>(),
			ComponentType.ReadOnly<CrowdAgent>(),
			ComponentType.ReadOnly<CrowdAgentNavigator>(),
			ComponentType.ReadOnly<FormationHighLevelPath>(),
			ComponentType.ReadOnly<FormationIntegrityData>()
		);

		// 获取系统引用
		integritySystem = World.GetOrCreateSystemManaged<FormationIntegritySystem>();
		spawnerSystem = World.GetOrCreateSystemManaged<SpawnerSystem>();

		work = new Queue<Action>();
		I = this;
	}

	protected override void OnDestroy()
	{
		minions.Dispose();
		formations.Dispose();
	}

	bool completeDependencies = false;
	protected override void OnUpdate()
	{
		if (completeDependencies)
		{
			return;
		}

		// 更新小兵数据
		if (!minionQuery.IsEmpty)
		{
			minions.transforms = minionQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
			minions.targets = minionQuery.ToComponentDataArray<MinionTarget>(Allocator.TempJob);
			minions.rigidbodies = minionQuery.ToComponentDataArray<RigidbodyData>(Allocator.TempJob);
			minions.animationData = minionQuery.ToComponentDataArray<TextureAnimatorData>(Allocator.TempJob);
			minions.data = minionQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
			minions.pathsInfo = minionQuery.ToComponentDataArray<MinionPathData>(Allocator.TempJob);
			minions.locationComponents = minionQuery.ToComponentDataArray<NavMeshLocationComponent>(Allocator.TempJob);
			minions.attackData = minionQuery.ToComponentDataArray<MinionAttackData>(Allocator.TempJob);
			minions.bitmask = minionQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
			minions.entities = minionQuery.ToEntityArray(Allocator.TempJob);
			
			// 路径数据需要特殊处理
			// 注意：这里简化处理，实际上可能需要根据原始代码逻辑调整
			minions.paths = new NativeArray<float3>(minions.transforms.Length * 10, Allocator.TempJob);
		}

		// 更新编队数据
		if (!formationQuery.IsEmpty)
		{
			formations.entities = formationQuery.ToEntityArray(Allocator.TempJob);
			formations.closestFormations = formationQuery.ToComponentDataArray<FormationClosestData>(Allocator.TempJob);
			formations.data = formationQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
			formations.agents = formationQuery.ToComponentDataArray<CrowdAgent>(Allocator.TempJob);
			formations.navigators = formationQuery.ToComponentDataArray<CrowdAgentNavigator>(Allocator.TempJob);
			formations.highLevelPaths = formationQuery.ToComponentDataArray<FormationHighLevelPath>(Allocator.TempJob);
			formations.integrityData = formationQuery.ToComponentDataArray<FormationIntegrityData>(Allocator.TempJob);
		}

		while (work.Count > 0)
		{
			work.Dequeue().Invoke();
		}
	}

	public void CompleteDependency()
	{
		// 强制完成所有依赖
		completeDependencies = true;
		this.Update();
		completeDependencies = false;
	}
}
