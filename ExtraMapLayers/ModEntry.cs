﻿using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using xTile.Dimensions;
using xTile.Display;
using xTile.Layers;

namespace ExtraMapLayers
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {

        public static IMonitor PMonitor;
        public static IModHelper PHelper;
        private Harmony harmony;
        public static ModEntry context;
        public static ModConfig config;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            context = this;
            config = this.Helper.ReadConfig<ModConfig>();

            if (!config.EnableMod)
                return;

            PMonitor = this.Monitor;
            PHelper = helper;

            this.harmony = new Harmony(this.ModManifest.UniqueID);

            this.harmony.Patch(
               original: AccessTools.Method(typeof(Layer), nameof(Layer.Draw)),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Layer_Draw_Postfix))
            );
            this.harmony.Patch(
               original: AccessTools.Method(typeof(Layer), "DrawNormal"),
               transpiler: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Layer_DrawNormal_Transpiler))
            );
            this.Helper.Events.GameLoop.GameLaunched += this.GameLoop_GameLaunched;
        }

        private void GameLoop_GameLaunched(object sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
        {
            this.Monitor.Log($"device type {Game1.mapDisplayDevice?.GetType()}");

            var pytkapi = this.Helper.ModRegistry.GetApi("Platonymous.Toolkit");
            if(pytkapi != null)
            {
                this.Monitor.Log($"patching pytk");
                this.harmony.Patch(
                   original: AccessTools.Method(pytkapi.GetType().Assembly.GetType("PyTK.Extensions.PyMaps"), "drawLayer", new Type[] { typeof(Layer), typeof(IDisplayDevice), typeof(Rectangle), typeof(int), typeof(Location), typeof(bool) }),
                   prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.PyTK_drawLayer_Prefix))
                );
                this.harmony.Patch(
                   original: AccessTools.Method(pytkapi.GetType().Assembly.GetType("PyTK.Types.PyDisplayDevice"), "DrawTile"),
                   prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.DrawTile_Prefix))
                );
            }
            this.harmony.Patch(
               original: AccessTools.Method(Game1.mapDisplayDevice.GetType(), "DrawTile"),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.DrawTile_Prefix))
            );
        }

        public static IEnumerable<CodeInstruction> Layer_DrawNormal_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            PMonitor.Log("Transpiling Layer_DrawNormal");
            List<CodeInstruction> codes = new(instructions);
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method("String:Equals", new Type[]{typeof(string)}) && codes[i - 1].opcode == OpCodes.Ldstr && (string)codes[i - 1].operand == "Front")
                {
                    PMonitor.Log("switching equals to startswith for layer id");
                    codes[i].operand = AccessTools.Method("String:StartsWith", new Type[] { typeof(string) });
                    break;
                }
            }
            return codes.AsEnumerable();
        }

        private static int GetLayerOffset(string layerName)
        {
            return layerName.StartsWith("Front") ? 16 : 0;
        }

        public static int thisLayerDepth = 0;
        public static void Layer_Draw_Postfix(Layer __instance, IDisplayDevice displayDevice, Rectangle mapViewport, Location displayOffset, bool wrapAround, int pixelZoom)
        {
            if (!config.EnableMod || char.IsDigit(__instance.Id, __instance.Id.Length - 1))
                return;

            foreach (Layer layer in Game1.currentLocation.Map.Layers)
            {
                if (layer.Id.StartsWith(__instance.Id) && int.TryParse(layer.Id[__instance.Id.Length..], out int layerIndex))
                {
                    thisLayerDepth = layerIndex;
                    layer.Draw(displayDevice, mapViewport, displayOffset, wrapAround, pixelZoom);
                    thisLayerDepth = 0;
                }
            }
        }

        private static void DrawTile_Prefix(ref float layerDepth)
        {
            if (!config.EnableMod || thisLayerDepth == 0)
                return;

            layerDepth += thisLayerDepth / 10000f;
        }
       private static bool PyTK_drawLayer_Prefix(Layer layer)
            => (!config.EnableMod || !char.IsDigit(layer.Id, layer.Id.Length - 1));
        
    }
}