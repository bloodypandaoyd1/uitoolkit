using System;
using System.Collections.Generic;
using PsdTools.Layers;
using UnityEngine;

namespace PsdTools.UIToolKit
{
    internal static class PsdUiToolkitTextEffectsHelper
    {
        public struct GradientColorStop
        {
            public float position; // 0-1
            public byte r, g, b;
        }

        public static bool TryGetStrokeEffect(Layer layer, out Color strokeColor, out float strokeSize)
        {
            strokeColor = Color.black;
            strokeSize = 1f;
            var blocks = layer.TaggedBlocks;
            if (blocks == null) return false;

            byte[] data = blocks.GetData(PsdTools.Constants.Tag.OBJECT_BASED_EFFECTS_LAYER1);
            if (data == null)
                data = blocks.GetData(PsdTools.Constants.Tag.OBJECT_BASED_EFFECTS_LAYER2);
            if (data == null) return false;

            int frfxPos = FindPattern(data, "FrFX");
            if (frfxPos < 0) return false;

            int nextEffect = data.Length;
            string[] effectKeys = { "DrSh", "IrSh", "OrGl", "IrGl", "ebbl", "SoFi", "patternFill", "GrFl" };
            foreach (var key in effectKeys)
            {
                int pos = FindPattern(data, key, frfxPos + 4);
                if (pos > frfxPos && pos < nextEffect)
                    nextEffect = pos;
            }

            int enabPos = FindPattern(data, "enab", frfxPos);
            if (enabPos >= 0 && enabPos < frfxPos + 200 && enabPos < nextEffect)
            {
                int boolPos = FindPattern(data, "bool", enabPos + 4);
                if (boolPos >= 0 && boolPos < enabPos + 20 && boolPos + 5 <= data.Length)
                {
                    if (data[boolPos + 4] == 0)
                        return false;
                }
            }

            // Stroke size
            int szPos = FindPattern(data, "Sz  ", frfxPos);
            if (szPos >= 0 && szPos < nextEffect)
            {
                int untfPos = FindPattern(data, "UntF", szPos + 4);
                if (untfPos >= 0 && untfPos < szPos + 20)
                {
                    if (untfPos + 16 <= data.Length)
                    {
                        double sz = ReadBigEndianDouble(data, untfPos + 8);
                        strokeSize = Mathf.Max(0f, (float)sz);
                    }
                }
            }

            // Stroke color
            int clrPos = FindPattern(data, "Clr ", frfxPos);
            if (clrPos >= 0 && clrPos < nextEffect)
            {
                if (TryReadRGBFromRawBytes(data, clrPos, out byte r, out byte g, out byte b))
                {
                    float opacity = 1f;
                    int opctPos = FindPattern(data, "Opct", frfxPos);
                    if (opctPos >= 0 && opctPos < clrPos && opctPos < nextEffect)
                    {
                        int untfPos = FindPattern(data, "UntF", opctPos + 4);
                        if (untfPos >= 0 && untfPos < opctPos + 20)
                        {
                            int prcPos = FindPattern(data, "#Prc", untfPos + 4);
                            if (prcPos >= 0 && prcPos < untfPos + 12 && prcPos + 12 <= data.Length)
                            {
                                double pct = ReadBigEndianDouble(data, prcPos + 4);
                                opacity = Mathf.Clamp01((float)pct / 100f);
                            }
                        }
                    }
                    strokeColor = new Color(r / 255f, g / 255f, b / 255f, opacity);
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetTextGradientCornersFromLayer(Layer layer, out Color32 topLeft, out Color32 topRight, out Color32 bottomLeft, out Color32 bottomRight)
        {
            topLeft = topRight = bottomLeft = bottomRight = default;
            if (!TryGetGradientOverlay(layer, out List<GradientColorStop> stops, out float angleDeg, out float gradOpacity))
                return false;

            float combinedOpacity = Mathf.Clamp01(gradOpacity * layer.OpacityFloat);
            byte alphaByte = (byte)Mathf.RoundToInt(combinedOpacity * 255f);

            float rad = angleDeg * Mathf.Deg2Rad;
            float dx = Mathf.Cos(rad);
            float dy = Mathf.Sin(rad);

            float t_tl = (-0.5f) * dx + (0.5f) * dy + 0.5f;
            float t_tr = (0.5f) * dx + (0.5f) * dy + 0.5f;
            float t_bl = (-0.5f) * dx + (-0.5f) * dy + 0.5f;
            float t_br = (0.5f) * dx + (-0.5f) * dy + 0.5f;

            topLeft = SampleGradient(stops, t_tl);
            topLeft.a = alphaByte;
            topRight = SampleGradient(stops, t_tr);
            topRight.a = alphaByte;
            bottomLeft = SampleGradient(stops, t_bl);
            bottomLeft.a = alphaByte;
            bottomRight = SampleGradient(stops, t_br);
            bottomRight.a = alphaByte;

            return true;
        }

        private static bool TryGetGradientOverlay(Layer layer, out List<GradientColorStop> stops, out float angle, out float opacity)
        {
            stops = null;
            angle = 90f;
            opacity = 1f;
            var blocks = layer.TaggedBlocks;
            if (blocks == null) return false;

            byte[] data = blocks.GetData(PsdTools.Constants.Tag.OBJECT_BASED_EFFECTS_LAYER1);
            if (data == null)
                data = blocks.GetData(PsdTools.Constants.Tag.OBJECT_BASED_EFFECTS_LAYER2);
            if (data == null) return false;

            int grflPos = FindPattern(data, "GrFl");
            if (grflPos < 0) return false;

            int enabPos = FindPattern(data, "enab", grflPos);
            if (enabPos >= 0 && enabPos < grflPos + 200)
            {
                int boolPos = FindPattern(data, "bool", enabPos + 4);
                if (boolPos >= 0 && boolPos < enabPos + 20 && boolPos + 5 <= data.Length)
                {
                    if (data[boolPos + 4] == 0)
                        return false;
                }
            }

            int opctPos = FindPattern(data, "Opct", grflPos);
            if (opctPos >= 0 && opctPos < grflPos + 300)
            {
                int untfPos = FindPattern(data, "UntF", opctPos + 4);
                if (untfPos >= 0 && untfPos < opctPos + 20)
                {
                    int prcPos = FindPattern(data, "#Prc", untfPos + 4);
                    if (prcPos >= 0 && prcPos < untfPos + 12 && prcPos + 12 <= data.Length)
                    {
                        double pct = ReadBigEndianDouble(data, prcPos + 4);
                        opacity = Mathf.Clamp01((float)pct / 100f);
                    }
                }
            }

            int anglPos = FindPattern(data, "Angl", grflPos);
            if (anglPos >= 0 && anglPos < grflPos + 500)
            {
                int untfPos = FindPattern(data, "UntF", anglPos + 4);
                if (untfPos >= 0 && untfPos < anglPos + 20 && untfPos + 16 <= data.Length)
                {
                    double a = ReadBigEndianDouble(data, untfPos + 8);
                    angle = (float)a;
                }
            }

            int clrsPos = FindPattern(data, "Clrs", grflPos);
            if (clrsPos < 0) return false;

            int vlLsPos = FindPattern(data, "VlLs", clrsPos);
            if (vlLsPos < 0 || vlLsPos > clrsPos + 20) return false;

            int stopCount = ReadBigEndianInt32(data, vlLsPos + 4);
            if (stopCount < 2 || stopCount > 100) return false;

            int trnsPos = FindPattern(data, "Trns", vlLsPos);
            int colorRegionEnd = trnsPos > 0 ? trnsPos : data.Length;

            stops = new List<GradientColorStop>();
            int searchFrom = vlLsPos + 8;

            for (int i = 0; i < stopCount; i++)
            {
                int clrPos = FindPattern(data, "Clr ", searchFrom);
                if (clrPos < 0 || clrPos >= colorRegionEnd) break;

                int lctnPos = FindPattern(data, "Lctn", clrPos);
                if (lctnPos < 0 || lctnPos >= colorRegionEnd) break;

                if (!TryReadRGBFromRawBytes(data, clrPos, out byte r, out byte g, out byte b))
                    break;

                int longPos = FindPattern(data, "long", lctnPos + 4);
                if (longPos < 0 || longPos > lctnPos + 20 || longPos + 8 > data.Length)
                    break;

                int lctnValue = ReadBigEndianInt32(data, longPos + 4);
                float pos = Mathf.Clamp01(lctnValue / 4096f);

                stops.Add(new GradientColorStop { position = pos, r = r, g = g, b = b });
                searchFrom = longPos + 8;
            }

            if (stops.Count < 2) { stops = null; return false; }
            stops.Sort((a, b) => a.position.CompareTo(b.position));
            return true;
        }

        private static Color32 SampleGradient(List<GradientColorStop> stops, float t)
        {
            t = Mathf.Clamp01(t);

            if (t <= stops[0].position)
                return new Color32(stops[0].r, stops[0].g, stops[0].b, 255);
            if (t >= stops[stops.Count - 1].position)
            {
                var last = stops[stops.Count - 1];
                return new Color32(last.r, last.g, last.b, 255);
            }

            for (int i = 0; i < stops.Count - 1; i++)
            {
                if (t >= stops[i].position && t <= stops[i + 1].position)
                {
                    float range = stops[i + 1].position - stops[i].position;
                    float lerp = range > 0.0001f ? (t - stops[i].position) / range : 0f;
                    byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(stops[i].r, stops[i + 1].r, lerp));
                    byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(stops[i].g, stops[i + 1].g, lerp));
                    byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(stops[i].b, stops[i + 1].b, lerp));
                    return new Color32(r, g, b, 255);
                }
            }

            var fallback = stops[stops.Count - 1];
            return new Color32(fallback.r, fallback.g, fallback.b, 255);
        }

