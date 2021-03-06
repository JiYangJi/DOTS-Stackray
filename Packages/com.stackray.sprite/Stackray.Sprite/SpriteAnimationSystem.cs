﻿using Stackray.Entities;
using Stackray.Renderer;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Stackray.Sprite {

  public class SpriteAnimationSystem : SystemBase {
    EntityQuery m_query;
    int m_lastOrderInfo;
    List<SpriteAnimation> m_spriteAnimations = new List<SpriteAnimation>();
    List<ISpritePropertyAnimator> m_spriteAnimators;

    protected override void OnCreate() {
      base.OnCreate();
      var availableComponentTypes = TypeUtility.GetTypes(typeof(IDynamicBufferProperty<>));
      m_query = GetEntityQuery(new EntityQueryDesc {
        All = new ComponentType[] {
            ComponentType.ReadOnly<SpriteAnimation>(),
            ComponentType.ReadOnly<SpriteAnimationTimeSpeedState>()
        },
        Any = availableComponentTypes.Select(t => (ComponentType)t).ToArray()
      });

      m_spriteAnimators = SpritePropertyAnimatorUtility.CreatePossibleInstances(this, m_query)
        .ToList();
    }

    void UpdateStates(float deltaTime) {
      Entities
        .ForEach((ref SpriteAnimationTimeSpeedState state, ref SpriteAnimationPlayingState playingState) => {
          state.Time += math.mul(deltaTime, state.Speed);
          playingState.Value = false;
        }).ScheduleParallel();
    }

    protected override void OnUpdate() {
      if (m_lastOrderInfo != m_query.GetCombinedComponentOrderVersion()) {
        m_lastOrderInfo = m_query.GetCombinedComponentOrderVersion();
        m_spriteAnimations.Clear();
        var animations = new List<SpriteAnimation>();
        EntityManager.GetAllUniqueSharedComponentData(animations);
        foreach (var animation in animations) {
          m_query.SetSharedComponentFilter(animation);
          var length = m_query.CalculateEntityCount();
          if (length > 0 && animation.ClipSetEntity != Entity.Null)
            m_spriteAnimations.Add(animation);
        }
      }

      m_query.ResetFilter();
      EntityManager.AddComponent(m_query, typeof(SpriteAnimationPlayingState));
      UpdateStates(Time.DeltaTime);

      foreach (var spriteAnimation in m_spriteAnimations) {
        m_query.SetSharedComponentFilter(spriteAnimation);
        foreach (var spriteAnimator in m_spriteAnimators)
          Dependency = JobHandle.CombineDependencies(
            Dependency,
            spriteAnimator.Update(spriteAnimation, Dependency));
      }
    }
  }
}