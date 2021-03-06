﻿using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stackray.Text {

  public struct MeshGroup : IDisposable {
    public Mesh Mesh;
    public NativeArray<SubMeshInfo> SubmeshInfo;

    public void Dispose() {
      SubmeshInfo.Dispose();
    }
  }

  class CameraRenderMesh : MonoBehaviour {
    Queue<MeshGroup> m_meshGroups = new Queue<MeshGroup>();
    EntityManager m_entityManager;

    public void Enqueue(MeshGroup value) {
      m_meshGroups.Enqueue(value);
    }

    void TouchCamera() {
      var camera = GetComponent<Camera>();
      camera.enabled = !camera.enabled;
      camera.enabled = !camera.enabled;
    }

    void Start() {
      m_entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
      // Required only for the Default render pipeline
      // Otherwise DrawMeshNow will not work (Bug?)
      Invoke(nameof(TouchCamera), 0.1f);
    }

    void OnEnable() {
      RenderPipelineManager.endFrameRendering += OnRendered;
    }

    void OnDisable() {
      RenderPipelineManager.endFrameRendering -= OnRendered;
    }

    private void OnRendered(ScriptableRenderContext arg1, Camera[] arg2) {
      OnPostRender();
    }

    private void OnPostRender() {
      while (m_meshGroups.Count > 0) {
        using (var group = m_meshGroups.Dequeue()) {
          for (var i = 0; i < group.SubmeshInfo.Length; ++i) {
            var material = GetMaterial(group.SubmeshInfo[i].MaterialId);
            material.SetPass(0);
            Graphics.DrawMeshNow(group.Mesh, Matrix4x4.identity, i);
          }
        }
      }
    }

    private void Update() {
      // Dispose unprocessed groups to avoid memory leaks
      while (m_meshGroups.Count > 0)
        m_meshGroups.Dequeue().Dispose();
    }

    Material GetMaterial(int id) {
      return m_entityManager.GetSharedComponentData<FontMaterial>(id).Value;
    }
  }

  [UpdateInGroup(typeof(PresentationSystemGroup))]
  class TextMeshRenderSystem : ComponentSystem {

    EntityQuery m_renderQuery;
    int m_lastOrderInfo;
    List<TextRenderMesh> m_textMeshes = new List<TextRenderMesh>();
    CameraRenderMesh m_cameraRenderMesh;

    protected override void OnCreate() {
      base.OnCreate();
      m_renderQuery = GetEntityQuery(
        ComponentType.ReadOnly<TextRenderMesh>(),
        ComponentType.ReadOnly<SubMeshInfo>());
    }

    protected override void OnStartRunning() {
      base.OnStartRunning();
      m_cameraRenderMesh = Camera.main.gameObject.AddComponent<CameraRenderMesh>();
    }

    protected override void OnStopRunning() {
      base.OnStopRunning();
      UnityEngine.Object.Destroy(m_cameraRenderMesh);
    }

    protected override void OnUpdate() {
      if (m_lastOrderInfo != m_renderQuery.GetCombinedComponentOrderVersion()) {
        m_lastOrderInfo = m_renderQuery.GetCombinedComponentOrderVersion();
        m_textMeshes.Clear();
        EntityManager.GetAllUniqueSharedComponentData(m_textMeshes);
        m_textMeshes.Remove(default);
        return;
      }
      foreach (var textMesh in m_textMeshes) {
        m_renderQuery.SetSharedComponentFilter(textMesh);
        using (var chunkArray = m_renderQuery.CreateArchetypeChunkArray(Allocator.TempJob)) {
          var subMeshBufferType = GetArchetypeChunkBufferType<SubMeshInfo>();
          if (chunkArray.Length != 1)
            throw new ArgumentException($"Excepted only 1 chunk with {nameof(SubMeshInfo)}, found {chunkArray.Length} chunks");
          var chunk = chunkArray[0];
          var subMeshBufferAccessor = chunk.GetBufferAccessor(subMeshBufferType);
          if (subMeshBufferAccessor.Length != 1)
            throw new ArgumentException($"Excepted only 1 submesh buffer accessor with, found {subMeshBufferAccessor.Length} accessors");
          m_cameraRenderMesh.Enqueue(new MeshGroup {
            Mesh = textMesh.Mesh,
            SubmeshInfo = new NativeArray<SubMeshInfo>(subMeshBufferAccessor[0].AsNativeArray(), Allocator.TempJob)
          });
        }
      }
    }

    Material GetMaterial(int id) {
      return EntityManager.GetSharedComponentData<FontMaterial>(id).Value;
    }
  }
}
