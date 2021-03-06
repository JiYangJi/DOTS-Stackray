﻿using Stackray.Mathematics;
using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Stackray.Text {
  [UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
  public class TextConversionSystem : GameObjectConversionSystem {

    static Dictionary<TMP_FontAsset, Entity> m_textFontAssets = new Dictionary<TMP_FontAsset, Entity>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init() {
      m_textFontAssets.Clear();
    }

    protected override void OnStartRunning() {
      base.OnStartRunning();
      // We need to override the default TextMesh conversion system
      World.GetOrCreateSystem<TextMeshConversionSystem>().Enabled = false;
    }

    protected override void OnUpdate() {
      Entities.ForEach((TextMeshPro textMesh, MeshFilter meshFilter) => {
        // We must disable the text mesh for it to be skipped by MeshRenderer conversion system
        meshFilter.mesh = null;
        var font = textMesh.font;
        var entity = GetPrimaryEntity(textMesh);
        if (!m_textFontAssets.TryGetValue(font, out var fontEntity)) {
          fontEntity = TextUtility.CreateTextFontAsset(DstEntityManager, font);
          m_textFontAssets.Add(font, fontEntity);
        }

        DstEntityManager.AddSharedComponentData(entity, new FontMaterial {
          Value = font.material
        });
        var materialId = DstEntityManager.GetSharedComponentDataIndex<FontMaterial>(entity);

        DstEntityManager.AddComponentData(entity, new TextRenderer() {
          Font = fontEntity,
          MaterialId = materialId,
          Size = textMesh.fontSize,
          Alignment = textMesh.alignment,
          Bold = (textMesh.fontStyle & FontStyles.Bold) == FontStyles.Bold,
          Italic = (textMesh.fontStyle & FontStyles.Italic) == FontStyles.Italic
        });
        DstEntityManager.AddComponentData(entity, new TextData {
          Value = textMesh.text
        });
        DstEntityManager.AddComponentData(entity, new VertexColor() {
          Value = textMesh.color.ToFloat4()
        });
        DstEntityManager.AddComponentData(entity, new VertexColorMultiplier() {
          Value = new float4(1.0f, 1.0f, 1.0f, 1.0f)
        });
        DstEntityManager.AddBuffer<Vertex>(entity);
        DstEntityManager.AddBuffer<VertexIndex>(entity);
        DstEntityManager.AddBuffer<TextLine>(entity);
        if (!DstEntityManager.HasComponent<RenderBounds>(entity))
          // RenderBounds will be calculated on TextMeshBuildSystem
          DstEntityManager.AddComponentData(entity, default(RenderBounds));
      });
    }
  }
}
