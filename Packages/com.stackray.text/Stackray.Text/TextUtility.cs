﻿#if UNITY_2020_1_OR_NEWER
using VerticalAlignmentOptions = TMPro.VerticalAlignmentOptions;
using HorizontalAlignmentOptions = TMPro.HorizontalAlignmentOptions;
#else
using VerticalAlignmentOptions = TMPro._VerticalAlignmentOptions;
using HorizontalAlignmentOptions = TMPro._HorizontalAlignmentOptions;
#endif
using Stackray.Collections;
using Stackray.Transforms;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stackray.Text {
  public static class TextUtility {

    public static Entity CreateCanvas(EntityManager entityManager) {
      var canvas = entityManager.CreateEntity(
        typeof(CanvasRoot), 
        typeof(Vertex), 
        typeof(VertexIndex), 
        typeof(SubMeshInfo));
#if UNITY_EDITOR
      entityManager.SetName(canvas, "Canvas");
#endif
      var newMesh = new Mesh();
      newMesh.MarkDynamic();
      newMesh.indexFormat = IndexFormat.UInt32;
      entityManager.AddSharedComponentData(canvas, new TextRenderMesh {
        Mesh = newMesh
      });
      return canvas;
    }

    public static Entity CreateTextFontAsset(EntityManager entityManager, TMP_FontAsset font) {
      var entity = entityManager.CreateEntity();
#if UNITY_EDITOR
      entityManager.SetName(entity, font.name);
#endif
      entityManager.AddComponentData(entity, new TextFontAsset {
        LineHeight = font.faceInfo.lineHeight,
        AscentLine = font.faceInfo.ascentLine,
        Baseline = font.faceInfo.baseline,
        BoldSpace = font.boldSpacing,
        CapLine = font.faceInfo.capLine,
        DescentLine = font.faceInfo.descentLine,
        MeanLine = font.faceInfo.meanLine,
        NormalSpace = font.normalSpacingOffset,
        PointSize = font.faceInfo.pointSize,
        BoldStyle = font.boldStyle,
        NormalStyle = font.normalStyle,
        AtlasSize = new float2(font.atlasWidth, font.atlasHeight),
      });

      var fontGlyphBuffer = entityManager.AddBuffer<FontGlyph>(entity);
      var max = 0;
      foreach (var glyph in font.characterLookupTable)
        max = math.max((int)glyph.Key + 1, max);

      fontGlyphBuffer.Capacity = max;
      fontGlyphBuffer.ResizeUninitialized(max);
      foreach (var glyph in font.characterLookupTable)
        fontGlyphBuffer[(int)glyph.Key] = new FontGlyph() {
          Character = (ushort)glyph.Key,
          Scale = glyph.Value.scale,
          Rect = glyph.Value.glyph.glyphRect,
          Metrics = glyph.Value.glyph.metrics
        };
      return entity;
    }

    public static bool GetGlyph(ushort character, DynamicBuffer<FontGlyph> glyphData, out FontGlyph glyph) {
      glyph = glyphData[character];
      return true;
    }

    struct CurrentLineData {
      public float LineWidth;
      public float WordWidth;
      public int LineWordIndex;
      public int WordCharacterCount;
      public int CharacterOffset;
    }

    public static void CalculateLines(
      LocalRectTransform renderBounds,
      float2 canvasScale,
      float styleSpaceMultiplier,
      DynamicBuffer<FontGlyph> glyphData,
      TextData textData,
      DynamicBuffer<TextLine> ret) {

      var maxLineWidth = renderBounds.Value.Size.x;  //rect.Max.x - rect.Min.x;
      CurrentLineData currentLine = default;
      for (int i = 0; i < textData.Value.LengthInBytes; i++) {
        var character = textData.Value.GetChar(i);
        if (character == '\n') {
          ret.Add(new TextLine {
            CharacterOffset = currentLine.CharacterOffset,
            LineWidth = currentLine.LineWidth,
          });
          currentLine.CharacterOffset = i + 1;
          currentLine.LineWidth = 0.0f;
          currentLine.LineWordIndex = 0;
          currentLine.WordCharacterCount = 0;
          currentLine.WordWidth = 0.0f;
          continue;
        }
        if (character == ' ') {
          currentLine.LineWordIndex++;
          currentLine.WordCharacterCount = -1;
          currentLine.WordWidth = 0.0f;
        }
        if (GetGlyph(character, glyphData, out var ch)) {
          if ((ch.Metrics.width * styleSpaceMultiplier * canvasScale.x) < renderBounds.Value.Size.x) {
            currentLine.WordCharacterCount++;
            float characterWidth = ch.Metrics.horizontalAdvance * styleSpaceMultiplier *
                                   canvasScale.x;
            currentLine.LineWidth += characterWidth;
            currentLine.WordWidth += characterWidth;

            if (currentLine.LineWidth > maxLineWidth) {
              if (currentLine.LineWordIndex != 0) {
                ret.Add(new TextLine {
                  CharacterOffset = currentLine.CharacterOffset,
                  LineWidth = currentLine.LineWidth - currentLine.WordWidth,
                });
                currentLine.CharacterOffset = i - currentLine.WordCharacterCount + 1;
                currentLine.LineWidth = 0.0f;
                currentLine.WordWidth = 0.0f;
                i = i - currentLine.WordCharacterCount + 1;
                currentLine.LineWordIndex = 0;
                currentLine.WordCharacterCount = 0;
              } else {
                ret.Add(new TextLine {
                  CharacterOffset = currentLine.CharacterOffset,
                  LineWidth = currentLine.LineWidth,
                });
                currentLine.CharacterOffset = i;
                currentLine.LineWidth = 0.0f;
                currentLine.WordWidth = 0.0f;
                currentLine.LineWordIndex = 0;
                currentLine.WordCharacterCount = 0;
              }
            }
            continue;
          }
          ret.Add(new TextLine {
            CharacterOffset = currentLine.CharacterOffset,
            LineWidth = currentLine.LineWidth,
          });
          currentLine.CharacterOffset = i;
          currentLine.LineWidth = 0.0f;
          currentLine.WordWidth = 0.0f;
          currentLine.LineWordIndex = 0;
          currentLine.WordCharacterCount = 0;
        }
      }
      ret.Add(new TextLine {
        CharacterOffset = currentLine.CharacterOffset,
        LineWidth = currentLine.LineWidth
      });
    }

    public static float GetAlignedLinePosition(LocalRectTransform renderBounds, float lineWidth, HorizontalAlignmentOptions horizontalAlignment) {
      var min = renderBounds.Value.Center - renderBounds.Value.Extents;
      if ((horizontalAlignment & HorizontalAlignmentOptions.Right) == HorizontalAlignmentOptions.Right)
        return min.x + renderBounds.Value.Size.x - lineWidth;
      if ((horizontalAlignment & HorizontalAlignmentOptions.Center) == HorizontalAlignmentOptions.Center)
        return min.x + renderBounds.Value.Size.x * 0.5f - lineWidth * 0.5f;
      return min.x;
    }

    public static float2 GetAlignedStartPosition(LocalRectTransform renderBounds, TextRenderer textRenderer, TextFontAsset font, float textBlockHeight, float2 scale) {
      var min = renderBounds.Value.Center - renderBounds.Value.Extents;
      var max = renderBounds.Value.Center + renderBounds.Value.Extents;
      float startY = 0.0f;
      var test = min.y + (font.LineHeight - font.AscentLine) * scale.y;
      var vertical = (VerticalAlignmentOptions)textRenderer.Alignment;
      var horizontal = (HorizontalAlignmentOptions)textRenderer.Alignment;
      if ((vertical & VerticalAlignmentOptions.Bottom) == VerticalAlignmentOptions.Bottom)
        startY = min.y - font.DescentLine * scale.y + textBlockHeight - font.LineHeight * scale.y;
      else if ((vertical & VerticalAlignmentOptions.Middle) == VerticalAlignmentOptions.Middle)
        startY = textBlockHeight * 0.5f - font.AscentLine * scale.y;
      else if ((vertical & VerticalAlignmentOptions.Top) == VerticalAlignmentOptions.Top)
        startY = max.y - font.AscentLine * scale.y;
      return new float2(min.x, startY);
    }

    public static float2 GetSize(TextData textData, DynamicBuffer<FontGlyph> glyphData, float stylePadding, float styleSpaceMultiplier, float2 canvasScale) {
      float2 size = default;
      for (int i = 0; i < textData.Value.LengthInBytes; i++) {
        var character = textData.Value.GetChar(i);
        if (GetGlyph(character, glyphData, out FontGlyph ch)) {
          size += new float2(ch.Metrics.horizontalBearingX - stylePadding, ch.Metrics.horizontalBearingY - ch.Metrics.height - stylePadding) * canvasScale;
          size += new float2(ch.Metrics.width + stylePadding * 2.0f, ch.Metrics.height + stylePadding * 2.0f) * canvasScale;
          size += new float2(ch.Metrics.horizontalAdvance * styleSpaceMultiplier, 0.0f) * canvasScale;
        }
      }
      return size;
    }
  }
}