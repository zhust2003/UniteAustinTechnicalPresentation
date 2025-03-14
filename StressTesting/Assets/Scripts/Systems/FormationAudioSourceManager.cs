using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;

[UpdateAfter(typeof(UnitLifecycleManager))]
public partial class FormationAudioSourceManager : SystemBase
{
	private class AudioSourceFormation
	{
		public AudioSource AudioSource;
		public Entity FormationEntity;
	}

	private struct FormationDistance
	{
		public float Dist;
		public Entity FormationEntity;
	}

	private EntityQuery formationsQuery;
	private ComponentLookup<FormationIntegrityData> formationIntegrityDataLookup;
	private ComponentLookup<FormationData> formationDataLookup;

	private const int AudioSourcePoolSize = 6;

	private bool isInitialized;
	private bool isFormationSoundEnabled;

	private List<AudioSourceFormation> audioSources;
	private readonly List<AudioSourceFormation> usedAudioSources = new List<AudioSourceFormation>();
	private float maxDist;

	private readonly List<FormationDistance> closestFormations = new List<FormationDistance>();

	private const string IntroEndedEvent = "INTRO_ENDED";
	private const string OutroStartedEvent = "OUTRO_STARTED";
	private const string OutroEndedEvent = "OUTRO_ENDED";

	protected override void OnCreate()
	{
		formationsQuery = GetEntityQuery(
			ComponentType.ReadOnly<FormationData>()
		);
		
		formationIntegrityDataLookup = GetComponentLookup<FormationIntegrityData>(true);
		formationDataLookup = GetComponentLookup<FormationData>(true);
	}

	private void EnableFormationSounds(string eventName)
	{
		isFormationSoundEnabled = true;
	}

	private void DisableFormationSounds(string eventName)
	{
		isFormationSoundEnabled = false;
		for (var i = usedAudioSources.Count - 1; i >= 0; i--)
		{
			var data = usedAudioSources[i];
			data.AudioSource.Stop();
			usedAudioSources.RemoveAt(i);
			audioSources.Add(data);
		}
	}

	protected override void OnUpdate()
	{
		if (!isInitialized)
		{
			isInitialized = true;
			AudioSystem.SubscribeOnce(IntroEndedEvent, EnableFormationSounds);
			AudioSystem.SubscribeOnce(OutroStartedEvent, DisableFormationSounds);
			AudioSystem.SubscribeOnce(OutroEndedEvent, EnableFormationSounds);
		}

		if (!isFormationSoundEnabled || formationsQuery.IsEmpty) return;

		if (audioSources == null)
		{
			audioSources = new List<AudioSourceFormation>();
			for (var i = 0; i < AudioSourcePoolSize; i++)
			{
				var go = UnityEngine.Object.Instantiate(SimulationSettings.Instance.FormationAudioSource);
				audioSources.Add(new AudioSourceFormation() {AudioSource = go.GetComponent<AudioSource>(), FormationEntity = new Entity()});
			}
			maxDist = audioSources[0].AudioSource.maxDistance;
			maxDist *= maxDist;
		}

		var curCamera = Camera.main;
		if (curCamera == null) return;
		var curCameraPos = curCamera.transform.position;

		// 更新组件查询
		formationIntegrityDataLookup.Update(this);
		formationDataLookup.Update(this);
		
		// 获取组件数据
		var formationsArray = formationsQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
		var entitiesArray = formationsQuery.ToEntityArray(Allocator.TempJob);

		closestFormations.Clear();
		for (var i = 0; i < formationsArray.Length; i++)
		{
			var formation = formationsArray[i];
			if (formation.FormationState == FormationData.State.AllDead) continue;
			var dist = (curCameraPos - (Vector3)formation.Position).sqrMagnitude;
			if (dist < maxDist) closestFormations.Add(new FormationDistance() {Dist = dist, FormationEntity = entitiesArray[i] });
		}

		closestFormations.Sort((fd1, fd2) => (fd1.Dist < fd2.Dist) ? -1 : ((fd1.Dist > fd2.Dist) ? 1 : 0));
		
		for (var i = usedAudioSources.Count - 1; i >= 0; i--)
		{
			var index = -1;
			for (var n = 0; (n < closestFormations.Count) && (n < AudioSourcePoolSize); n++)
			{
				if (closestFormations[n].FormationEntity == usedAudioSources[i].FormationEntity)
				{
					index = n;
					break;
				}
			}
			if (index < 0 || index >= AudioSourcePoolSize)
			{
				var data = usedAudioSources[i];
				data.AudioSource.Stop();
				audioSources.Add(data);
				usedAudioSources.RemoveAt(i);
			}
		}
		
		for (var i = 0; (i < closestFormations.Count) && (i < AudioSourcePoolSize); i++)
		{
			AudioSourceFormation found = null;
			var formationEntity = closestFormations[i].FormationEntity;
			for (var n = 0; n < usedAudioSources.Count; n++)
			{
				if (usedAudioSources[n].FormationEntity == formationEntity)
				{
					found = usedAudioSources[n];
					break;
				}
			}
			bool doPlay = false;
			if (found == null)
			{
				found = audioSources[0];
				audioSources.RemoveAt(0);
				found.FormationEntity = formationEntity;
				usedAudioSources.Add(found);
				doPlay = true;
			}

			if(formationDataLookup.HasComponent(found.FormationEntity) && formationIntegrityDataLookup.HasComponent(found.FormationEntity))
			{
				var formation = formationDataLookup[found.FormationEntity];
				var formationIntegrityData = formationIntegrityDataLookup[found.FormationEntity];
				var percent = (float)formationIntegrityData.unitsAttacking / formation.UnitCount;
				var isAttacking = percent > 0.1f;
				
				var clip = GetClipForUnitTypeAndState((UnitType)formation.UnitType, isAttacking);
				if (found.AudioSource.clip != clip) found.AudioSource.clip = clip;

				found.AudioSource.transform.position = formationDataLookup[formationEntity].Position;
				if (doPlay) found.AudioSource.Play();
			}
		}
		
		// 清理临时分配的数组
		formationsArray.Dispose();
		entitiesArray.Dispose();
	}

	private static AudioClip GetClipForUnitTypeAndState(UnitType unitType, bool isAttacking)
	{
		var fclips = SimulationSettings.Instance.FormationClips;
		for (var i = 0; i < fclips.Count; i++)
		{
			if (fclips[i].UnitType == unitType)
			{
				if (isAttacking) return fclips[i].FightingClip;
				return fclips[i].MovingClip;
			}
		}
		return null;
	}
}