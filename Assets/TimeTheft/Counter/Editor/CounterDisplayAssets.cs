using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace TimeTheft.CounterStudy.Editor
{
    // Python 작성기가 출력한 비트맵과 메트릭을 TMP 자산에 반영한다. 동적 문자열은 굽지 않는다.
    public static class CounterDisplayAssets
    {
        private const string Root = "Assets/TimeTheft/Counter";
        [Serializable] private sealed class FontSpec
        {
            public int pointSize, ascent, descent, cap;
            public GlyphSpec[] glyphs;
        }
        [Serializable] private sealed class GlyphSpec
        {
            public int unicode, x, y, width, height, bearingX, bearingY, advance;
        }

        [MenuItem("TimeTheft/Counter/Import bitmap display sources")]
        public static void Import()
        {
            if (Application.isPlaying) throw new InvalidOperationException("편집 모드에서 실행합니다.");
            foreach (string id in new[] { "Galmuri7Native", "CounterPixelSegments" }) ImportFont(id);
            AssetDatabase.SaveAssets();
        }

        private static void ImportFont(string id)
        {
            string path = Root + "/Fonts/" + id;
            AssetDatabase.ImportAsset(path + "_Glyphs.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path + "_Glyphs.png");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 128;
            importer.SaveAndReimport();
            var spec = JsonUtility.FromJson<FontSpec>(File.ReadAllText(path + ".json"));
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path + ".asset");
            bool create = !font;
            if (create)
            {
                var init = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
                font = TMP_FontAsset.CreateFontAsset(init, spec.pointSize, 2, GlyphRenderMode.RASTER, 128, 128, AtlasPopulationMode.Static, false);
                font.name = "Counter Study " + id;
            }
            var face = font.faceInfo;
            face.familyName = "TimeTheft Study Bitmap";
            face.styleName = "Native Pixels";
            face.pointSize = spec.pointSize;
            face.scale = 1;
            face.ascentLine = spec.ascent;
            face.descentLine = spec.descent;
            face.capLine = face.meanLine = spec.cap;
            face.lineHeight = spec.ascent - spec.descent;
            face.baseline = 0;
            font.faceInfo = face;
            font.glyphTable.Clear();
            font.characterTable.Clear();
            uint index = 1;
            foreach (var g in spec.glyphs)
            {
                var glyph = new Glyph(index++, new GlyphMetrics(g.width, g.height, g.bearingX, g.bearingY, g.advance), new GlyphRect(g.x, g.y, g.width, g.height), 1, 0);
                font.glyphTable.Add(glyph);
                font.characterTable.Add(new TMP_Character((uint)g.unicode, font, glyph));
            }
            font.atlasTextures = new[] { AssetDatabase.LoadAssetAtPath<Texture2D>(path + "_Glyphs.png") };
            if (create)
            {
                font.material = new Material(Shader.Find("TimeTheft/Counter Pixel Glyph")) { name = id + " Point Bitmap" };
                AssetDatabase.CreateAsset(font, path + ".asset");
                AssetDatabase.AddObjectToAsset(font.material, font);
            }
            font.material.SetTexture("_MainTex", font.atlasTextures[0]);
            font.material.SetColor("_Color", Color.white);
            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_SourceFontFile").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            font.ReadFontAssetDefinition();
            EditorUtility.SetDirty(font);
            EditorUtility.SetDirty(font.material);
        }
    }
}