        private static int FindPattern(byte[] data, string pattern, int startIndex = 0)
        {
            byte[] patternBytes = System.Text.Encoding.ASCII.GetBytes(pattern);
            for (int i = startIndex; i <= data.Length - patternBytes.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < patternBytes.Length; j++)
                {
                    if (data[i + j] != patternBytes[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static double ReadBigEndianDouble(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return 0;
            byte[] temp = new byte[8];
            System.Array.Copy(data, offset, temp, 0, 8);
            if (System.BitConverter.IsLittleEndian)
                System.Array.Reverse(temp);
            return System.BitConverter.ToDouble(temp, 0);
        }

        private static int ReadBigEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private static bool TryReadRGBFromRawBytes(byte[] data, int searchStart, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            int rdPos = FindPattern(data, "Rd  ", searchStart);
            if (rdPos < 0) return false;
            int rdDoub = FindPattern(data, "doub", rdPos + 4);
            if (rdDoub < 0 || rdDoub > rdPos + 20) return false;
            double rdVal = ReadBigEndianDouble(data, rdDoub + 4);

            int grnPos = FindPattern(data, "Grn ", rdDoub);
            if (grnPos < 0) return false;
            int grnDoub = FindPattern(data, "doub", grnPos + 4);
            if (grnDoub < 0 || grnDoub > grnPos + 20) return false;
            double grnVal = ReadBigEndianDouble(data, grnDoub + 4);

            int blPos = FindPattern(data, "Bl  ", grnDoub);
            if (blPos < 0) return false;
            int blDoub = FindPattern(data, "doub", blPos + 4);
            if (blDoub < 0 || blDoub > blPos + 20) return false;
            double blVal = ReadBigEndianDouble(data, blDoub + 4);

            r = (byte)Mathf.Clamp(Mathf.RoundToInt((float)rdVal), 0, 255);
            g = (byte)Mathf.Clamp(Mathf.RoundToInt((float)grnVal), 0, 255);
            b = (byte)Mathf.Clamp(Mathf.RoundToInt((float)blVal), 0, 255);
            return true;
        }
    }
}
